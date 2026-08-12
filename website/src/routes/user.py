"""Player-facing pages (match, profile, spectate, settings) and their match JSON."""

import json
from datetime import datetime
from uuid import uuid4

from fastapi import APIRouter, HTTPException, Request
from fastapi.responses import PlainTextResponse, RedirectResponse

from src.core.settings import settings
from src.core.db import open as opendb
from src.core import models as data
from src.core import steam
from src.dependencies import ApiSteam64, JsonBody, LoggedInSteam64, templates

router = APIRouter()

mmconfig = settings.mmconfig


@router.get("/match")
def match(request: Request, steam64: LoggedInSteam64):
    with opendb() as db:
        current_id = data.User.getCurrentMatchId(steam64)
        if not current_id:
            request.session["lastError"] = "You are not in a match."
            return RedirectResponse("/", status_code=302)

        current = db.query(data.Match).where(data.Match.id == current_id).first()
        localJson = current.localJson()
        gm = mmconfig["gamemodes"][current.gamemode]
        userData = current.meta["userData"][str(steam64)]

        localJson["currentkit"] = userData["kit"] or gm["kitsAllowed"][0]
        powerups = gm["powerups"]
        localJson["currentpowerup"] = userData["powerup"] or (
            powerups[0]["name"] if powerups else None
        )

        return templates.TemplateResponse(
            request=request,
            name="app/match.html",
            context={
                "data": json.dumps(localJson),
                "dataAsDict": localJson,
                "len": len,
            },
        )


@router.get("/match/current")
def match_current(steam64: ApiSteam64, id: int | None = None):
    """Polled by the in-match screen — returns the live match state as JSON."""
    current_id = id or data.User.getCurrentMatchId(steam64)
    if not current_id:
        raise HTTPException(status_code=404, detail="No match found.")

    with opendb() as db:
        current = db.query(data.Match).where(data.Match.id == current_id).first()
        data.Match.cancelStaleUnclaimed()
        return current.localJson()


@router.get("/match/overlay")
def match_overlay(id: int | None = None, createdBy: int | None = None):
    """Public stream-overlay endpoint — no auth. Look up by match id or creator."""
    matchid = id
    if createdBy is not None:
        matchid = data.Match.findLastCreatedBy(createdBy)
    if not matchid:
        raise HTTPException(status_code=404, detail="No match found.")

    with opendb() as db:
        current = db.query(data.Match).where(data.Match.id == matchid).first()
        if not current:
            raise HTTPException(status_code=404, detail="No match found.")
        return current.localJson()


@router.post("/match/decide")
def match_decide(
    steam64: ApiSteam64,
    pick: str | None = None,
    kit: str | None = None,
    powerup: str | None = None,
):
    """Submit a pre-match decision: map/side pick, kit, or powerup."""
    current_id = data.User.getCurrentMatchId(steam64)
    if not current_id:
        raise HTTPException(status_code=404, detail="No match found.")

    choices = {
        key: value
        for key, value in {"pick": pick, "kit": kit, "powerup": powerup}.items()
        if value is not None
    }
    with opendb() as db:
        match = db.query(data.Match).where(data.Match.id == current_id).first()
        db.query(data.Match).where(data.Match.id == current_id).update(
            {"meta": match.getReturnedMeta(steam64, **choices)}
        )
    return PlainTextResponse("success")


@router.post("/match/custom")
def custom_create(steam64: ApiSteam64, payload: JsonBody):
    """Create a custom match (premium only)."""
    with opendb() as db:
        user = db.query(data.User).where(data.User.steam64 == steam64).first()
        if not user.getPermission("premium"):
            raise HTTPException(status_code=401, detail="Not authorized")

    teams = [
        {"name": name, "users": [u for u in users if u]}
        for name, users in payload["teams"].items()
    ]
    if not teams:
        raise HTTPException(status_code=400, detail="Team data cannot be empty")

    with opendb() as db:
        match = data.Match(
            gamemode=int(payload.get("gamemode", 0)),
            teams=teams,
            region=payload["region"],
            createdBy=steam64,
            matchSettings={
                "ballistics": payload["ballistics"],
                "map": payload["map"],
                "movement": payload["movement"],
            },
        )
        db.add(match)
        db.flush()
        return PlainTextResponse(str(match.id))


