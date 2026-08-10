"""Shared FastAPI dependencies: the current user, auth guards, request-body
compatibility helpers, and the template renderer every route uses.
"""

import json
from time import time as unix_time
from typing import Annotated, Optional

from fastapi import Depends, HTTPException, Request
from fastapi.templating import Jinja2Templates

from src.core.settings import settings
from src.core.db import open as opendb
from src.core import models as data


class LoginRedirect(Exception):
    """Raised by page guards for anonymous users; handled with a redirect home."""


class ActiveBan(Exception):
    """Raised when the requester has an active ban; handled with the error page."""

    def __init__(self, ban: data.UserBan):
        self.ban = ban


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
    "/unt/",
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


def _find_active_ban(steam64: int | None, ip: str) -> data.UserBan | None:
    with opendb() as db:
        steam64s = [
            row.steam64
            for row in db.query(data.UserIP).where(data.UserIP.address == ip).all()
        ]
        if steam64:
            steam64s.append(steam64)
        if not steam64s:
            return None

        for ban in (
            db.query(data.UserBan).where(data.UserBan.steam64.in_(steam64s)).all()
        ):
            if unix_time() < ban.endTime:
                return ban
    return None


def load_user(request: Request) -> Optional[data.User]:
    """Resolve the requester from the session (or ?uuid=), record the page for
    post-login redirects, track their IP, and gate active bans.

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

    if unix_time() - request.session.get("lastBanCheck", 0) > 60:
        request.session["lastBanCheck"] = unix_time()
        ban = _find_active_ban(user.steam64, ip)
        if ban:
            raise ActiveBan(ban)

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
    """The body parsed as JSON regardless of Content-Type — get_json(force=True)."""
    return json.loads(await request.body())


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
        "getPermission": user.getPermission if user else (lambda permission: False),
    }


templates = Jinja2Templates(
    directory=settings.root / "templates", context_processors=[page_context]
)
