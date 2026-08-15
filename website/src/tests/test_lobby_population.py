"""What the site knows about how busy the arenas are, and what Discord is told.

The queue deliberately is not this number: a lobby holds players back from queueing
to keep its own browser listing worth joining, so a full arena can honestly have
nobody queueing in it. Everything here exists so the counter never reports an empty
server that has six people fighting in it.
"""

from time import time

import pytest

from src.core import models as data
from src.core.db import open as opendb
from src.core.notify import _counter_name


@pytest.fixture
def gameserver(client):
    """A client whose IP is allow-listed as a paris game server."""
    data.AuthorizedServer.bootstrap({"testclient": "paris"})
    return client


def report(gameserver, players):
    r = gameserver.post("/server/v1/lobby/population", json={"players": players})
    assert r.status_code == 200, r.text
    return r.json()


def test_a_lobby_reporting_its_population_is_counted(gameserver):
    report(gameserver, 6)

    assert data.LobbyPopulation.countFresh() == 6


def test_reporting_again_replaces_rather_than_adds(gameserver):
    report(gameserver, 6)
    report(gameserver, 3)

    assert data.LobbyPopulation.countFresh() == 3


def test_every_reporting_lobby_is_added_up(gameserver):
    report(gameserver, 4)
    data.AuthorizedServer.bootstrap({"10.0.0.9": "utah"})
    data.LobbyPopulation.report("10.0.0.9", 5)

    assert data.LobbyPopulation.countFresh() == 9


def test_nobody_anywhere_counts_as_nobody():
    assert data.LobbyPopulation.countFresh() == 0


def test_an_unauthorized_server_cannot_report(client):
    r = client.post("/server/v1/lobby/population", json={"players": 99})

    assert r.status_code == 401
    assert data.LobbyPopulation.countFresh() == 0


def test_a_lobby_that_stopped_reporting_is_not_counted(gameserver):
    """A lobby that crashed with eleven people in it would otherwise keep
    advertising them for as long as its row survives."""
    report(gameserver, 11)
    with opendb() as db:
        row = db.query(data.LobbyPopulation).first()
        row.at = int(time()) - 600

    assert data.LobbyPopulation.countFresh() == 0


def makeMatch(first, second):
    with opendb() as db:
        db.add(
            data.Match(
                gamemode=0,
                teams=[
                    {"name": "one", "users": [str(first.steam64)]},
                    {"name": "two", "users": [str(second.steam64)]},
                ],
            )
        )


def test_players_in_a_live_match_are_playing(user_factory):
    """Match servers are never asked. The site handed out the roster, so it already
    knows, which is what keeps twelve match servers out of this entirely."""
    makeMatch(user_factory(), user_factory())

    assert data.LobbyPopulation.playingNow() == 2


def test_arena_and_match_players_are_added_together(gameserver, user_factory):
    report(gameserver, 3)
    makeMatch(user_factory(), user_factory())

    assert data.LobbyPopulation.playingNow() == 5


def test_a_finished_match_is_nobody(user_factory):
    makeMatch(user_factory(), user_factory())
    with opendb() as db:
        db.query(data.Match).update({"finished": True})

    assert data.LobbyPopulation.playingNow() == 0


@pytest.mark.parametrize(
    "playing, queued, expected",
    [
        (4, 0, "4 players on PeekSeek.net"),
        (1, 0, "1 player on PeekSeek.net"),
        # Playing wins over queueing: it is the stronger thing that can be said, and
        # the queued are a subset of the same evening either way.
        (6, 2, "6 players on PeekSeek.net"),
        (0, 3, "3 in queue"),
        (0, 1, "1 in queue"),
    ],
)
def test_the_counter_leads_with_the_best_true_thing(playing, queued, expected):
    assert _counter_name(playing, queued) == expected


def test_the_counter_never_announces_a_zero():
    """With nothing to report it says neither number rather than posting a 0. It is
    allowed to decline to give a figure; it is not allowed to invent one."""
    name = _counter_name(0, 0)

    assert "0" not in name
    assert name == "queue is open"
