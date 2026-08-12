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


def test_party_members_land_on_the_same_team(user_factory):
    """Casual Duo is 2 teams of 2 — a partied pair queueing under the same partyId
    must end up on one team together, never split across both."""
    DUO_GAMEMODE = 3
    DUO_BIT = 2**DUO_GAMEMODE
    partyA = [user_factory(), user_factory()]
    partyB = [user_factory(), user_factory()]

    for player in partyA:
        data.User.addToQueue(player.steam64, DUO_BIT, partyId=101, region="paris")
    for player in partyB:
        data.User.addToQueue(player.steam64, DUO_BIT, partyId=202, region="paris")

    assert matchmaking.attemptCreateMatch(partyA[0].steam64, "paris") is True

    with Session(core_db.engine) as s:
        match = s.query(data.Match).first()
        assert match.gamemode == DUO_GAMEMODE
        teamA = next(t for t in match.teams if str(partyA[0].steam64) in t["users"])
        assert str(partyA[1].steam64) in teamA["users"]
        assert str(partyB[0].steam64) not in teamA["users"]


def test_oversized_party_never_matches(user_factory):
    """A 3-person party can't fit Casual Duo's 2-per-team cap — it must simply wait
    forever rather than ever being split across teams to make it fit."""
    DUO_BIT = 2**3
    trio = [user_factory(), user_factory(), user_factory()]
    for player in trio:
        data.User.addToQueue(player.steam64, DUO_BIT, partyId=101, region="paris")
    # Enough solo players queued alongside to fill both teams if the trio were
    # (wrongly) allowed to split.
    solo = [user_factory() for _ in range(2)]
    for player in solo:
        data.User.addToQueue(player.steam64, DUO_BIT, region="paris")

    assert matchmaking.attemptCreateMatch(trio[0].steam64, "paris") is False
    with Session(core_db.engine) as s:
        assert s.query(data.Match).count() == 0


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


def test_match_results_record_headshots_and_damage(user_factory):
    winner, loser = user_factory(), user_factory()

    matchmaking.matchResults(
        RANKED,
        {
            "steam64": winner.steam64,
            "win": True,
            "kills": 5,
            "deaths": 2,
            "headshots": 3,
            "damageDealt": 620,
            "damageTaken": 140,
        },
        {"steam64": loser.steam64, "win": False, "kills": 2, "deaths": 5},
    )

    with Session(core_db.engine) as s:
        won = s.query(data.Stat).where(data.Stat.steam64 == winner.steam64).first()
        lost = s.query(data.Stat).where(data.Stat.steam64 == loser.steam64).first()
        assert won.headshots == 3
        assert won.damageDealt == 620
        assert won.damageTaken == 140
        # A payload without the newer fields (an older plugin build) must not error
        # and must simply record zero rather than crash on a missing key.
        assert lost.headshots == 0


def test_elo_never_drops_below_floor(user_factory):
    loser = user_factory()

    for _ in range(10):
        matchmaking.matchResults(
            RANKED, {"steam64": loser.steam64, "win": False, "kills": 0, "deaths": 10}
        )

    with Session(core_db.engine) as s:
        stat = s.query(data.Stat).where(data.Stat.steam64 == loser.steam64).first()
        assert stat.elo >= 100


def test_match_without_matchsettings_gets_a_pick_deadline(user_factory):
    """Matchmaking never passes matchSettings, so ballistics/movement start undecided —
    that's exactly when a deadline is needed, or a silent player could stall forever."""
    first, second = user_factory(), user_factory()
    with core_db.open() as db:
        match = data.Match(
            gamemode=0,
            teams=[
                {"name": "one", "users": [str(first.steam64)]},
                {"name": "two", "users": [str(second.steam64)]},
            ],
        )
        db.add(match)

    with Session(core_db.engine) as s:
        loaded = s.query(data.Match).first()
        assert loaded.meta["pickDeadline"] is not None
        assert loaded.meta["pickDeadline"] <= time() + settings.mmconfig["pickTimeout"]


