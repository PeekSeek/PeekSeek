"""A custom match made ahead of time, for a tie-breaker its players agreed on.

A game server takes any waiting match the moment it is free, so a match made at noon
for ten at night would be hosted at noon to nobody. A scheduled match waits for its
start time, shows on its players' match page ten minutes before, and gives them longer
to arrive than a match they queued into.
"""

from time import time

from src.core import models as data
from src.core.db import session
from src.core.models.match import mmconfig


def _schedule(client, login, user_factory, startsAt, region="brazil"):
    owner = user_factory(discord=111)
    other = user_factory(discord=222)
    owner.set_permission("premium", True)
    login(client, owner)
    r = client.post(
        "/match/custom",
        json={
            "teams": {"a": [str(owner.steam64)], "b": [str(other.steam64)]},
            "region": region,
            "gamemode": 0,
            "ballistics": True,
            "map": "Alpha Valley",
            "movement": True,
            "startsAt": startsAt,
        },
    )
    return r, owner, other


def _match(matchId) -> data.Match:
    with session() as db:
        match = db.query(data.Match).where(data.Match.id == int(matchId)).first()
        db.expunge(match)
        return match


def _move(matchId, startsAt):
    with session() as db:
        db.query(data.Match).where(data.Match.id == int(matchId)).update(
            {"time": int(startsAt)}
        )


def test_a_scheduled_match_starts_at_its_start_time(client, login, user_factory):
    startsAt = int(time()) + 3600
    r, _, _ = _schedule(client, login, user_factory, startsAt)

    assert r.status_code == 200
    match = _match(r.text)
    assert match.time == startsAt
    assert match.scheduled


def test_a_start_time_in_the_past_or_far_off_is_refused(client, login, user_factory):
    for startsAt in (int(time()) - 60, int(time()) + 8 * 24 * 3600):
        r, _, _ = _schedule(client, login, user_factory, startsAt)
        assert r.status_code == 400


def test_no_server_takes_it_before_it_starts(client, login, user_factory):
    r, _, _ = _schedule(client, login, user_factory, int(time()) + 3600)

    assert data.Match.pending_for("brazil") is None

    _move(r.text, time() - 1)
    assert data.Match.pending_for("brazil").id == int(r.text)


def test_players_can_queue_until_ten_minutes_before(client, login, user_factory):
    r, owner, other = _schedule(client, login, user_factory, int(time()) + 3600)

    assert data.Match.current_for(owner.steam64) is None
    assert str(other.steam64) not in data.Match.players_in_a_match()
    assert data.User.add_to_queue(owner.steam64, 0, region="brazil") is None
    with session() as db:
        assert db.query(data.UserHB).where(data.UserHB.steam64 == owner.steam64).count()

    _move(r.text, time() + data.Match.SHOWN_BEFORE - 30)
    assert data.Match.current_for(owner.steam64) == int(r.text)
    assert str(other.steam64) in data.Match.players_in_a_match()


def test_it_gets_longer_to_pick_and_join(client, login, user_factory):
    startsAt = int(time()) + 3600
    r, _, _ = _schedule(client, login, user_factory, startsAt)

    assert _match(r.text).selection_ends_at() == (
        startsAt + mmconfig["selectKitsTime"] + mmconfig["scheduledLeniency"]
    )


def test_it_is_not_cancelled_while_it_waits(client, login, user_factory):
    r, _, _ = _schedule(client, login, user_factory, int(time()) + 3600)

    data.Match.cancel_stranded()
    assert not _match(r.text).finished

    _move(r.text, time() - mmconfig["selectKitsTime"] - 60)
    data.Match.cancel_stranded()
    assert not _match(r.text).finished, "the leniency still covers it"


def test_players_are_told_once_ten_minutes_before(
    client, login, user_factory, monkeypatch
):
    sent = []
    monkeypatch.setattr(
        "src.core.notify.send_discord_dm",
        lambda discordId, title, message, **kw: sent.append((discordId, title)),
    )
    r, _, _ = _schedule(client, login, user_factory, int(time()) + 3600)
    assert sent == [], "nobody is told a match started when it has not"

    data.Match.announce_scheduled()
    assert sent == []

    _move(r.text, time() + data.Match.SHOWN_BEFORE - 30)
    data.Match.announce_scheduled()
    data.Match.announce_scheduled()
    assert sorted(sent) == [
        (111, "Your match starts soon"),
        (222, "Your match starts soon"),
    ]


def test_the_match_page_knows_when_it_starts(client, login, user_factory):
    startsAt = int(time()) + 300
    r, owner, _ = _schedule(client, login, user_factory, startsAt)

    page = _match(r.text).local_json()
    assert page["scheduled"] is True
    assert page["startsAt"] == startsAt
    assert page["status"] == data.Match.PENDING
    assert client.get("/match").status_code == 200
