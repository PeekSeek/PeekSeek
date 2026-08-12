"""JSON/plain endpoints of the unified app: auth guards and frozen contracts."""

from src.core import models as data
from src.core.db import open as opendb
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


def test_unturned_endpoints_reject_unknown_ip(client):
    r = client.post("/unt/ban", params={"steam64": "123"})
    assert r.status_code == 401


def test_unturned_endpoints_accept_a_bootstrapped_ip(client):
    data.AuthorizedServer.bootstrap({"testclient": "paris"})

    r = client.post("/unt/ban", params={"steam64": "123"})
    assert r.status_code == 200


def test_ban_rejects_a_non_numeric_steam64(client):
    """steam64 must be typed int like everywhere else in the codebase — a bare str
    let a garbage value reach the UserBans table instead of failing cleanly."""
    data.AuthorizedServer.bootstrap({"testclient": "paris"})
    r = client.post("/unt/ban", params={"steam64": "not-a-number"})
    assert r.status_code == 422


def test_malformed_json_body_400s_instead_of_crashing(client):
    """The JSON body is parsed in a dependency, ahead of any route's own
    try/except — a decode error there must not fall through as a bare 500."""
    data.AuthorizedServer.bootstrap({"testclient": "paris"})
    r = client.post(
        "/unt/kills",
        params={"matchid": 1},
        content=b"not json",
        headers={"Content-Type": "application/json"},
    )
    assert r.status_code == 400


def test_eventwait_claims_a_match_with_the_server_code(client, user_factory):
    """`claimed` has to hold what a player can connect with, not the server's IP."""
    data.AuthorizedServer.bootstrap({"testclient": "paris"})
    first, second = user_factory(), user_factory()
    with opendb() as db:
        db.add(
            data.Match(
                gamemode=0,
                teams=[
                    {"name": "one", "users": [str(first.steam64)]},
                    {"name": "two", "users": [str(second.steam64)]},
                ],
                matchSettings={
                    "ballistics": True,
                    "map": "Alpha Valley",
                    "movement": True,
                },
            )
        )
    with opendb() as db:
        db.query(data.Match).update({"time": 0})

    r = client.get("/unt/eventwait", params={"code": "90071992547409931"})

    assert r.status_code == 200
    assert r.json()["head"] == "start"
    with opendb() as db:
        assert db.query(data.Match).first().claimed == "90071992547409931"


def test_eventwait_from_a_server_too_old_to_send_a_code_claims_as_zero(
    client, user_factory
):
    data.AuthorizedServer.bootstrap({"testclient": "paris"})
    first, second = user_factory(), user_factory()
    with opendb() as db:
        db.add(
            data.Match(
                gamemode=0,
                teams=[
                    {"name": "one", "users": [str(first.steam64)]},
                    {"name": "two", "users": [str(second.steam64)]},
                ],
                matchSettings={
                    "ballistics": True,
                    "map": "Alpha Valley",
                    "movement": True,
                },
            )
        )
    with opendb() as db:
        db.query(data.Match).update({"time": 0})

    client.get("/unt/eventwait")

    with opendb() as db:
        assert db.query(data.Match).first().claimed == "0"


def test_unt_kills_appends_to_the_match_result(client, user_factory):
    data.AuthorizedServer.bootstrap({"testclient": "paris"})
    killer, victim = user_factory(), user_factory()
    with opendb() as db:
        db.add(
            data.Match(
                gamemode=0,
                teams=[
                    {"name": "one", "users": [str(killer.steam64)]},
                    {"name": "two", "users": [str(victim.steam64)]},
                ],
                matchSettings={
                    "ballistics": True,
                    "map": "Alpha Valley",
                    "movement": True,
                },
            )
        )
        matchid = db.query(data.Match).first().id

    r = client.post(
        "/unt/kills",
        params={"matchid": matchid},
        json={"killer": str(killer.steam64), "victim": str(victim.steam64)},
    )

    assert r.status_code == 200
    with opendb() as db:
        result = (
            db.query(data.MatchResult)
            .where(data.MatchResult.matchId == matchid)
            .first()
        )
        assert len(result.data["kills"]) == 1


