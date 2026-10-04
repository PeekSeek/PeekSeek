"""Player-facing pages (match, profile, spectate, settings) and their match JSON."""

import json
from datetime import datetime
from uuid import uuid4
from time import time

from fastapi import APIRouter, HTTPException, Request
from fastapi.responses import PlainTextResponse, RedirectResponse

from src.core.settings import settings
from src.core.db import session
from src.core import models as data
from src.core import steam
from src.dependencies import (
    ApiSteam64,
    CurrentUser,
    JsonBody,
    LoggedInSteam64,
    templates,
)

router = APIRouter()

mmconfig = settings.mmconfig


@router.get("/match", tags=["pages"], include_in_schema=False)
def match_page(request: Request, steam64: LoggedInSteam64):
    current_id = data.Match.current_for(steam64)
    if not current_id:
        request.session["lastError"] = "You are not in a match."
        return RedirectResponse("/", status_code=302)

    with session() as db:
        current = db.query(data.Match).where(data.Match.id == current_id).first()

    local_json = current.local_json()
    userData = current.meta["userData"][str(steam64)]

    # Left as the player's real (possibly still-unmade) choice rather than
    # defaulting to the first option — the match still falls back to
    # kitsAllowed[0]/powerups[0] server-side (Kit.items_for, Match.all_kits_locked)
    # if they never pick, but the tile UI should never claim a choice was made
    # when it wasn't.
    local_json["currentkit"] = userData["kit"]
    local_json["currentpowerup"] = userData["powerup"]

    return templates.TemplateResponse(
        request=request,
        name="app/match.html",
        context={
            "data": json.dumps(local_json),
            "dataAsDict": local_json,
            "len": len,
        },
    )


@router.get("/match/current", tags=["match"])
def match_current(steam64: ApiSteam64, matchId: int | None = None):
    """Polled by the in-match screen — returns the live match state as JSON."""
    current_id = matchId or data.Match.current_for(steam64)
    if not current_id:
        raise HTTPException(status_code=404, detail="No match found.")

    with session() as db:
        current = db.query(data.Match).where(data.Match.id == current_id).first()

        # The game server's own poll enforces the pick deadline too, so a match whose
        # players all closed the tab still resolves — this just makes it instant for
        # whoever is actually watching rather than waiting on the next server tick.
        resolved = current.resolved_meta_if_expired()
        if resolved is not None:
            db.query(data.Match).where(data.Match.id == current_id).update(
                {"meta": resolved}
            )
            current.meta = resolved

    return current.local_json()


@router.post("/match/decide", tags=["match"])
def match_decide(
    steam64: ApiSteam64,
    pick: str | None = None,
    kit: str | None = None,
    powerup: str | None = None,
):
    """Submit a pre-match decision: map/side pick, kit, or powerup."""
    current_id = data.Match.current_for(steam64)
    if not current_id:
        raise HTTPException(status_code=404, detail="No match found.")

    choices = {
        key: value
        for key, value in {"pick": pick, "kit": kit, "powerup": powerup}.items()
        if value is not None
    }
    with session() as db:
        match = db.query(data.Match).where(data.Match.id == current_id).first()
        db.query(data.Match).where(data.Match.id == current_id).update(
            {"meta": match.get_returned_meta(steam64, **choices)}
        )
    return PlainTextResponse("success")


@router.post("/match/custom", tags=["match"])
def custom_create(steam64: ApiSteam64, payload: JsonBody):
    """Create a custom match (premium only)."""
    with session() as db:
        user = db.query(data.User).where(data.User.steam64 == steam64).first()
        if not user.get_permission("premium"):
            raise HTTPException(status_code=401, detail="Not authorized")

    teams = [
        {"name": name, "users": [u for u in users if u]}
        for name, users in payload["teams"].items()
    ]
    if not teams:
        raise HTTPException(status_code=400, detail="Team data cannot be empty")

    players = [int(u) for team in teams for u in team["users"]]
    with session() as db:
        barred = data.UserBan.barring(players, db)
    if barred:
        raise HTTPException(
            status_code=400,
            detail="Banned players cannot be in a match: "
            + ", ".join(str(steam64) for steam64 in barred),
        )

    with session() as db:
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


