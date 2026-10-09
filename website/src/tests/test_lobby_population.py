"""What the site knows about how busy the arenas are, and what Discord is told.

The queue deliberately is not this number: a lobby holds players back from queueing
to keep its own browser listing worth joining, so a full arena can honestly have
nobody queueing in it. Everything here exists so the counter never reports an empty
server that has six people fighting in it.
"""

from time import time

import pytest

from src.core import models as data
from src.core.db import session
from src.core.notify import _counter_name


@pytest.fixture
def gameserver(client):
    """A client whose IP is allow-listed as a paris game server."""
    data.AuthorizedServer.bootstrap({"testclient": "paris"})
    return client


def report(gameserver, players, **counts):
    r = gameserver.post(
        "/server/v1/lobby/population", json={"players": players, **counts}
    )
    assert r.status_code == 200, r.text
    return r.json()


def readings(address="testclient"):
    with session() as db:
        return (
            db.query(data.ArenaReading)
            .where(data.ArenaReading.address == address)
            .order_by(data.ArenaReading.id)
            .all()
        )


def test_a_lobby_reporting_its_population_is_counted(gameserver):
    report(gameserver, 6)

    assert data.LobbyPopulation.count_fresh() == 6


def test_reporting_again_replaces_rather_than_adds(gameserver):
    report(gameserver, 6)
    report(gameserver, 3)

    assert data.LobbyPopulation.count_fresh() == 3


def test_every_reporting_lobby_is_added_up(gameserver):
    report(gameserver, 4)
    data.AuthorizedServer.bootstrap({"10.0.0.9": "utah"})
    data.LobbyPopulation.report("10.0.0.9", 5)

    assert data.LobbyPopulation.count_fresh() == 9


def test_nobody_anywhere_counts_as_nobody():
    assert data.LobbyPopulation.count_fresh() == 0


def test_an_unauthorized_server_cannot_report(client):
    r = client.post("/server/v1/lobby/population", json={"players": 99})

    assert r.status_code == 401
    assert data.LobbyPopulation.count_fresh() == 0


def test_a_lobby_that_stopped_reporting_is_not_counted(gameserver):
    """A lobby that crashed with eleven people in it would otherwise keep
    advertising them for as long as its row survives."""
    report(gameserver, 11)
    with session() as db:
        row = db.query(data.LobbyPopulation).first()
        row.at = int(time()) - 600

    assert data.LobbyPopulation.count_fresh() == 0


def make_match(first, second):
    with session() as db:
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
    make_match(user_factory(), user_factory())

    assert data.LobbyPopulation.playing_now() == 2


def test_arena_and_match_players_are_added_together(gameserver, user_factory):
    report(gameserver, 3)
    make_match(user_factory(), user_factory())

    assert data.LobbyPopulation.playing_now() == 5


def test_a_finished_match_is_nobody(user_factory):
    make_match(user_factory(), user_factory())
    with session() as db:
        db.query(data.Match).update({"finished": True})

    assert data.LobbyPopulation.playing_now() == 0


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


def test_a_report_is_kept_as_history_as_well_as_a_current_reading(gameserver):
    """Both, because they answer different questions: the counter wants to know what is
    true now, and everything about why there are not more matches is only readable from
    what was true over time."""
    report(gameserver, 3, queueing=3, held=1)

    assert data.LobbyPopulation.count_fresh() == 3
    assert [(r.players, r.queueing, r.held) for r in readings()] == [(3, 3, 1)]


def test_saying_the_same_thing_again_is_not_worth_a_row(gameserver):
    """A lobby reports every five seconds and an arena mostly does not change, so
    without this a quiet night is seventeen thousand identical rows."""
    report(gameserver, 2, queueing=2, held=2)
    report(gameserver, 2, queueing=2, held=2)
    report(gameserver, 2, queueing=2, held=2)

    assert len(readings()) == 1


def test_the_hold_changing_is_worth_a_row_even_when_nobody_arrived(gameserver):
    """Two players who both start wanting a match is the whole event being measured, and
    the population is identical either side of it."""
    report(gameserver, 2, queueing=0, held=0)
    report(gameserver, 2, queueing=2, held=2)

    assert [(r.queueing, r.held) for r in readings()] == [(0, 0), (2, 2)]


def test_a_lobby_that_reports_no_counts_stores_none_rather_than_zero(gameserver):
    """A build that predates these fields has not said nobody wanted a match, it has
    said nothing, and a zero here would be the site inventing the answer."""
    report(gameserver, 4)

    reading = readings()[0]
    assert reading.players == 4
    assert reading.queueing is None
    assert reading.held is None


def woke_up(previous_players, seconds_ago, now_players=1):
    """Whether a lobby reporting `now_players` counts as waking, given what it last
    said and how long ago it said it."""
    previous = data.ArenaReading(
        address="testclient",
        at=int(time()) - seconds_ago,
        players=previous_players,
    )
    return data.ArenaReading._is_waking_up(previous, now_players)


def test_an_arena_that_has_been_dead_for_hours_is_worth_announcing():
    assert woke_up(previous_players=0, seconds_ago=7200)


def test_a_player_bouncing_off_an_empty_server_is_not(gameserver):
    """People join an empty server, find nobody, and leave within seconds. Announcing
    each of those teaches everybody to ignore the announcement that matters."""
    assert not woke_up(previous_players=0, seconds_ago=20)


def test_an_arena_that_was_already_busy_is_not_waking_up():
    assert not woke_up(previous_players=3, seconds_ago=7200, now_players=4)


def test_an_arena_emptying_is_never_an_announcement():
    assert not woke_up(previous_players=2, seconds_ago=7200, now_players=0)


def test_a_lobby_nobody_has_ever_heard_from_is_treated_as_quiet():
    """A server coming back up with people already in it is exactly the news this
    exists to carry."""
    assert data.ArenaReading._is_waking_up(None, 2)
    assert not data.ArenaReading._is_waking_up(None, 0)


def test_waking_up_is_announced_once_and_not_again(gameserver, monkeypatch):
    posted = []
    monkeypatch.setattr(
        "src.core.notify.send_discord_channel",
        lambda channel, **kwargs: posted.append(kwargs.get("content")),
    )

    report(gameserver, 1, queueing=1, held=1)
    report(gameserver, 2, queueing=2, held=2)

    assert len(posted) == 1
    assert "1 player" in posted[0]
