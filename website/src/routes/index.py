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
    if request.state.user:
        return RedirectResponse("/play", status_code=302)
    return templates.TemplateResponse(request=request, name="pages/index.html")


@router.get("/play")
def play(request: Request):
    # request.state.user, not the session UUID: a cookie can hold a UUID that no
    # longer resolves to anyone, and the two answers must never disagree.
    user = request.state.user
    if not user:
        return RedirectResponse("/", status_code=302)

    with opendb() as db:
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
    """Keep the player — and their whole party, if they lead one — in queue, and
    report back if they've been matched.

    Queueing does not make a match. A free game server in the player's region is what
    scans this queue and forms one (see `/server/v1/session`), so this only ever adds
    them to it and reports what happened elsewhere.
    """
    current_match = data.User.getCurrentMatchId(steam64)
    if current_match:
        return {"succes": "true", "matchid": current_match}

    if eventId is not None:
        return _event_heartbeat(steam64, team, eventId)

    with opendb() as db:
        party = data.Party.find(db, steam64)
    if party and str(party.steam64) != str(steam64):
        raise HTTPException(
            status_code=403, detail="Only your party leader can start the queue."
        )

    # The play page blocks this itself, so reaching it means something else called
    # us — and an empty bitmask reached addToQueue as a NULL gamemode and 500'd.
    if not gamemode:
        raise HTTPException(
            status_code=400, detail="Pick a gamemode before queueing."
        )

    partyMembers = party.data.get("members", []) if party else [str(steam64)]
    # Refusing here is the whole point: a party too big for every mode it picked
    # would otherwise sit in a queue matchmaking has already decided to skip.
    gamemode = matchmaking.modesFittingParty(gamemode, len(partyMembers))
    if not gamemode:
        raise HTTPException(
            status_code=400,
            detail=f"A party of {len(partyMembers)} is too big for the gamemodes you picked.",
        )

    _ping_if_queueing_alone(region)

    for member in partyMembers:
        data.User.addToQueue(
            int(member), gamemode, partyId=(party.id if party else None), region=region
        )
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
