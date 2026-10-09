"""MatchPlayers: a player's match is one indexed lookup, kept in step with Matches."""

from src.core import db as core_db
from src.core import models as data


def _match(*players, finished=False) -> int:
    with core_db.session() as db:
        match = data.Match(
            gamemode=0,
            teams=[
                {"name": f"t{i}", "users": [str(p.steam64)]}
                for i, p in enumerate(players)
            ],
        )
        match.finished = finished
        db.add(match)
        db.flush()
        return match.id


def test_a_new_match_writes_its_players(user_factory):
    first, second = user_factory(), user_factory()
    matchId = _match(first, second)

    assert data.Match.current_for(first.steam64) == matchId
    assert data.Match.players_in_a_match() == {str(first.steam64), str(second.steam64)}


def test_a_finished_match_is_nobody_s_current_match(user_factory):
    player = user_factory()
    _match(player, finished=True)

    assert data.Match.current_for(player.steam64) is None
    assert data.Match.players_in_a_match() == set()


def test_backfill_fills_an_empty_table_from_the_teams(user_factory):
    player = user_factory()
    matchId = _match(player)
    with core_db.session() as db:
        db.query(data.MatchPlayer).delete()

    data.MatchPlayer.backfill()

    assert data.Match.current_for(player.steam64) == matchId
