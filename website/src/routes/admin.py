"""Admin dashboard and its match-control endpoints."""

from datetime import datetime

from fastapi import APIRouter, HTTPException, Request
from fastapi.responses import PlainTextResponse, RedirectResponse

from src.core.settings import settings
from src.core.db import open as opendb
from src.core import models as data
from src.core import steam
from src.core.webhook import Webhook
from src.dependencies import ApiSteam64, LoggedInSteam64, RequestValues, templates

router = APIRouter(prefix="/admin")


@router.get("/")
def admin(request: Request, steam64: LoggedInSteam64):
    with opendb() as db:
        user = db.query(data.User).where(data.User.steam64 == steam64).first()
        if not user.getPermission("admin"):
            request.session["lastError"] = "You are not an admin."
            return RedirectResponse("/", status_code=302)

        matches = [m for m in db.query(data.Match).all() if m.finished is not True]
        return templates.TemplateResponse(
            request=request,
            name="app/admin.html",
            context={
                "matches": matches,
                "fromTimestamp": datetime.fromtimestamp,
                "toList": lambda steam64s: [steam.User(s) for s in steam64s],
            },
        )


@router.post("/match/stop")
def force_stop(steam64: ApiSteam64, values: RequestValues):
    """Admin-only: immediately mark a match finished and log it."""
    matchid = values.get("id")
    with opendb() as db:
        user = db.query(data.User).where(data.User.steam64 == steam64).first()
        if not user.getPermission("admin"):
            raise HTTPException(status_code=401, detail="Not authorized")

        match = db.query(data.Match).where(data.Match.id == matchid)
        region = match.first().region
        match.update({"finished": True})

    name = data.User.getSteamData(steam64).name
    Webhook(f"Match.{matchid} ({region})").info(
        "Force stop",
        description=f"By [({name})]({settings.profile_url(steam64)}) `{steam64}`.",
    )
    return PlainTextResponse("success")