# POST only, and that is the whole point: rotating the UUID logs out every session on
# the account, so a prefetcher, link scanner or <img src> reaching it over GET would do
# that without anyone asking. Safe methods may not change state (RFC 9110 9.2.1).
@router.post("/user/changeuuid", tags=["account"])
def change_uuid(request: Request, steam64: ApiSteam64):
    """Rotate the caller's session UUID and notify them."""
    new_uuid = str(uuid4())
    with session() as db:
        user = db.query(data.User).where(data.User.steam64 == steam64).first()
        db.query(data.User).where(data.User.steam64 == steam64).update(
            {"uuid": new_uuid}
        )
        user.notify("Security Notification", "Your UUID has been changed.")
    request.session["uuid"] = new_uuid
    return {"uuid": new_uuid}


@router.get("/me/custom", tags=["pages"], include_in_schema=False)
def custom_match(request: Request, steam64: LoggedInSteam64):
    with session() as db:
        user = db.query(data.User).where(data.User.steam64 == steam64).first()
        if not user.get_permission("premium"):
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


def _elo_history(matches: list, steam64Str: str) -> list[dict]:
    """Elo right after each ranked match, oldest first — `matches` is already the
    same latest_for() list the match list renders, newest first, so this just walks
    it backwards rather than running a second query for the graph."""
    history = []
    for m in reversed(matches):
        r = m.data.get("results", {}).get(steam64Str, {})
        if "Current Elo" in r and "Elo Change" in r:
            history.append(
                {
                    "date": m.endTime.strftime("%d/%m"),
                    "elo": r["Current Elo"] + r["Elo Change"],
                }
            )
    return history


@router.get("/kit/{steam64}", tags=["player"])
def public_kit_data(steam64: int):
    """Every saved kit for a player, fully resolved into ready-to-equip item lists
    — public, no auth, no match context needed. For anything that has to equip
    someone without a live match (the lobby server, in particular)."""
    return data.Kit.public_data(steam64)


@router.get("/reputation/{steam64}", tags=["player"])
def public_reputation(steam64: int):
    """A player's Elo, public and unauthenticated — what the lobby server mirrors
    into their in-game reputation so rank is visible over someone's head.

    Deliberately just Elo: Unturned moves reputation itself on kills and other
    actions, so anything reading this has to be able to re-assert the real value
    rather than track a drifting one.
    """
    return {"steam64": str(steam64), "elo": data.Stat.get_elo(steam64)}


@router.get("/u/{steam64}", tags=["pages"], include_in_schema=False)
def profile(
    request: Request,
    steam64: int,
    viewer: CurrentUser = None,
    season: int | None = None,
):
    with session() as db:
        seasons = data.Season.all()
        viewing = data.Season.find(season) or data.Season.current()
        isCurrent = viewing.id == seasons[0].id

        stat = data.Stat.for_season(steam64, viewing.id)
        # A match carries no season of its own, so a season's matches are the ones
        # played while it was the one being played.
        window = viewing.window()
        matches = [
            m
            for m in data.MatchResult.latest_for(steam64)
            if data.Season.within(window, m.endTime)
        ]
        rank = data.Stat.rank_for(steam64)
        return templates.TemplateResponse(
            request=request,
            name="pages/profile.html",
            context={
                "userData": db.query(data.User)
                .where(data.User.steam64 == steam64)
                .first(),
                "stat": stat,
                "seasons": seasons,
                "viewing": viewing,
                "kitData": db.query(data.Kit).where(data.Kit.steam64 == steam64).all(),
                "matches": matches,
                "eloHistory": _elo_history(matches, str(steam64)),
                "steam64Str": str(steam64),
                "steamData": data.User.steam_user(steam64),
                # Only the live ladder has places to stand in; an archived season's
                # seats were handed out when it ended.
                "place": data.Stat.ladder_position(steam64) if isCurrent else None,
                # Only counted for the players whose rank card shows a position instead
                # of a bar, which is the tier under Apollo and Apollo itself.
                "world_position": (
                    data.Stat.world_position(steam64)
                    if isCurrent and stat and rank.stands_on_position
                    else None
                ),
                # Moderation lives on the profile because this is the page somebody is
                # already on when they decide a player needs dealing with. Everything
                # below is drawn only for an admin; for everyone else the profile is
                # exactly what it was.
                "viewerIsAdmin": viewer is not None and viewer.get_permission("admin"),
                "viewerSteam64": str(viewer.steam64) if viewer else "",
                "viewerName": (
                    data.User.steam_user(viewer.steam64).name if viewer else ""
                ),
                "banHistory": (
                    data.UserBan.history_for(steam64)
                    if (viewer and viewer.get_permission("admin"))
                    else []
                ),
                # Compared against each ban's endTime in the template, so a row can say
                # whether it is still in force without the template doing arithmetic.
                "now": int(time()),
                "max": max,
                "round": round,
            },
        )


