"""Steam login must start no matter what the visitor's session cookie says."""

import base64
import json

from itsdangerous import TimestampSigner
from sqlmodel import Session

from src.core import db as core_db
from src.core import models as data
from src.core.settings import settings

STEAM_ID = 76561198344422592
CLAIMED = f"https://steamcommunity.com/openid/id/{STEAM_ID}"
CALLBACK = (
    "/auth/callback/steam?openid.ns=http://specs.openid.net/auth/2.0"
    "&openid.mode=id_res"
    "&openid.op_endpoint=https://steamcommunity.com/openid/login"
    f"&openid.claimed_id={CLAIMED}"
    f"&openid.identity={CLAIMED}"
    "&openid.return_to=http://testserver/auth/callback/steam"
    "&openid.response_nonce=2026-08-13T14:23:58Z"
    "&openid.assoc_handle=1234567890"
    "&openid.signed=signed,op_endpoint,claimed_id,identity,return_to,"
    "response_nonce,assoc_handle"
    "&openid.sig=notchecked"
)
STEAM_LOGIN = "https://steamcommunity.com/openid/login"


def _session(client, **payload):
    """Hand the client a signed session cookie holding exactly `payload`."""
    client.cookies.clear()
    signer = TimestampSigner(settings.secret_key)
    encoded = base64.b64encode(json.dumps(payload).encode())
    client.cookies.set(
        "session", signer.sign(encoded).decode(), domain="testserver.local", path="/"
    )


def _steam_accepts(monkeypatch):
    class Reply:
        status_code = 200
        text = "ns:http://specs.openid.net/auth/2.0\nis_valid:true\n"

    monkeypatch.setattr("src.routes.auth.requests.post", lambda *a, **k: Reply())


def test_login_starts_with_a_stale_uuid_in_the_cookie(client):
    """The UUID of a wiped database must not turn the login button into a no-op."""
    _session(client, uuid="uuid-nobody-owns", lastPage="http://testserver/play")
    r = client.get("/auth/steam")
    assert r.status_code == 302
    assert r.headers["location"].startswith(STEAM_LOGIN)


def test_login_starts_when_already_logged_in(client, login, user_factory):
    login(client, user_factory())
    r = client.get("/auth/steam")
    assert r.status_code == 302
    assert r.headers["location"].startswith(STEAM_LOGIN)


def test_login_over_a_stale_cookie_signs_the_visitor_in(client, monkeypatch):
    _steam_accepts(monkeypatch)
    _session(client, uuid="uuid-nobody-owns", lastPage="http://testserver/play")

    client.get("/auth/steam")
    callback = client.get(CALLBACK)
    assert callback.status_code == 302
    assert callback.headers["location"] == "http://testserver/play"

    with Session(core_db.engine) as db:
        assert db.query(data.User).where(data.User.steam64 == STEAM_ID).first()

    assert client.get("/").headers["location"] == "/play"


def test_logout_empties_the_whole_session(client, login, user_factory):
    login(client, user_factory(), lastPage="http://testserver/play", lastBanCheck=1)
    client.get("/auth/logout")
    assert client.get("/").status_code == 200
