"""The site's own JSON endpoints: auth guards, and the public reads anyone can make.

The game servers' API lives in test_gameserver_api.py."""

from src.core import models as data
from src.core.db import session
from src.core.settings import settings


def test_ping(client):
    r = client.get("/ping")
    assert r.status_code == 200
    assert r.text == "Pong"


def test_build(client):
    r = client.get("/build")
    assert r.status_code == 200
    assert r.text == settings.build


def test_api_login_required_without_uuid(client):
    r = client.get("/queue/quit")
    assert r.status_code == 401
    assert r.json()["error"] == "Login required"


def test_api_login_bad_uuid_is_rejected(client):
    r = client.get("/queue/quit", params={"uuid": "not-a-real-uuid"})
    assert r.status_code == 401


def test_api_login_via_query_uuid(client, user_factory):
    user = user_factory()
    r = client.get("/queue/quit", params={"uuid": user.uuid})
    assert r.status_code == 200
    assert r.text == "success"


def test_server_bootstrap_keeps_the_region_already_stored(client):
    data.AuthorizedServer.bootstrap({"testclient": "paris"})
    data.AuthorizedServer.bootstrap({"testclient": "utah"})

    with session() as db:
        servers = db.query(data.AuthorizedServer).all()

    assert [(s.address, s.region) for s in servers] == [("testclient", "paris")]


def test_change_uuid_rotates_uuid(client, user_factory):
    user = user_factory()
    r = client.post("/user/changeuuid", params={"uuid": user.uuid})
    assert r.status_code == 200
    body = r.json()
    assert body["uuid"] != user.uuid


def test_change_uuid_refuses_get(client, user_factory):
    """A safe method may not rotate a session: a prefetch would log the account out.

    What GET answers is not the point, there is no GET handler for the path, so it
    falls through to the /user/{steam64} profile route and is refused there. The point
    is that it rotates nothing.
    """
    user = user_factory()
    r = client.get("/user/changeuuid", params={"uuid": user.uuid})
    assert r.status_code != 200
    with session() as db:
        assert db.query(data.User).where(data.User.uuid == user.uuid).first()


def test_match_current_404_without_match(client, user_factory):
    user = user_factory()
    r = client.get("/match/current", params={"uuid": user.uuid})
    assert r.status_code == 404
    assert r.json()["error"] == "No match found."


def test_match_current_poll_auto_resolves_an_expired_pick(client, user_factory):
    """Nobody has to be watching for this to fire, the very next poll from either
    player's own screen is what notices the deadline passed and moves the match on."""
    from time import time
    from sqlmodel import Session
    from src.core import db as core_db

    first, second = user_factory(), user_factory()
    with session() as db:
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

    r = client.get("/match/current", params={"uuid": first.uuid})
    assert r.status_code == 200
    body = r.json()
    assert body["params"]["ballistics"] is not None
    assert body["currentPick"] == str(second.steam64)

    with Session(core_db.engine) as s:
        stored = s.query(data.Match).where(data.Match.id == matchId).first()
        assert stored.meta["ballistics"] is not None


def test_public_kit_data_requires_no_auth(client, user_factory):
    player = user_factory()
    r = client.get(f"/kit/{player.steam64}")
    assert r.status_code == 200
    assert set(r.json()) == set(settings.kits["kits"])


def test_public_kit_data_works_for_a_player_with_no_account(client):
    """A steam64 that's never touched the site still gets a fully-resolved default
    loadout back, the lobby plugin has no way to create an account first."""
    r = client.get("/kit/76561197960287930")
    assert r.status_code == 200
    assert set(r.json()) == set(settings.kits["kits"])


def test_public_reputation_returns_elo(client, user_factory):
    player = user_factory()
    data.Stat.append(player.steam64, "unt", elo=42)

    r = client.get(f"/reputation/{player.steam64}")

    assert r.status_code == 200
    assert r.json()["elo"] == data.Stat.get_elo(player.steam64)


def test_public_reputation_defaults_for_an_unknown_player(client):
    """The lobby asks about anyone who connects, including players who have
    never had a ranked match, they get the starting Elo, not a 404."""
    r = client.get("/reputation/76561197960287930")

    assert r.status_code == 200
    assert r.json()["elo"] == 300


def test_public_reputation_steam64_is_a_string(client, user_factory):
    """A steam64 exceeds Number.MAX_SAFE_INTEGER, so it must not go out as a
    JSON number for any browser-side caller."""
    player = user_factory()
    r = client.get(f"/reputation/{player.steam64}")

    assert r.json()["steam64"] == str(player.steam64)
