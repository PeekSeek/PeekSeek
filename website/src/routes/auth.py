"""Steam OpenID login, Discord OAuth linking, and logout."""

import logging
import urllib.parse
from uuid import uuid4

import requests
from fastapi import APIRouter, Request
from fastapi.responses import RedirectResponse

from src.core.settings import settings
from src.core.db import open as opendb
from src.core import models as data

router = APIRouter(prefix="/auth")

STEAM_OPENID = "https://steamcommunity.com/openid/login"
OPENID_NS = "http://specs.openid.net/auth/2.0"
DISCORD_API = "https://discord.com/api"


def _back(request: Request) -> RedirectResponse:
    return RedirectResponse(request.session.get("lastPage") or "/", status_code=302)


@router.get("/steam")
def steam(request: Request):
    """Start a Steam login, whatever the visitor's cookie currently says.

    Never refuse this because the session already holds a UUID. The cookie is a
    signed but stateless blob, so it can carry a UUID the site no longer treats
    as logged in: a wiped database, a UUID rotated from another browser, a
    changed secret. Bouncing those visitors home makes the login button do
    nothing, and deleting the cookie by hand becomes their only way back in. We
    drop the old session here instead, keeping only the page to return them to.
    """
    last_page = request.session.get("lastPage")
    request.session.clear()
    if last_page:
        request.session["lastPage"] = last_page

    query = urllib.parse.urlencode(
        {
            "openid.ns": OPENID_NS,
            "openid.mode": "checkid_setup",
            "openid.realm": str(request.base_url),
            "openid.identity": f"{OPENID_NS}/identifier_select",
            "openid.claimed_id": f"{OPENID_NS}/identifier_select",
            "openid.return_to": str(request.url_for("steam_callback")),
        }
    )
    return RedirectResponse(f"{STEAM_OPENID}?{query}", status_code=302)


@router.get("/callback/steam")
def steam_callback(request: Request):
    query = request.query_params
    fields = (
        "ns",
        "op_endpoint",
        "claimed_id",
        "identity",
        "return_to",
        "response_nonce",
        "assoc_handle",
        "signed",
        "sig",
    )
    params = {f"openid.{field}": query.get(f"openid.{field}") for field in fields}
    params["openid.mode"] = "check_authentication"
    for field in (query.get("openid.signed") or "").split(","):
        params[f"openid.{field}"] = query.get(f"openid.{field}")

    r = requests.post(STEAM_OPENID, data=params)

    if "is_valid:true" not in r.text:
        # The only other trace of this is a lastError the next render pops, so
        # log it. A re-used nonce and a Steam outage look identical otherwise.
        logging.error("Steam rejected the login: %s %s", r.status_code, r.text)
        request.session["lastError"] = "Authentication failed."
        return _back(request)

    steam64 = int(query.get("openid.claimed_id").split("/")[-1])
    uuid = str(uuid4())

    # A fresh UUID on every login, never the stored one. The UUID is a bearer token —
    # `?uuid=` authenticates on its own — so proving ownership of the Steam account has
    # to be what mints it. Handing back the existing one would mean a leaked or shared
    # token stays valid no matter how many times its owner signs in, with /me/settings
    # the only way to ever revoke it. Signing in now revokes every older session by
    # itself, which is what signing in is expected to do.
    with opendb() as db:
        existing = db.query(data.User).where(data.User.steam64 == steam64).first()
        if existing:
            db.query(data.User).where(data.User.steam64 == steam64).update(
                {"uuid": uuid}
            )
        else:
            db.add(data.User(steam64=steam64, uuid=uuid))

    request.session["uuid"] = uuid
    return _back(request)


@router.get("/discord")
def discord(request: Request):
    if not request.state.user:
        return _back(request)

    query = urllib.parse.urlencode(
        {
            "client_id": settings.discord_client_id,
            "response_type": "code",
            "redirect_uri": str(request.url_for("discord_callback")),
            "scope": "identify",
        }
    )
    return RedirectResponse(
        f"https://discord.com/oauth2/authorize?{query}", status_code=302
    )


@router.get("/callback/discord/")
def discord_callback(request: Request, code: str):
    r = requests.post(
        f"{DISCORD_API}/oauth2/token",
        headers={"Content-Type": "application/x-www-form-urlencoded"},
        data={
            "client_id": settings.discord_client_id,
            "client_secret": settings.discord_client_secret,
            "grant_type": "authorization_code",
            "code": code,
            "redirect_uri": str(request.url_for("discord_callback")),
            "scope": "identify connections guilds",
        },
    )
    r.raise_for_status()
    me = requests.get(
        f"{DISCORD_API}/users/@me",
        headers={"authorization": "Bearer " + r.json()["access_token"]},
    )
    me.raise_for_status()

    with opendb() as db:
        db.query(data.User).where(data.User.uuid == request.session["uuid"]).update(
            {"discord": me.json()["id"]}
        )

    return _back(request)


@router.get("/logout")
def logout(request: Request):
    # Clear the lot: popping only the UUID leaves lastPage, lastBanCheck and
    # logged behind for whoever uses the browser next.
    request.session.clear()
    return RedirectResponse("/", status_code=302)