def _rank_after(summary: dict, steam64) -> data.Rank | None:
    """The badge a player came out of a match wearing, or None if the match did not
    move any Elo.

    An unranked gamemode records no "Elo Change", and neither did anything played
    before Elo existed, so those have no rank to show rather than a change of zero to
    show. Ladder position is read as it stands now rather than as it stood then, which
    is what keeps a badge here identical to the one on the same player's profile.
    """
    if "Elo Change" not in summary or "Current Elo" not in summary:
        return None
    elo = summary["Current Elo"] + summary["Elo Change"]
    return data.Rank.for_elo(elo, data.Stat.ladder_position(steam64))


def _your_progress(results: dict, steam64: int | None) -> dict | None:
    """The reader's own Elo movement in this match, or None if they were not in it.

    The results table below already lists what everybody scored. This is the only part
    of the page that is about the person reading it, so it is worked out here rather
    than picked back out of that table in the template.
    """
    summary = results.get(str(steam64)) if steam64 else None
    if not summary:
        return None

    rank = _rank_after(summary, steam64)
    if rank is None:
        return None

    before = summary["Current Elo"]
    was = data.Rank.for_elo(before, data.Stat.ladder_position(steam64))
    return {
        "rank": rank,
        "bar": rank.bar(before),
        "delta": summary["Elo Change"],
        "elo": rank.elo,
        "promoted": rank.floor > was.floor,
        "demoted": rank.floor < was.floor,
    }


@router.get("/match/{matchId}", tags=["pages"], include_in_schema=False)
def match_detail(request: Request, matchId: int, user: CurrentUser):
    """A finished match's result and kill log — public, same as /u/{steam64}. The
    kill-by-kill map/graph view is still a concept; this is the plain data it will sit on
    top of."""
    with session() as db:
        # Not `match` — page_context() already injects that key app-wide as the logged-in
        # user's own active match id, and would silently overwrite this on every render.
        result = (
            db.query(data.MatchResult)
            .where(data.MatchResult.matchId == matchId)
            .first()
        )
        if not result:
            raise HTTPException(
                status_code=404, detail="No result found for this match."
            )

        results = result.data.get("results", {})
        rings = data.User.rings_for(results.keys())
        return templates.TemplateResponse(
            request=request,
            name="pages/match_detail.html",
            context={
                "result": result,
                "players": [
                    {
                        "steam64": s64,
                        "user": steam.User(int(s64)),
                        "rank": _rank_after(summary, s64),
                        "place": data.Stat.ladder_position(s64),
                        "ring": rings.get(str(s64)),
                        **summary,
                    }
                    for s64, summary in results.items()
                ],
                "you": _your_progress(results, user.steam64 if user else None),
                "round": round,
                "max": max,
            },
        )


@router.get("/spectate", tags=["pages"], include_in_schema=False)
def spectate(request: Request, steam64: LoggedInSteam64):
    with session() as db:
        matches = [
            m
            for m in db.query(data.Match).where(data.Match.finished.is_(False)).all()
            if str(steam64) in m.spectators
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


@router.get("/settings", tags=["pages"], include_in_schema=False)
def user_settings(request: Request, steam64: LoggedInSteam64):
    return templates.TemplateResponse(request=request, name="app/settings.html")


# One page, one URL. The second spelling each of these grew stays reachable as a
# permanent redirect, so old links keep working without two addresses for one resource.
@router.get("/me/settings", tags=["pages"], include_in_schema=False)
def settings_alias():
    return RedirectResponse("/settings", status_code=308)


@router.get("/user/{steam64}", tags=["pages"], include_in_schema=False)
def profile_alias(steam64: int):
    return RedirectResponse(f"/u/{steam64}", status_code=308)
