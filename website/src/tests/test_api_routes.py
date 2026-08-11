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
