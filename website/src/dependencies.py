"""Shared FastAPI dependencies: the current user, auth guards, request-body
compatibility helpers, and the template renderer every route uses.
"""

import json
from typing import Annotated, Optional

from fastapi import Depends, HTTPException, Request
from fastapi.templating import Jinja2Templates

from src.core.settings import settings
from src.core.db import open as opendb
from src.core import models as data


class LoginRedirect(Exception):
    """Raised by page guards for anonymous users; handled with a redirect home."""


def client_ip(request: Request) -> str:
    return request.client.host if request.client else ""


# Paths that must never become the post-login return page: redirect-only
# endpoints, JSON the site's scripts poll, and the game-server contract.
PAGES_NEVER_REMEMBERED = (
    "/auth/",
    "/discord",
    "/queue/",
    "/match/current",
    "/match/overlay",
    "/user/changeuuid",
    "/server/",
    "/ping",
    "/build",
    "/ads",
)


def _remember_page(request: Request) -> None:
    if request.method != "GET":
        return
    path = request.url.path
    if any(path.startswith(prefix) for prefix in PAGES_NEVER_REMEMBERED):
        return
    request.session["lastPage"] = str(request.url)


def load_user(request: Request) -> Optional[data.User]:
    """Resolve the requester from the session (or ?uuid=), record the page for
    post-login redirects, and track their IP.

    Registered app-wide, so every route (and template) sees request.state.user.
    """
    request.state.user = None
    _remember_page(request)

    uuid = request.session.get("uuid") or request.query_params.get("uuid")
    if not uuid:
        return None

    ip = client_ip(request)
    with opendb() as db:
        user = db.query(data.User).where(data.User.uuid == uuid).first()
        if not user:
            request.session.pop("uuid", None)
            return None

        request.session["logged"] = True
        known_ip = (
            db.query(data.UserIP)
            .where(
                data.UserIP.address == ip,
                data.UserIP.steam64 == user.steam64,
            )
            .first()
        )
        if not known_ip and ip:
            db.add(data.UserIP(steam64=user.steam64, address=ip))

    request.state.user = user

    return user


CurrentUser = Annotated[Optional[data.User], Depends(load_user)]


def require_page_login(user: CurrentUser) -> int:
    if not user:
        raise LoginRedirect("You must be logged in to access this page.")
    return user.steam64


def require_api_login(user: CurrentUser) -> int:
    if not user:
        raise HTTPException(status_code=401, detail="Login required")
    return user.steam64


LoggedInSteam64 = Annotated[int, Depends(require_page_login)]
ApiSteam64 = Annotated[int, Depends(require_api_login)]


def authorized_server(request: Request) -> data.AuthorizedServer:
    """Allow only requests from an allow-listed game-server IP."""
    with opendb() as db:
        server = (
            db.query(data.AuthorizedServer)
            .where(data.AuthorizedServer.address == client_ip(request))
            .first()
        )
    if not server:
        raise HTTPException(status_code=401, detail="IP not authorized.")
    return server


GameServer = Annotated[data.AuthorizedServer, Depends(authorized_server)]


async def merged_request_values(request: Request) -> dict:
    """Query and form params merged, query winning — Flask's request.values."""
    form = await request.form()
    return dict(form) | dict(request.query_params)


RequestValues = Annotated[dict, Depends(merged_request_values)]


async def forced_json_body(request: Request):
    """The body parsed as JSON regardless of Content-Type — get_json(force=True).

    An absent body reads as `{}` so a route whose body is entirely optional does not
    need a second signature for the empty case. A body that is present but malformed (a
    crashed plugin build, a truncated request) must 400, not 500 — this runs as a
    dependency, before any route's own try/except gets a chance to run, so an uncaught
    decode error here skips every route's error handling and its Discord webhook
    logging along with it.
    """
    raw = await request.body()
    if not raw.strip():
        return {}
    try:
        return json.loads(raw)
    except json.JSONDecodeError:
        raise HTTPException(status_code=400, detail="Invalid JSON body")


JsonBody = Annotated[dict, Depends(forced_json_body)]


def page_context(request: Request) -> dict:
    """Values available to every template render — Flask's context_processor."""
    user: Optional[data.User] = getattr(request.state, "user", None)
    return {
        "config": settings,
        "lastError": request.session.pop("lastError", None),
        "lastPage": request.session.get("lastPage"),
        "uuid": request.session.get("uuid") or request.query_params.get("uuid"),
        "rawUser": user,
        "user": data.User.getSteamData(user) if user else None,
        "match": user.getCurrentMatchId() if user else None,
        "discord": user.discord if user else None,
        # Six accounts in seven have no Discord linked, so six players in seven cannot
        # be told their match is ready. That is the single thing capping every
        # notification the site sends, which is why it gets a banner rather than a
        # line in settings nobody opens.
        "needsDiscordLink": bool(user and not user.discord),
        "getPermission": user.getPermission if user else (lambda permission: False),
        # Raised as a banner rather than a wall. A ban stops somebody playing, not
        # reading: they still need their profile, the rules and the appeal link, and
        # the page that used to block all of that told them less than the banner does.
        "ownBan": data.UserBan.activeFor(user.steam64) if user else None,
    }


templates = Jinja2Templates(
    directory=settings.root / "templates", context_processors=[page_context]
)
