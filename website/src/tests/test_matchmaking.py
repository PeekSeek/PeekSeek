"""The full matchmaking loop: queue -> match -> dequeue -> results -> cancel."""

from time import time

from sqlmodel import Session

from src.core import db as core_db
from src.core import matchmaking
from src.core import models as data
from src.core.settings import settings

GAMEMODE_BIT = 1  # bitmask selecting gamemode 0 (Competitive, 1v1, ranked)
RANKED = settings.mmconfig["gamemodes"][0]


def test_two_queued_players_get_matched(user_factory):
    first, second = user_factory(), user_factory()
    data.User.addToQueue(first.steam64, GAMEMODE_BIT, region="paris")
    data.User.addToQueue(second.steam64, GAMEMODE_BIT, region="paris")

    assert matchmaking.attemptCreateMatch(first.steam64, "paris") is True

    with Session(core_db.engine) as s:
        match = s.query(data.Match).first()
        players = {u for team in match.teams for u in team["users"]}
        assert players == {str(first.steam64), str(second.steam64)}
        assert len(match.teams) == 2
        assert s.query(data.UserHB).count() == 0, "players must leave the queue"


def test_lone_player_is_not_matched(user_factory):
    player = user_factory()
    data.User.addToQueue(player.steam64, GAMEMODE_BIT, region="paris")

    assert not matchmaking.attemptCreateMatch(player.steam64, "paris")
    with Session(core_db.engine) as s:
        assert s.query(data.Match).count() == 0
        assert s.query(data.UserHB).count() == 1


def test_region_mismatch_does_not_match(user_factory):
    first, second = user_factory(), user_factory()
    data.User.addToQueue(first.steam64, GAMEMODE_BIT, region="paris")
    data.User.addToQueue(second.steam64, GAMEMODE_BIT, region="utah")

    assert not matchmaking.attemptCreateMatch(first.steam64, "paris")
    with Session(core_db.engine) as s:
        assert s.query(data.Match).count() == 0


def test_stale_heartbeats_are_pruned(user_factory):
    player = user_factory()
    data.User.addToQueue(player.steam64, GAMEMODE_BIT, region="paris")
    with Session(core_db.engine) as s:
        s.query(data.UserHB).update({"time": int(time()) - settings.queue_timeout - 1})
        s.commit()

    assert not matchmaking.attemptCreateMatch(player.steam64, "paris")
    with Session(core_db.engine) as s:
        assert s.query(data.UserHB).count() == 0


def test_match_results_update_stats_and_elo(user_factory):
    winner, loser = user_factory(), user_factory()

    results, avgElo = matchmaking.matchResults(
        RANKED,
        {"steam64": winner.steam64, "win": True, "kills": 5, "deaths": 2},
        {"steam64": loser.steam64, "win": False, "kills": 2, "deaths": 5},
    )

    assert avgElo == 300
    assert results[winner.steam64]["Elo Change"] > 0
    assert results[loser.steam64]["Elo Change"] < 0
    with Session(core_db.engine) as s:
        won = s.query(data.Stat).where(data.Stat.steam64 == winner.steam64).first()
        lost = s.query(data.Stat).where(data.Stat.steam64 == loser.steam64).first()
        assert won.wins == 1 and won.kills == 5 and won.elo > 300
        assert lost.losses == 1 and lost.deaths == 5 and lost.elo < 300


def test_elo_never_drops_below_floor(user_factory):
    loser = user_factory()

    for _ in range(10):
        matchmaking.matchResults(
            RANKED, {"steam64": loser.steam64, "win": False, "kills": 0, "deaths": 10}
        )

    with Session(core_db.engine) as s:
        stat = s.query(data.Stat).where(data.Stat.steam64 == loser.steam64).first()
        assert stat.elo >= 100


def test_unclaimed_match_cancelled_after_timeout(user_factory):
    first, second = user_factory(), user_factory()
    with core_db.open() as db:
        db.add(
            data.Match(
                gamemode=0,
                teams=[
                    {"name": "one", "users": [str(first.steam64)]},
                    {"name": "two", "users": [str(second.steam64)]},
                ],
            )
        )
    with Session(core_db.engine) as s:
        s.query(data.Match).update({"time": int(time()) - settings.claim_timeout - 1})
        s.commit()

    data.Match.cancelStaleUnclaimed()

    with Session(core_db.engine) as s:
        assert s.query(data.Match).first().finished is True
