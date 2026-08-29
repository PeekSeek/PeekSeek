"""Esports events: listing, event pages, and team signups."""

from time import time
from uuid import uuid4

from fastapi import APIRouter, Request
from fastapi.responses import PlainTextResponse, RedirectResponse
from markdown_it import MarkdownIt

from src.core.db import open as opendb
from src.core import models as data
from src.dependencies import ApiSteam64, CurrentUser, RequestValues, templates

router = APIRouter(prefix="/esports")


def _get_events(db):
    events = db.query(data.Event).all()
    events = [e for e in events if e.data["meta"]["queueTimes"][-1][1] > time()]
    events.sort(key=lambda x: x.data["meta"]["queueTimes"][0][0])
    return events


@router.get("/")
def index(request: Request, user: CurrentUser):
    with opendb() as db:
        elo = 300
        if user:
            stat = db.query(data.Stat).where(data.Stat.steam64 == user.steam64).first()
            if stat:
                elo = stat.elo

        return templates.TemplateResponse(
            request=request,
            name="esports/index.html",
            context={
                "events": _get_events(db),
                "top10": data.Stat.getTop10(),
                "elo": elo,
            },
        )


@router.post("/signup")
def signup(steam64: ApiSteam64, values: RequestValues):
    """Create / join / leave a team, or reset its join code. Registration closes
    once the first queue window opens."""
    eventId = _as_int(values.get("eventId"))
    if not eventId:
        return PlainTextResponse("Event ID is required", status_code=400)

    with opendb() as db:
        event = db.query(data.Event).where(data.Event.id == eventId).first()
        if not event:
            return PlainTextResponse("Event not found", status_code=404)
        if event.data["meta"]["queueTimes"][0][0] <= time():
            return PlainTextResponse(
                "This event has started; teams are locked.", status_code=403
            )

        existing = _find_signup(db, eventId, steam64)
        if existing is not None:
            # Leaving a team and resetting a join code stay open to a banned player.
            # The ban is about playing, and somebody who cannot play the event is
            # exactly who should be able to drop out of a team that is counting on them.
            return _handle_existing(db, existing, steam64, values)

        # A ban only blocks signing up if it is still running when the event starts.
        # Someone banned for a week can enter an event three weeks out: they will be
        # free to play it by the time it happens, and holding the whole event against
        # a ban that will have expired punishes them twice.
        ban = data.UserBan.activeFor(steam64)
        if ban is not None and ban.endTime >= event.data["meta"]["queueTimes"][0][0]:
            return PlainTextResponse(
                f'Your "{ban.reason}" ban runs until {ban.expiryLabel}, which is after '
                "this event starts.",
                status_code=403,
            )

        if values.get("team"):
            teamname = _clean_teamname(values["team"])
            db.add(
                data.EventSignup(
                    ownerSteam64=steam64, eventId=eventId, teamname=teamname
                )
            )
            return PlainTextResponse("Team created successfully")

        joincode = values.get("joincode")
        if not joincode:
            return PlainTextResponse(
                "Join code is required to join a team", status_code=400
            )

        target = (
            db.query(data.EventSignup)
            .where(data.EventSignup.joincode == joincode.strip("'").lower())
            .first()
        )
        if not target:
            return PlainTextResponse("Invalid join code", status_code=400)
        db.query(data.EventSignup).where(data.EventSignup.id == target.id).update(
            {"users": [*target.users, steam64]}
        )
        return PlainTextResponse("Successfully joined the team")


@router.get("/{event}")
def event(request: Request, event: str, user: CurrentUser):
    signup = None
    ownsTeam = False

    with opendb() as db:
        ev = db.query(data.Event).where(data.Event.name == event).first()
        if not ev:
            request.session["lastError"] = "Invalid event."
            return RedirectResponse("/", status_code=302)

        if user:
            signup = (
                db.query(data.EventSignup)
                .where(
                    data.EventSignup.ownerSteam64 == user.steam64,
                    data.EventSignup.event_id == ev.id,
                )
                .first()
            )
            if signup:
                ownsTeam = True
            else:
                for _s in (
                    db.query(data.EventSignup)
                    .where(data.EventSignup.event_id == ev.id)
                    .all()
                ):
                    if user.steam64 in _s.users:
                        signup = _s
                        break

        users = None
        if signup:
            users = [
                data.User.getSteamData(s) for s in [signup.ownerSteam64, *signup.users]
            ]

        return templates.TemplateResponse(
            request=request,
            name="esports/event.html",
            context={
                "event": ev,
                "signup": signup,
                "user": user,
                "discord": user.discord if user else None,
                "ownsTeam": ownsTeam,
                "rendermd": MarkdownIt().render,
                "users": users,
            },
        )


def _find_signup(db, eventId, steam64):
    signup = (
        db.query(data.EventSignup)
        .where(
            data.EventSignup.ownerSteam64 == steam64,
            data.EventSignup.event_id == eventId,
        )
        .first()
    )
    if signup:
        return signup
    for s in db.query(data.EventSignup).where(data.EventSignup.event_id == eventId):
        if steam64 in s.users:
            return s
    return None


def _handle_existing(db, signup, steam64, values):
    is_owner = signup.ownerSteam64 == steam64
    if is_owner and values.get("reset"):
        db.query(data.EventSignup).where(data.EventSignup.id == signup.id).update(
            {"joincode": str(uuid4())}
        )
        return PlainTextResponse("Successfully changed join code")
    if not values.get("leave"):
        return PlainTextResponse(
            "You are already signed up for this event.", status_code=400
        )
    if is_owner:
        db.query(data.EventSignup).where(data.EventSignup.id == signup.id).delete()
        return PlainTextResponse("Team deleted successfully")
    db.query(data.EventSignup).where(data.EventSignup.id == signup.id).update(
        {"users": [u for u in signup.users if u != steam64]}
    )
    return PlainTextResponse("Left team successfully")


def _clean_teamname(raw: str) -> str:
    allowed = "abcdefghijklmnopqrstuvwxyz0123456789._"
    return "".join(c for c in raw.replace(" ", "_") if c.lower() in allowed)[:14]


def _as_int(value):
    try:
        return int(value)
    except (TypeError, ValueError):
        return None
