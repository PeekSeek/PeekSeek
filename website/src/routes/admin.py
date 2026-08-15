"""Admin dashboard and its match-control endpoints."""

from datetime import timezone
from time import time

from fastapi import APIRouter, HTTPException, Request
from fastapi.responses import PlainTextResponse, RedirectResponse
from sqlalchemy import desc, or_

from src.core.settings import settings
from src.core.db import open as opendb
from src.core import models as data
from src.core import steam
from src.core.webhook import Webhook
from src.dependencies import ApiSteam64, LoggedInSteam64, RequestValues, templates

router = APIRouter(prefix="/admin")

mmconfig = settings.mmconfig

# How far back the match list reaches. A day is the window an admin is actually asking
# about ("what happened today"), and it is also what keeps the 15-second poll small.
HISTORY_SECONDS = 24 * 60 * 60


def _requireAdmin(steam64: int) -> None:
    with opendb() as db:
        user = db.query(data.User).where(data.User.steam64 == steam64).first()
        if user is None or not user.getPermission("admin"):
            raise HTTPException(status_code=401, detail="Not authorized")


@router.get("/")
def admin(request: Request, steam64: LoggedInSteam64):
    """The shell. Every match on it is drawn by admin.js from `/admin/matches`, so this
    template renders once and never again, so there is no server-rendered match list to
    fall out of step with the polled one."""
    with opendb() as db:
        user = db.query(data.User).where(data.User.steam64 == steam64).first()
        if not user.getPermission("admin"):
            request.session["lastError"] = "You are not an admin."
            return RedirectResponse("/", status_code=302)

    return templates.TemplateResponse(
        request=request, name="app/admin.html", context={}
    )


@router.get("/matches")
def matches(steam64: LoggedInSteam64):
    """Every live match, and every one that ended in the last day.

    One call answers the whole page, because the page redraws the whole page: polling
    per match would multiply by however many matches happen to be on screen, and the
    cost of a match nobody is looking at is the same as one they are.
    """
    _requireAdmin(steam64)

    cutoff = time() - HISTORY_SECONDS
    with opendb() as db:
        rows = (
            db.query(data.Match)
            .where(or_(data.Match.finished.is_(False), data.Match.time >= cutoff))
            .order_by(desc(data.Match.id))
            .all()
        )
        # One read each for the whole page rather than one per card. Events are a small
        # table and most pages have none, so they are only read if a match names one.
        results = {
            row.matchId: row
            for row in db.query(data.MatchResult)
            .where(data.MatchResult.matchId.in_([m.id for m in rows]))
            .all()
        }
        eventIds = {m.meta.get("eventId") for m in rows if m.meta.get("eventId")}
        events = (
            {
                event.id: event
                for event in db.query(data.Event)
                .where(data.Event.id.in_(eventIds))
                .all()
            }
            if eventIds
            else {}
        )
        signups = (
            {
                (signup.event_id, str(signup.ownerSteam64)): signup
                for signup in db.query(data.EventSignup)
                .where(data.EventSignup.event_id.in_(eventIds))
                .all()
            }
            if eventIds
            else {}
        )
        profiles = _profiles(db, rows)

    return {
        "generatedAt": int(time()),
        "matches": [
            _card(m, results.get(m.id), events, signups, profiles) for m in rows
        ],
    }


def _profiles(db, rows) -> dict:
    """Every player on the page, named and avatared, in one query.

    Read off the Users table rather than through `steam.User`, which asks Steam first
    and does not cache a failure: called once per player per card per poll, that was
    twenty blocking Steam requests every fifteen seconds, and with Steam refusing the
    key it made this endpoint take sixteen seconds to answer. The site persists each
    player's name and avatar on their own row precisely so a page does not have to ask.
    """
    steam64s = {str(u) for match in rows for team in match.teams for u in team["users"]}
    stored = (
        db.query(data.User)
        .where(data.User.steam64.in_([int(s) for s in steam64s]))
        .all()
    )
    return {str(user.steam64): user.steam_data or {} for user in stored}


def _card(match, result, events, signups, profiles) -> dict:
    """One match, as the admin page draws it.

    Live numbers come off the match's own snapshot and final ones off its result row,
    and which of the two is authoritative flips when the match ends. Scoring a match
    clears the snapshot, so for anything finished the result row is all there is.
    """
    gamemode = mmconfig["gamemodes"][match.gamemode]
    live = match.livedata or {}
    stored = result.data if result else {}
    finalResults = stored.get("results") or {}

    event = events.get(match.meta.get("eventId"))

    return {
        "id": match.id,
        "status": match.status,
        "statusLabel": match.statusLabel,
        "over": match.finished,
        "region": match.region,
        "gamemode": gamemode["name"],
        "map": match.meta.get("map"),
        "ballistics": match.meta.get("ballistics"),
        "movement": match.meta.get("movement"),
        "roundsToWin": match.meta.get("roundsToWin", 3),
        "round": live.get("round"),
        "startedAt": match.time,
        "endedAt": _endedAt(match, result),
        # Two different questions. `serverCode` is which server holds the match, true
        # from the moment it is made; `connectCode` is whether there is yet anything to
        # connect to, which stays null until the match is actually being played.
        "serverCode": match.claimed,
        "connectCode": match.connectCode(),
        "server": steam.Server.describe(match.claimed),
        "ending": _ending(match, stored),
        "event": {"id": event.id, "name": event.name} if event else None,
        "teams": [
            _team(team, live, stored, finalResults, event, signups, profiles)
            for team in match.teams
        ],
    }