@router.api_route("/user/changeuuid", methods=["GET", "POST"])
def change_uuid(request: Request, steam64: ApiSteam64):
    """Rotate the caller's session UUID and notify them."""
    new_uuid = str(uuid4())
    with opendb() as db:
        user = db.query(data.User).where(data.User.steam64 == steam64).first()
        db.query(data.User).where(data.User.steam64 == steam64).update(
            {"uuid": new_uuid}
        )
        user.notify("Security Notification", "Your UUID has been changed.")
    request.session["uuid"] = new_uuid
    return {"uuid": new_uuid}


@router.get("/me/custom")
def custom_match(request: Request, steam64: LoggedInSteam64):
    with opendb() as db:
        user = db.query(data.User).where(data.User.steam64 == steam64).first()
        if not user.getPermission("premium"):
            request.session["lastError"] = (
                "You do not have an active Premium subscription."
            )
            return RedirectResponse("/", status_code=302)

    gamemodes = list(
        enumerate(
            gm["name"] + " - " + "/".join(gm["kitsAllowed"])
            for gm in mmconfig["gamemodes"]
        )
    )
    return templates.TemplateResponse(
        request=request, name="app/custommatch.html", context={"gamemodes": gamemodes}
    )


@router.get("/kit/{steam64}")
def public_kit_data(steam64: int):
    """Every saved kit for a player, fully resolved into ready-to-equip item lists
    — public, no auth, no match context needed. For anything that has to equip
    someone without a live match (the lobby server, in particular)."""
    return data.Kit.publicData(steam64)


@router.get("/user/{steam64}")
@router.get("/u/{steam64}")
def profile(request: Request, steam64: int):
    with opendb() as db:
        return templates.TemplateResponse(
            request=request,
            name="pages/profile.html",
            context={
                "userData": db.query(data.User)
                .where(data.User.steam64 == steam64)
                .first(),
                "statsData": db.query(data.Stat)
                .where(data.Stat.steam64 == steam64)
                .all(),
                "kitData": db.query(data.Kit).where(data.Kit.steam64 == steam64).all(),
                "matches": data.MatchResult.latestFor(steam64),
                "steam64Str": str(steam64),
                "steamData": data.User.getSteamData(steam64),
                "max": max,
                "round": round,
            },
        )


@router.get("/match/{matchid}")
def match_detail(request: Request, matchid: int):
    """A finished match's result and kill log — public, same as /u/{steam64}. The
    kill-by-kill map/graph view is still a concept; this is the plain data it will sit on
    top of."""
    with opendb() as db:
        # Not `match` — page_context() already injects that key app-wide as the logged-in
        # user's own active match id, and would silently overwrite this on every render.
        result = (
            db.query(data.MatchResult)
            .where(data.MatchResult.matchId == matchid)
            .first()
        )
        if not result:
            raise HTTPException(
                status_code=404, detail="No result found for this match."
            )

        results = result.data.get("results", {})
        return templates.TemplateResponse(
            request=request,
            name="pages/match_detail.html",
            context={
                "result": result,
                "players": [
                    {"steam64": s64, "user": steam.User(int(s64)), **summary}
                    for s64, summary in results.items()
                ],
                "kills": result.data.get("kills", []),
                "round": round,
                "max": max,
            },
        )


@router.get("/spectate")
def spectate(request: Request, steam64: LoggedInSteam64):
    with opendb() as db:
        matches = [
            m
            for m in db.query(data.Match).all()
            if m.finished is not True and str(steam64) in m.spectators
        ]
        return templates.TemplateResponse(
            request=request,
            name="app/spectate.html",
            context={
                "matches": matches,
                "fromTimestamp": datetime.fromtimestamp,
                "toList": lambda steam64s: [steam.User(s) for s in steam64s],
            },
        )


@router.get("/settings")
@router.get("/me/settings")
def user_settings(request: Request, steam64: LoggedInSteam64):
    return templates.TemplateResponse(request=request, name="app/settings.html")
