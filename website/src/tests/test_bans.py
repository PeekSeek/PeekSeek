"""A ban has to keep somebody out of matches, not just off the website.

Every one of these covers a path that was open in production: a permanently banned
player queued from in-game for six weeks and played twenty ranked matches, because
the only ban check the site had lived in `load_user`, which a game server never
triggers — it carries a server token, not a player session.
"""

from time import time

import pytest
from sqlmodel import Session

from src.core import models as data
from src.core.db import engine, open as opendb


@pytest.fixture
def gameserver(client):
    """A client whose IP is allow-listed as a paris game server."""
    data.AuthorizedServer.bootstrap({"testclient": "paris"})
    return client


def ban(steam64, endTime=None, reason="Cheating"):
    with Session(engine) as session:
        session.add(
            data.UserBan(
                steam64=steam64,
                endTime=endTime if endTime is not None else data.UserBan.PERMANENT,
                reason=reason,
            )
        )
        session.commit()


def test_a_banned_player_cannot_be_queued_by_a_game_server(gameserver, user_factory):
    player = user_factory()
    ban(player.steam64)

    gameserver.put(f"/server/v1/players/{player.steam64}/queue")

    with opendb() as db:
        assert db.query(data.UserHB).count() == 0


def test_the_game_server_is_told_why(gameserver, user_factory):
    player = user_factory()
    ban(player.steam64, reason="Aimbot")

    body = gameserver.put(f"/server/v1/players/{player.steam64}/queue").json()

    assert body["ban"] == {
        "reason": "Aimbot",
        "expires": data.UserBan.PERMANENT,
        "permanent": True,
    }


def test_an_unbanned_player_still_queues(gameserver, user_factory):
    player = user_factory()

    body = gameserver.put(f"/server/v1/players/{player.steam64}/queue").json()

    assert body["ban"] is None
    with opendb() as db:
        assert db.query(data.UserHB).count() == 1


def test_an_expired_ban_does_not_block(gameserver, user_factory):
    player = user_factory()
    ban(player.steam64, endTime=int(time()) - 60)

    gameserver.put(f"/server/v1/players/{player.steam64}/queue")

    with opendb() as db:
        assert db.query(data.UserHB).count() == 1


def test_a_ban_landing_mid_queue_drops_them_out(gameserver, user_factory):
    """The row was written before anyone banned them, so nothing else reconsiders it."""
    player = user_factory()
    gameserver.put(f"/server/v1/players/{player.steam64}/queue")
    ban(player.steam64)

    with opendb() as db:
        assert data.UserHB.freshInQueue(db) == []
        assert db.query(data.UserHB).count() == 0


def test_matchmaking_never_seats_a_banned_player(user_factory):
    """The last line of defence: even holding a heartbeat, they are not match material."""
    banned = user_factory()
    ban(banned.steam64)
    data.User.addToQueue(banned.steam64, 1)

    with opendb() as db:
        queued = [hb.steam64 for hb in data.UserHB.freshInQueue(db)]

    assert banned.steam64 not in queued


def test_a_party_leader_cannot_drag_a_banned_member_into_the_queue(
    client, login, user_factory, party_factory
):
    leader = user_factory()
    member = user_factory()
    ban(member.steam64)
    party_factory(leader, member)
    login(client, leader)

    # Casual Duo (bit 8) — a party of two does not fit any 1v1 mode.
    body = client.get("/queue/heartbeat?gamemode=8&region=paris").json()

    assert body["refused"] == {str(member.steam64): "Cheating"}
    with opendb() as db:
        queued = {hb.steam64 for hb in db.query(data.UserHB).all()}
    assert member.steam64 not in queued
    assert leader.steam64 in queued


def test_activeFor_prefers_the_ban_that_lasts_longest(user_factory):
    """Two bans is normal — a player can be banned twice before either expires — and
    the one an admin needs to see is whichever keeps them out longest."""
    player = user_factory()
    ban(player.steam64, endTime=int(time()) + 60, reason="Toxicity")
    ban(player.steam64, reason="Cheating")

    assert data.UserBan.activeFor(player.steam64).reason == "Cheating"


def test_history_includes_expired_bans(user_factory):
    player = user_factory()
    ban(player.steam64, endTime=int(time()) - 60, reason="Toxicity")
    ban(player.steam64, reason="Cheating")

    assert {b.reason for b in data.UserBan.historyFor(player.steam64)} == {
        "Toxicity",
        "Cheating",
    }