def test_match_with_full_matchsettings_gets_no_pick_deadline(user_factory):
    first, second = user_factory(), user_factory()
    with core_db.open() as db:
        match = data.Match(
            gamemode=0,
            teams=[
                {"name": "one", "users": [str(first.steam64)]},
                {"name": "two", "users": [str(second.steam64)]},
            ],
            matchSettings={"ballistics": True, "map": "Alpha Valley", "movement": True},
        )
        db.add(match)

    with Session(core_db.engine) as s:
        assert s.query(data.Match).first().meta.get("pickDeadline") is None


def test_expired_pick_is_not_resolved_before_its_deadline(user_factory):
    first, second = user_factory(), user_factory()
    with core_db.open() as db:
        match = data.Match(
            gamemode=0,
            teams=[
                {"name": "one", "users": [str(first.steam64)]},
                {"name": "two", "users": [str(second.steam64)]},
            ],
        )
        db.add(match)

    with Session(core_db.engine) as s:
        assert s.query(data.Match).first().resolvedMetaIfExpired() is None


def test_expired_pick_auto_resolves_to_the_gamemode_default_and_advances_turn(
    user_factory,
):
    """A player who never clicks Yes/No must not be able to hang the whole match —
    once the deadline passes, the pick resolves itself to the gamemode's first
    configured option (the same fallback kits/powerups already use) and turn passes
    on, exactly as if the stalling player had picked."""
    first, second = user_factory(), user_factory()
    with core_db.open() as db:
        match = data.Match(
            gamemode=0,
            teams=[
                {"name": "one", "users": [str(first.steam64)]},
                {"name": "two", "users": [str(second.steam64)]},
            ],
        )
        db.add(match)
        db.flush()
        matchId = match.id
        db.query(data.Match).where(data.Match.id == matchId).update(
            {"meta": match.meta | {"pickDeadline": time() - 1}}
        )

    with Session(core_db.engine) as s:
        loaded = s.query(data.Match).where(data.Match.id == matchId).first()
        resolved = loaded.resolvedMetaIfExpired()
        assert resolved is not None
        assert resolved["ballistics"] == RANKED["ballistics"][0]
        assert resolved["movement"] is None  # only the first outstanding field resolves
        assert resolved["currentPick"] == str(second.steam64)
        # movement is still outstanding, so the next field gets its own full window
        assert resolved["pickDeadline"] > time()


def test_expired_pick_clears_the_deadline_once_everything_is_decided(user_factory):
    first, second = user_factory(), user_factory()
    with core_db.open() as db:
        match = data.Match(
            gamemode=0,
            teams=[
                {"name": "one", "users": [str(first.steam64)]},
                {"name": "two", "users": [str(second.steam64)]},
            ],
        )
        db.add(match)
        db.flush()
        matchId = match.id
        db.query(data.Match).where(data.Match.id == matchId).update(
            {
                "meta": match.meta
                | {"pickDeadline": time() - 1, "ballistics": True}
            }
        )

    with Session(core_db.engine) as s:
        loaded = s.query(data.Match).where(data.Match.id == matchId).first()
        resolved = loaded.resolvedMetaIfExpired()
        assert resolved["movement"] == RANKED["movement"][0]
        assert resolved["pickDeadline"] is None


def test_a_real_pick_resets_the_deadline_for_the_next_field(user_factory):
    first, second = user_factory(), user_factory()
    with core_db.open() as db:
        match = data.Match(
            gamemode=0,
            teams=[
                {"name": "one", "users": [str(first.steam64)]},
                {"name": "two", "users": [str(second.steam64)]},
            ],
        )
        db.add(match)
        db.flush()
        matchId = match.id

    with Session(core_db.engine) as s:
        loaded = s.query(data.Match).where(data.Match.id == matchId).first()
        result = loaded.getReturnedMeta(first.steam64, pick="1")
        assert result["ballistics"] is True
        assert result["movement"] is None
        assert result["pickDeadline"] > time()  # a fresh window for movement


def test_match_cannot_be_claimed_early_with_picks_outstanding(user_factory):
    first, second = user_factory(), user_factory()
    with core_db.open() as db:
        match = data.Match(
            gamemode=0,
            teams=[
                {"name": "one", "users": [str(first.steam64)]},
                {"name": "two", "users": [str(second.steam64)]},
            ],
            matchSettings={"ballistics": True, "map": "Alpha Valley", "movement": True},
        )
        db.add(match)

    with Session(core_db.engine) as s:
        loaded = s.query(data.Match).first()
        assert loaded.allKitsLocked() is False
        assert loaded.canBeClaimed() is False