def test_server_bootstrap_keeps_the_region_already_stored(client):
    data.AuthorizedServer.bootstrap({"testclient": "paris"})
    data.AuthorizedServer.bootstrap({"testclient": "utah"})

    with opendb() as db:
        servers = db.query(data.AuthorizedServer).all()

    assert [(s.address, s.region) for s in servers] == [("testclient", "paris")]


def test_change_uuid_rotates_uuid(client, user_factory):
    user = user_factory()
    r = client.get("/user/changeuuid", params={"uuid": user.uuid})
    assert r.status_code == 200
    body = r.json()
    assert body["uuid"] != user.uuid


def test_match_current_404_without_match(client, user_factory):
    user = user_factory()
    r = client.get("/match/current", params={"uuid": user.uuid})
    assert r.status_code == 404
    assert r.json()["error"] == "No match found."


def test_match_current_poll_auto_resolves_an_expired_pick(client, user_factory):
    """Nobody has to be watching for this to fire — the very next poll from either
    player's own screen is what notices the deadline passed and moves the match on."""
    from time import time
    from sqlmodel import Session
    from src.core import db as core_db

    first, second = user_factory(), user_factory()
    with opendb() as db:
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
    loadout back — the lobby plugin has no way to create an account first."""
    r = client.get("/kit/76561197960287930")
    assert r.status_code == 200
    assert set(r.json()) == set(settings.kits["kits"])


def test_unt_queue_heartbeat_rejects_unknown_ip(client):
    r = client.get("/unt/queue/heartbeat", params={"steam64": "123"})
    assert r.status_code == 401


def test_unt_queue_heartbeat_queues_a_player(client, user_factory):
    data.AuthorizedServer.bootstrap({"testclient": "paris"})
    player = user_factory()

    r = client.get(
        "/unt/queue/heartbeat",
        params={"steam64": player.steam64, "gamemode": 1, "region": "paris"},
    )

    assert r.status_code == 200
    assert r.json()["succes"] == "true"
    with opendb() as db:
        assert (
            db.query(data.UserHB).where(data.UserHB.steam64 == player.steam64).first()
        )


def test_unt_queue_heartbeat_matches_two_lobby_players(client, user_factory):
    """The whole point of the lobby heartbeat: two players sitting on the server
    with nothing else queued get pulled into a real match — same two-heartbeat
    shape as the browser flow, where the match scan (which runs before this
    call's own addToQueue) only sees a player once a prior heartbeat landed."""
    data.AuthorizedServer.bootstrap({"testclient": "paris"})
    first, second = user_factory(), user_factory()

    client.get(
        "/unt/queue/heartbeat",
        params={"steam64": first.steam64, "gamemode": 1, "region": "paris"},
    )
    client.get(
        "/unt/queue/heartbeat",
        params={"steam64": second.steam64, "gamemode": 1, "region": "paris"},
    )
    r = client.get(
        "/unt/queue/heartbeat",
        params={"steam64": second.steam64, "gamemode": 1, "region": "paris"},
    )

    assert r.json().get("matchid") is not None
    with opendb() as db:
        assert db.query(data.Match).count() == 1


def test_unt_queue_quit_removes_the_heartbeat(client, user_factory):
    data.AuthorizedServer.bootstrap({"testclient": "paris"})
    player = user_factory()
    data.User.addToQueue(player.steam64, 1, region="paris")

    r = client.get("/unt/queue/quit", params={"steam64": player.steam64})

    assert r.status_code == 200
    with opendb() as db:
        assert (
            db.query(data.UserHB).where(data.UserHB.steam64 == player.steam64).first()
            is None
        )
