"""Endpoints the Unturned game servers call. Auth is by allow-listed server IP.

These paths are a frozen contract with the deployed C# plugin (RemoteAPI.cs) —
rename them only alongside a plugin release.
"""

from fastapi import APIRouter, Depends
from fastapi.responses import JSONResponse, PlainTextResponse

from src.core.settings import settings
from src.core.db import open as opendb
from src.core import models as data
from src.core import matchmaking
from src.core.webhook import Webhook
from src.dependencies import GameServer, JsonBody, authorized_server

router = APIRouter(prefix="/unt", dependencies=[Depends(authorized_server)])

mmconfig = settings.mmconfig


@router.get("/eventwait")
def event_wait(server: GameServer, matchid: str = "0", code: str = "0"):
    """Server tick. matchid=0 → claim a pending match; else report liveness.

    `code` is the claiming server's Unturned server code, which is what players
    connect with (`steam://run/304930//+connect <code>`). A server too old to send
    one claims as "0", and the site then offers no connect button for that match.
    """
    region = server.region

    if matchid == "0":
        return data.Match.claimMatchIfPossible(region, code) or {"head": "idle"}

    with opendb() as db:
        current = db.query(data.Match).where(data.Match.id == matchid).first()
        if current is None or current.finished:
            Webhook(f"Match.{matchid} ({region})").error(
                f"Server from `{region}`",
                "Received `stop` header due to invalid or prematurely finished match.",
            )
            return {"head": "stop"}

    data.Match.cancelStaleUnclaimed()
    return {"head": "idle"}


@router.post("/results")
def results(payload: JsonBody, matchid: int | None = None):
    """Match results from the server. Computes Elo and logs to Discord."""
    try:
        with opendb() as db:
            match = (
                db.query(data.Match)
                .where(data.Match.id == matchid, data.Match.finished.is_(False))
                .first()
                if matchid is not None
                else None
            )
            if not match:
                return JSONResponse({"error": "Invalid match id"}, status_code=403)

            eventId = match.meta.get("eventId")
            if eventId:
                Webhook(f"Event.{eventId}").info(
                    f"Match.{match.id} ({match.region}) Results",
                    matchmaking.eventResults(eventId, match.id, *payload["data"]),
                )
                match.finished = True
                return PlainTextResponse("success")

            if payload["played"]:
                gamemode = mmconfig["gamemodes"][match.gamemode]
                result, avgElo = matchmaking.matchResults(gamemode, *payload["data"])
                data.MatchResult.recordFinalResults(matchid, result)
                players = "\n\n".join(
                    f"{data.User.getSteamData(s).name} ({s})\n"
                    f"**[PeekSeek profile]({settings.profile_url(s)})** - "
                    f"[Steam profile](https://steamcommunity.com/profiles/{s})\n"
                    + "\n".join(f"**{name}** {value}" for name, value in stats.items())
                    for s, stats in result.items()
                )
                Webhook(f"Match.{match.id} ({match.region})").info(
                    "Results", f"***Average Elo** {avgElo}*\n\n{players}"
                )

            match.finished = True
            match.livedata = {}
            match.spectators = []
        return PlainTextResponse("success")
    except Exception as e:
        Webhook("Unturned Match Results").error(
            "Error", f"Error: {e}`\n```json\n{payload}\n```"
        )
        return PlainTextResponse("error", status_code=500)


@router.post("/update")
def update(payload: JsonBody, matchid: int | None = None):
    """Live match data pushed during play (scoreboard, positions)."""
    data.Match.setLiveData(matchid, payload)
    return PlainTextResponse("success")


@router.post("/kills")
def kills(payload: JsonBody, matchid: int | None = None):
    """One durable kill record, sent as it happens rather than batched at match end so a
    crashed server doesn't lose the whole match's kill log."""
    if matchid is not None:
        data.MatchResult.appendKill(matchid, payload)
    return PlainTextResponse("success")


@router.post("/ban")
def ban(steam64: int | None = None, reason: str = "Cheating"):
    """Permanent ban issued by PeekSeek Guard on a game server."""
    if not steam64:
        return PlainTextResponse("invalid steam64")

    with opendb() as db:
        db.add(
            data.UserBan(
                steam64=steam64,
                endTime=9999999999,
                reason=reason,
                moderatorNote="by PeekSeek Guard",
            )
        )
    return PlainTextResponse("success")


@router.get("/hlwn25/{steam64}")
def hlwn25(steam64: int):
    data.Hlwn25.append(steam64)
    return PlainTextResponse("success")
