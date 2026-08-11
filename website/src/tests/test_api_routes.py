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
