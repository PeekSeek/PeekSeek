"""Admin dashboard and its match-control endpoints."""

from datetime import timezone
from time import time

from fastapi import APIRouter, HTTPException, Request
from fastapi.responses import PlainTextResponse, RedirectResponse
from sqlalchemy import desc, or_

from src.core.settings import settings
from src.core.db import open as opendb
from src.core import models as data
from src.core import moderation, steam
from src.core.webhook import Webhook
from src.dependencies import (
    ApiSteam64,
    JsonBody,
    LoggedInSteam64,
    RequestValues,
    templates,
)

# Staff-only dashboard and its controls. Out of the schema: it is not a published
# surface, and listing it tells a reader about endpoints they cannot call.
router = APIRouter(prefix="/admin", tags=["admin"], include_in_schema=False)

mmconfig = settings.mmconfig

HISTORY_SECONDS = 24 * 60 * 60


def _requireAdmin(steam64: int) -> None:
    with opendb() as db:
        user = db.query(data.User).where(data.User.steam64 == steam64).first()
        if user is None or not user.getPermission("admin"):
            raise HTTPException(status_code=401, detail="Not authorized")


@router.get("/")
def admin(request: Request, steam64: LoggedInSteam64):
    """The shell. admin.js draws every match on it from `/admin/matches`."""
    with opendb() as db:
        user = db.query(data.User).where(data.User.steam64 == steam64).first()
        if not user.getPermission("admin"):
            request.session["lastError"] = "You are not an admin."
            return RedirectResponse("/", status_code=302)

    return templates.TemplateResponse(
        request=request,
        name="app/admin.html",
        context={
            "adminSteam64": str(steam64),
            "adminName": data.User.getSteamData(steam64).name,
        },
    )


@router.get("/matches")
def matches(steam64: LoggedInSteam64):
    """Every live match, and every one that ended in the last day, in one call."""
    _requireAdmin(steam64)

    cutoff = time() - HISTORY_SECONDS
    with opendb() as db:
        rows = (
            db.query(data.Match)
            .where(or_(data.Match.finished.is_(False), data.Match.time >= cutoff))
            .order_by(desc(data.Match.id))
            .all()
        )
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
    """Every player on the page, named and avatared from the Users table in one query,
    so the poll never waits on Steam."""
    steam64s = {str(u) for match in rows for team in match.teams for u in team["users"]}
    stored = (
        db.query(data.User)
        .where(data.User.steam64.in_([int(s) for s in steam64s]))
        .all()
    )
    return {str(user.steam64): user.steam_data or {} for user in stored}


def _card(match, result, events, signups, profiles) -> dict:
    """One match, as the admin page draws it: the round score while it runs, and each
    player's final line once it is scored."""
    gamemode = mmconfig["gamemodes"][match.gamemode]
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
        "startedAt": match.time,
        "endedAt": _endedAt(match, result),
        "serverCode": match.claimed,
        "connectCode": match.connectCode(),
        "ending": _ending(match, stored),
        "event": {"id": event.id, "name": event.name} if event else None,
        "teams": [
            {
                "name": team["name"],
                "wins": (match.roundWins or {}).get(team["name"]),
                "eventScore": _eventScore(team, event, signups),
                "players": [
                    _cardPlayer(steam64, finalResults, profiles)
                    for steam64 in team["users"]
                ],
            }
            for team in match.teams
        ],
    }


def _endedAt(match, result) -> int | None:
    """When a finished match's result landed, in Unix seconds. MatchResult stores naive
    UTC, which `.timestamp()` would otherwise read as local time."""
    if not (result and match.finished and result.endTime):
        return None

    ended = result.endTime
    if ended.tzinfo is None:
        ended = ended.replace(tzinfo=timezone.utc)
    return int(ended.timestamp())


def _ending(match, stored: dict) -> str | None:
    """Why a finished match stopped, in one word, or None while it is still going."""
    if not match.finished:
        return None
    if stored.get("unscored"):
        return stored["unscored"]
    return "scored" if stored.get("results") else "unrecorded"


def _eventScore(team, event, signups) -> int | None:
    signup = signups.get((event.id, team["users"][0])) if event else None
    return signup.score if signup else None


