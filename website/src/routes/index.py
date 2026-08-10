"""Landing page, the play/queue screen, and the queue JSON the browser polls."""

from time import time

import requests
from fastapi import APIRouter, HTTPException, Request
from fastapi.responses import FileResponse, PlainTextResponse, RedirectResponse

from src.core.settings import settings
from src.core.db import open as opendb
from src.core import models as data
from src.core import matchmaking
from src.dependencies import ApiSteam64, templates

router = APIRouter()

DISCORD_API = "https://discord.com/api/v10"


@router.get("/")
def index(request: Request):
    if "uuid" in request.session:
        return RedirectResponse("/play", status_code=302)
    return templates.TemplateResponse(request=request, name="pages/index.html")


@router.get("/play")
def play(request: Request):
    if "uuid" not in request.session:
        return RedirectResponse("/", status_code=302)

    with opendb() as db:
        user = (
            db.query(data.User).where(data.User.uuid == request.session["uuid"]).first()
        )
        if not user:
            return templates.TemplateResponse(
                request=request,
                name="app/play.html",
                context={
                    "mmconfig": settings.visible_mmconfig,
                    "events": [],
                    "eventPlayers": {},
                    "maxTeam": {},
                },
            )

        events = []
        for signup in (
            db.query(data.EventSignup)
            .where(data.EventSignup.ownerSteam64 == user.steam64)
            .all()
        ):
            event = db.query(data.Event).where(data.Event.id == signup.event_id).first()
            if event:
                events.append({"signup": signup, "event": event, "users": signup.users})

    return templates.TemplateResponse(
        request=request,
        name="app/play.html",
        context={
            "mmconfig": settings.visible_mmconfig,
            "events": events,
            "eventPlayers": {
                e["event"].id: [
                    data.User.getSteamData(s).toDict() | {"steam64": s}
                    for s in e["users"]
                ]
                for e in events
            },
            "maxTeam": {
                e["event"].id: e["event"].data["meta"].get("maxPlayersPerTeam", 1)
                for e in events
            },
        },
    )


@router.get("/queue/heartbeat")
def queue_heartbeat(
    steam64: ApiSteam64,
    gamemode: int | None = None,
    region: str | None = None,
    team: int | None = None,
    eventId: int | None = None,
):
    """Keep the player in queue and report back if they've been matched."""
    current_match = data.User.getCurrentMatchId(steam64)
    if current_match:
        return {"succes": "true", "matchid": current_match}

    if eventId is not None:
        return _event_heartbeat(steam64, team, eventId)

    _ping_if_queueing_alone(region)
    if matchmaking.attemptCreateMatch(steam64, region):
        return {"succes": "true", "matchid": 1}

    data.User.addToQueue(steam64, gamemode, region=region)
    return {"succes": "true"}


def _event_heartbeat(steam64, team, eventId):
    with opendb() as db:
        event = db.query(data.Event).where(data.Event.id == eventId).first()
        if not event:
            raise HTTPException(status_code=403, detail="Invalid event.")

        if not any(
            start <= int(time()) <= end
            for start, end in event.data["meta"]["queueTimes"]
        ):
            raise HTTPException(status_code=403, detail="This event hasn't started.")

        signup = (
            db.query(data.EventSignup)
            .where(
                data.EventSignup.event_id == eventId,
                data.EventSignup.ownerSteam64 == steam64,
            )
            .first()
        )
        if signup.matchesplayed >= event.data["meta"]["maxMatchesPerTeam"]:
            raise HTTPException(
                status_code=403, detail="You have reached your maximum matches."
            )

    data.User.addToQueue(steam64, team, eventId=eventId)
    if matchmaking.attemptCreateEventMatch(steam64, eventId):
        return {"succes": "true", "matchid": 1}
    return {"succes": "true"}


def _ping_if_queueing_alone(region):
    """Ping Discord when a player is the first to queue in their region."""
    channel = settings.queue_log_channel
    if not channel:
        return
    with opendb() as db:
        recent = [
            hb
            for hb in db.query(data.UserHB).where(data.UserHB.region == region).all()
            if time() - hb.time < settings.queue_timeout
        ]
    if recent:
        return
    try:
        requests.post(
            f"{DISCORD_API}/channels/{channel}/messages",
            headers={
                "Authorization": f"Bot {settings.bot_token}",
                "Content-Type": "application/json",
            },
            json={
                "content": (
                    f"Somebody has started queue'ing all alone in "
                    f"`{settings.region_names.get(region, 'A region')}`! "
                    f"Help them [**find a match**](<{settings.base_url}>) "
                    f"by going to [{settings.domain}](<{settings.base_url}>)."
                )
            },
        )
    except requests.RequestException:
        pass


@router.get("/queue/quit")
def queue_quit(steam64: ApiSteam64):
    data.User.removeFromQueue(steam64)
    return PlainTextResponse("success")


@router.get("/ping")
def ping():
    return PlainTextResponse("Pong")


@router.get("/build")
def build():
    return PlainTextResponse(settings.build)


@router.get("/ads")
@router.get("/ads.txt")
def ads_txt():
    return FileResponse(settings.public / "assets" / "ads.txt")