def test_match_starts_early_once_every_kit_and_powerup_is_locked(user_factory):
    """Waiting out the full `selectKitsTime` timer is pointless once nobody has
    anything left to decide — the match should be claimable immediately."""
    first, second = user_factory(), user_factory()
    with core_db.open() as db:
        match = data.Match(
            gamemode=0,
            teams=[
                {"name": "one", "users": [str(first.steam64)]},
                {"name": "two", "users": [str(second.steam64)]},
            ],
            matchSettings={"ballistics": True, "map": "Alpha Valley", "movement": True},
        )
        db.add(match)
        db.flush()
        matchId = match.id
        db.query(data.Match).where(data.Match.id == matchId).update(
            {
                "meta": match.meta
                | {
                    "userData": {
                        str(first.steam64): {"kit": "meta", "powerup": "Ammo"},
                        str(second.steam64): {"kit": "sniper", "powerup": "Doctor"},
                    }
                }
            }
        )

    with Session(core_db.engine) as s:
        loaded = s.query(data.Match).where(data.Match.id == matchId).first()
        assert loaded.allKitsLocked() is True
        assert loaded.canBeClaimed() is True


def test_append_kill_creates_and_grows_the_match_result(user_factory):
    killer, victim = user_factory(), user_factory()
    with core_db.open() as db:
        match = data.Match(
            gamemode=0,
            teams=[
                {"name": "one", "users": [str(killer.steam64)]},
                {"name": "two", "users": [str(victim.steam64)]},
            ],
            matchSettings={"ballistics": True, "map": "Alpha Valley", "movement": True},
        )
        db.add(match)
        db.flush()
        matchId = match.id

    data.MatchResult.appendKill(
        matchId,
        {"killer": str(killer.steam64), "victim": str(victim.steam64), "limb": "skull"},
    )
    data.MatchResult.appendKill(
        matchId,
        {"killer": str(killer.steam64), "victim": str(victim.steam64), "limb": "spine"},
    )

    with Session(core_db.engine) as s:
        result = (
            s.query(data.MatchResult).where(data.MatchResult.matchId == matchId).first()
        )
        assert result.data["map"] == "Alpha Valley"
        assert len(result.data["kills"]) == 2
        assert result.data["kills"][0]["limb"] == "skull"


def test_record_final_results_merges_into_the_same_row_as_kills(user_factory):
    killer, victim = user_factory(), user_factory()
    with core_db.open() as db:
        match = data.Match(
            gamemode=0,
            teams=[
                {"name": "one", "users": [str(killer.steam64)]},
                {"name": "two", "users": [str(victim.steam64)]},
            ],
            matchSettings={"ballistics": True, "map": "Alpha Valley", "movement": True},
        )
        db.add(match)
        db.flush()
        matchId = match.id

    data.MatchResult.appendKill(
        matchId, {"killer": str(killer.steam64), "victim": str(victim.steam64)}
    )
    data.MatchResult.recordFinalResults(
        matchId, {str(killer.steam64): {"Kills": 1, "Deaths": 0, "Won": True}}
    )

    with Session(core_db.engine) as s:
        result = (
            s.query(data.MatchResult).where(data.MatchResult.matchId == matchId).first()
        )
        assert len(result.data["kills"]) == 1
        assert result.data["results"][str(killer.steam64)]["Won"] is True


def test_latest_for_finds_matches_a_player_has_a_result_in(user_factory):
    player, other = user_factory(), user_factory()
    with core_db.open() as db:
        match = data.Match(
            gamemode=0, teams=[{"name": "one", "users": [str(player.steam64)]}]
        )
        db.add(match)
        db.flush()
        matchId = match.id

    data.MatchResult.recordFinalResults(matchId, {str(player.steam64): {"Kills": 3}})

    found = data.MatchResult.latestFor(player.steam64)
    assert len(found) == 1
    assert found[0].matchId == matchId
    assert data.MatchResult.latestFor(other.steam64) == []


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