def _cardPlayer(steam64: str, finalResults: dict, profiles: dict) -> dict:
    final = finalResults.get(str(steam64)) or {}
    profile = profiles.get(str(steam64)) or {}

    return {
        "steam64": str(steam64),
        "name": profile.get("name") or "Nameless",
        "avatar": profile.get("avatar") or steam.DEFAULT_AVATAR,
        "kills": final.get("Kills"),
        "deaths": final.get("Deaths"),
        "headshots": final.get("Headshots"),
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


def _banSummary(ban) -> dict:
    return {
        "id": ban.id,
        "reason": ban.reason,
        "note": ban.moderatorNote,
        "expires": ban.endTime,
        "expiryLabel": ban.expiryLabel,
        "permanent": ban.permanent,
        "issued": ban.created_at.isoformat() if ban.created_at else None,
    }


@router.get("/player/{target}")
def admin_player(steam64: LoggedInSteam64, target: int):
    """Everything the moderation drawer shows about one player, including what a
    cheating ban would cost everyone else."""
    _requireAdmin(steam64)

    with opendb() as db:
        record = db.query(data.Stat).where(data.Stat.steam64 == target).first()
        user = db.query(data.User).where(data.User.steam64 == target).first()

    active = data.UserBan.activeFor(target)
    profile = (user.steam_data or {}) if user else {}

    return {
        "steam64": str(target),
        "known": user is not None,
        "name": profile.get("name") or "Nameless",
        "avatar": profile.get("avatar") or steam.DEFAULT_AVATAR,
        "stats": (
            {
                "elo": record.elo,
                "kills": record.kills,
                "deaths": record.deaths,
                "wins": record.wins,
                "losses": record.losses,
                "headshotPercent": record.headshotPercent,
            }
            if record
            else None
        ),
        "ban": _banSummary(active) if active else None,
        "history": [_banSummary(b) for b in data.UserBan.historyFor(target)],
        "preview": moderation.previewBan(target),
        "presets": {
            key: {
                "label": p["label"],
                "summary": p["summary"],
                "voids": p["voidsHistory"],
            }
            for key, p in moderation.PRESETS.items()
        },
    }


@router.post("/ban")
def ban(steam64: ApiSteam64, values: JsonBody):
    """Issue a ban from the dashboard, through the same `moderation.issueBan` the
    Discord bot uses."""
    _requireAdmin(steam64)

    target = values.get("steam64")
    if not target:
        raise HTTPException(status_code=400, detail="No player given.")

    preset = values.get("preset") or None
    days = values.get("days")
    try:
        summary = moderation.issueBan(
            int(target),
            preset=None if preset == "custom" else preset,
            days=int(days) if days else None,
            reason=values.get("reason"),
            note=values.get("note"),
            byName=data.User.getSteamData(steam64).name,
            bySteam64=steam64,
        )
    except ValueError as error:
        raise HTTPException(status_code=400, detail=str(error)) from error

    return {"status": "ok", "summary": summary}


@router.get("/bans")
def bans(steam64: LoggedInSteam64, steam64s: str = ""):
    """Ban state for everyone on screen, for the chips on the match cards."""
    _requireAdmin(steam64)

    ids = [int(s) for s in steam64s.split(",") if s.strip().isdigit()]
    if not ids:
        return {}

    active = data.UserBan.activeAmong(ids)
    with opendb() as db:
        counts = {}
        for row in db.query(data.UserBan).where(data.UserBan.steam64.in_(ids)).all():
            counts[row.steam64] = counts.get(row.steam64, 0) + 1

    return {
        str(i): {
            "banned": i in active,
            # Prior bans only; the running one is already shown.
            "prior": max(counts.get(i, 0) - (1 if i in active else 0), 0),
        }
        for i in ids
        if i in active or counts.get(i)
    }


@router.post("/unban")
def unban(steam64: ApiSteam64, values: JsonBody):
    """Overturn one ban. The row is kept and marked, never deleted."""
    _requireAdmin(steam64)

    banId = values.get("id")
    if not banId:
        raise HTTPException(status_code=400, detail="No ban given.")

    lifted = moderation.overturnBan(
        int(banId),
        byName=data.User.getSteamData(steam64).name,
        bySteam64=steam64,
    )
    if lifted is None:
        raise HTTPException(status_code=404, detail="No such ban.")

    return {"status": "ok", "ban": lifted}