def _endedAt(match, result) -> int | None:
    """When a finished match's result landed, as a Unix second, or None while it runs.

    MatchResult writes `datetime.utcnow()`, which is UTC carrying no timezone, and
    `.timestamp()` reads a naive datetime as *local* time. On a machine two hours ahead
    of UTC that put every match's end two hours before its own start, so the card said a
    finished match had lasted minus two hours.
    """
    if not (result and match.finished and result.endTime):
        return None

    ended = result.endTime
    if ended.tzinfo is None:
        ended = ended.replace(tzinfo=timezone.utc)
    return int(ended.timestamp())


def _ending(match, stored: dict) -> str | None:
    """Why a finished match stopped, in one word, or None while it is still going.

    `unscored` is the one that matters: a match nobody turned up for and a match whose
    server vanished both end with every stat at zero, and until this was on the card
    they were indistinguishable from a real result that happened to be goalless.
    """
    if not match.finished:
        return None
    if stored.get("unscored"):
        return stored["unscored"]
    return "scored" if stored.get("results") else "unrecorded"


def _team(
    team, live: dict, stored: dict, finalResults: dict, event, signups, profiles
) -> dict:
    """One side of a match. `match.teams` is the roster of record, so the snapshot and
    the result row are both looked up against it rather than iterated themselves: a
    player who never connected appears in neither, and leaving them off the card is
    what used to make a no-show look like a match that was never made."""
    liveTeam = next(
        (t for t in live.get("teams", []) if t.get("teamName") == team["name"]), None
    )
    # The snapshot while it exists, the copy taken of it at scoring time afterwards.
    score = liveTeam or next(
        (s for s in stored.get("score") or [] if s.get("name") == team["name"]), {}
    )
    signup = signups.get((event.id, team["users"][0])) if event else None

    return {
        "name": team["name"],
        "wins": score.get("wins"),
        "losses": score.get("losses"),
        "eventScore": signup.score if signup else None,
        "players": [
            _cardPlayer(
                steam64, (liveTeam or {}).get("users", {}), finalResults, profiles
            )
            for steam64 in team["users"]
        ],
    }


def _cardPlayer(
    steam64: str, liveUsers: dict, finalResults: dict, profiles: dict
) -> dict:
    """One player's line. Live stats while the match runs, final ones once it is scored,
    and a name and face either way so a card is readable before anyone has done anything
    at all.

    The in-game character name wins over the stored Steam persona when there is one,
    because that is what an admin watching the server sees people called.
    """
    liveUser = liveUsers.get(str(steam64)) or {}
    final = finalResults.get(str(steam64)) or {}
    profile = profiles.get(str(steam64)) or {}

    return {
        "steam64": str(steam64),
        "name": liveUser.get("name") or profile.get("name") or "Nameless",
        "avatar": profile.get("avatar") or steam.DEFAULT_AVATAR,
        # Only meaningful while the match is live; the snapshot it comes from is gone
        # afterwards, which is why this is null rather than false on a finished match.
        "connected": liveUser.get("state") != "offline" if liveUser else None,
        "state": liveUser.get("state"),
        "health": liveUser.get("health"),
        "kills": final.get("Kills", liveUser.get("kills")),
        "deaths": final.get("Deaths", liveUser.get("deaths")),
        "headshots": final.get("Headshots", liveUser.get("headshots")),
        "eloChange": final.get("Elo Change"),
        "elo": final.get("Current Elo"),
        "won": final.get("Won"),
    }


@router.post("/match/stop")
def force_stop(steam64: ApiSteam64, values: RequestValues):
    """Admin-only: immediately mark a match finished and log it."""
    matchid = values.get("id")
    _requireAdmin(steam64)

    with opendb() as db:
        match = db.query(data.Match).where(data.Match.id == matchid)
        region = match.first().region
        match.update({"finished": True})

    name = data.User.getSteamData(steam64).name
    Webhook(f"Match.{matchid} ({region})").info(
        "Force stop",
        description=f"By [({name})]({settings.profile_url(steam64)}) `{steam64}`.",
    )
    return PlainTextResponse("success")
