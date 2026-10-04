"""The game-server API: `/server/v1/*`.

Conventions for every route here:

- Auth is the caller's source IP, allow-listed in `AuthorizedServers`.
- Field names are camelCase.
- Steam64s are strings, because JSON numbers lose their bottom digits.
- Every response is a JSON object, so a field can be added without changing the shape.
- Durations are seconds; timestamps are Unix seconds.
- Writes that can be retried are idempotent, and say which of the two happened.

The whole lifecycle runs through one call, `GET /session`.
"""

from fastapi import APIRouter, Depends, File, Form, HTTPException, UploadFile
from pydantic import ValidationError

from src.core.settings import settings
from src.core.db import session
from src.core import models as data
from src.core import matchmaking, notify, reports
from src.core.webhook import Webhook
from src.dependencies import GameServer, JsonBody, authorized_server, body_schema
from src.routes import contracts

router = APIRouter(
    prefix="/server/v1",
    tags=["game server"],
    dependencies=[Depends(authorized_server)],
)

mmconfig = settings.mmconfig


def _items(rawItems: list) -> list[dict]:
    """Kit items as `{"itemId", "amount", "state"}` objects rather than the stored
    `[itemId, amount, "1,2,3"]` tuples."""
    return [
        {
            "itemId": int(item[0]),
            "amount": int(item[1]),
            "state": [int(byte) for byte in str(item[2]).split(",") if byte != ""],
        }
        for item in rawItems
    ]


def _match(match: data.Match) -> dict:
    """The whole match, as the server hosting it needs to see it."""
    gamemode = mmconfig["gamemodes"][match.gamemode]
    return {
        "id": match.id,
        "region": match.region,
        "gamemode": {
            "id": match.gamemode,
            "name": gamemode["name"],
            "usesElo": gamemode["usesElo"],
        },
        "roundsToWin": match.meta.get("roundsToWin", 3),
        "settings": {
            "map": match.meta["map"],
            "ballistics": match.meta["ballistics"],
            "movement": match.meta["movement"],
        },
        "teams": [
            {"name": team["name"], "players": [str(u) for u in team["users"]]}
            for team in match.teams
        ],
        "players": [
            {
                "steam64": str(steam64),
                "kit": _items(data.Kit.items_for(steam64, match)),
            }
            for steam64 in match.meta["userData"]
        ],
        "spectators": [str(s) for s in match.spectators],
    }


@router.get(
    "/session",
    # Described, not enforced: see contracts.SessionAnswer for why a response model
    # would change the payload these plugins are compiled against.
    responses={200: {"model": contracts.SessionAnswer, "description": "What to do next"}},
)
def server_session(server: GameServer, serverCode: str = "0", matchId: int = 0):
    """The one call a game server makes on a loop. Answers what it should be doing.

    `matchId` is the match the server holds, including one it has finished but not yet
    reported; `0` means it is free. Matchmaking runs here, so a match is only ever made
    for a server standing by to host it.

    `action` is one of:

    - `idle`: nothing to do. Ask again shortly.
    - `wait`: the match is yours but its players are still choosing. Keep sending its id.
    - `host`: start it. Sent once per match; asking again while hosting answers `idle`.
    - `abort`: drop it. Nobody is coming.

    A match a server still names is never written off, which is what makes the result
    report safe to retry indefinitely.
    """
    if matchId:
        answer = _answer_about_held_match(matchId, serverCode)
        data.Match.cancel_stranded()
        return _stamped(answer, server)

    data.Match.release_abandoned(serverCode)
    data.Match.resolve_expired_picks()

    # A match this server holds but was never told about is handed back before a new
    # one is built, so a lost answer cannot turn into a second match for the same
    # players.
    match = data.Match.undelivered_for(serverCode) or matchmaking.match_for_server(
        server.region, serverCode
    )
    data.Match.cancel_stranded()

    answer = _host_or_wait(match) if match else {"action": "idle"}
    return _stamped(answer, server)


def _stamped(answer: dict, server: data.AuthorizedServer) -> dict:
    """Every answer names this server's registered region and how many are queued there,
    so a region mismatch reads off the console instead of looking like an empty queue.
    """
    return answer | {
        "region": server.region,
        "queuedInRegion": data.UserHB.count_fresh(server.region),
    }


def _answer_about_held_match(matchId: int, serverCode: str) -> dict:
    """What to tell a server that is already holding a match."""
    with session() as db:
        match = db.query(data.Match).where(data.Match.id == matchId).first()

    if match is None:
        return {"action": "abort", "matchId": matchId, "reason": "unknownMatch"}
    if match.finished:
        return {"action": "abort", "matchId": matchId, "reason": "matchOver"}
    if match.claimed not in (serverCode, "0", None):
        return {"action": "abort", "matchId": matchId, "reason": "hostedElsewhere"}

    if match.status == data.Match.LIVE:
        return {"action": "idle"}

    resolved = match.resolved_meta_if_expired()
    if resolved is not None:
        with session() as db:
            db.query(data.Match).where(data.Match.id == matchId).update(
                {"meta": resolved}
            )
        match.meta = resolved

    return _host_or_wait(match)


def _roster(match: data.Match) -> dict:
    """Who is on a match that is still choosing, so the server can let them in."""
    gamemode = mmconfig["gamemodes"][match.gamemode]
    return {
        "id": match.id,
        "gamemode": {"id": match.gamemode, "name": gamemode["name"]},
        "teams": [
            {"name": team["name"], "players": [str(u) for u in team["users"]]}
            for team in match.teams
        ],
        "spectators": [str(s) for s in match.spectators],
    }


def _options(match: data.Match) -> dict:
    """The kits and powerups there are to choose between, or empty lists where the
    gamemode leaves no choice."""
    gm = mmconfig["gamemodes"][match.gamemode]
    kits = list(gm["kitsAllowed"]) if len(gm["kitsAllowed"]) > 1 else []
    powerups = (
        [{"name": p["name"], "items": _items(p["items"])} for p in gm["powerups"]]
        if len(gm["powerups"]) > 1
        else []
    )
    return {"kits": kits, "powerups": powerups}


def _host_or_wait(match: data.Match) -> dict:
    """Hand over a match whose selection is done, or say what it is still waiting on.

    The wait carries the roster and the options, because the players are relayed to
    the server as soon as it holds the match and pick there.
    """
    if not match.ready_to_host():
        return {
            "action": "wait",
            "matchId": match.id,
            "waitingOn": match.waiting_on(),
            "selectionEndsAt": int(match.selection_ends_at()),
            "match": _roster(match),
            "options": _options(match),
            "picked": {
                str(steam64): {"kit": user["kit"], "powerup": user["powerup"]}
                for steam64, user in match.meta["userData"].items()
            },
        }

    # Built before it is marked hosted: a hosted match is never handed out again, so a
    # payload that failed to build here would leave a match nobody runs.
    payload = _match(match)
    match.mark_hosting()
    return {"action": "host", "match": payload}


@router.get("/matches/{matchId}", response_model=contracts.MatchState)
def gameserver_match(matchId: int):
    """A match's state. `relayTo` is the server code to send its players to, and stays
    null until there is a server to send them to."""
    with session() as db:
        current = db.query(data.Match).where(data.Match.id == matchId).first()
    if current is None:
        raise HTTPException(status_code=404, detail="No such match.")

    return {
        "id": current.id,
        "region": current.region,
        "status": current.status,
        "relayTo": current.connect_code(),
    }


@router.post(
    "/matches/{matchId}/rounds",
    response_model=contracts.Status,
    openapi_extra=body_schema(contracts.RoundWinsReport),
)
def match_rounds(matchId: int, payload: JsonBody):
    """Rounds won per team, sent each time a round is decided.

    Body: `{"wins": {"<team name>": <rounds won>, ...}}`. Each report replaces the last,
    so a lost one is covered by the next.
    """
    wins = _validated(contracts.RoundWinsReport, payload).checked()
    data.Match.record_round_wins(matchId, wins)
    return {"status": "ok"}


def _validated(model: type, payload: dict):
    """Parse a body that arrived through `forced_json_body` into its model.

    The lenient parse is kept - a plugin's Content-Type is not worth refusing a report
    over - so validation happens here rather than in the signature. A bad body is a 400
    naming the field, not a 500 from scoring code reaching into a dict that lacks it.
    """
    try:
        return model.model_validate(payload)
    except (ValidationError, ValueError) as error:
        raise HTTPException(status_code=400, detail=str(error))


@router.post(
    "/matches/{matchId}/result",
    response_model=contracts.Status,
    openapi_extra=body_schema(contracts.MatchResultReport),
)
def match_result(matchId: int, payload: JsonBody):
    """The match's final per-player line. This is what moves Elo, and what ends a match.

    Safe to retry: a repeat is answered `alreadyRecorded` and changes nothing. Accepted
    for a match already marked finished, since that is what happens to one whose result
    was slow to arrive.

    Body:
        played       false when nobody turned up. Scores nothing, releases the match.
        winningTeam  the `name` of the winning team, or null.
        draw         true when the match ended level. Elo moves on kills and deaths.
        players      every player on the roster, including no-shows and leavers.
    """
    with session() as db:
        current = db.query(data.Match).where(data.Match.id == matchId).first()
    if current is None:
        raise HTTPException(status_code=404, detail="No such match.")

    if data.MatchResult.has_results(matchId):
        return {"status": "alreadyRecorded", "matchId": matchId}

    report = _validated(contracts.MatchResultReport, payload)
    try:
        _score(current, report)
    except Exception as error:
        Webhook(f"Match.{matchId}").error(
            "Result rejected",
            f"`{error}`\n```json\n{payload}\n```",
        )
        raise HTTPException(status_code=400, detail=f"Could not score result: {error}")

    with session() as db:
        db.query(data.Match).where(data.Match.id == matchId).update(
            {"finished": True, "spectators": []}
        )
    return {"status": "recorded", "matchId": matchId}


def _score(match: data.Match, report: contracts.MatchResultReport) -> None:
    """Apply one result payload: event standings, or ranked Elo, or nothing at all for a
    match that was never played."""
    players = [line.model_dump() for line in report.players]

    eventId = match.meta.get("eventId")
    if eventId:
        Webhook(f"Event.{eventId}").info(
            f"Match.{match.id} ({match.region}) Results",
            matchmaking.event_results(eventId, match.id, *players),
        )
        return

    if not report.played or not players:
        data.MatchResult.record_unscored(match.id, "notPlayed")
        return

    gamemode = mmconfig["gamemodes"][match.gamemode]
    result, avgElo = matchmaking.match_results(
        gamemode,
        *players,
        draw=report.draw,
        teams=[team["users"] for team in match.teams],
    )
    data.MatchResult.record_final_results(match.id, result)

    lines = "\n\n".join(
        f"{data.User.steam_user(s).name} ({s})\n"
        f"**[PeekSeek profile]({settings.profile_url(s)})** - "
        f"[Steam profile](https://steamcommunity.com/profiles/{s})\n"
        + "\n".join(f"**{name}** {value}" for name, value in stats.items())
        for s, stats in result.items()
    )
    Webhook(f"Match.{match.id} ({match.region})").info(
        "Results", f"***Average Elo** {avgElo}*\n\n{lines}"
    )
    _post_result_publicly(match, result, avgElo)


def _post_result_publicly(match: data.Match, result: dict, avgElo: int) -> None:
    """The result again, as a scoreboard in the channel players read."""
    winners = [steam64 for steam64, stats in result.items() if stats.get("Won")]
    names = {steam64: data.User.steam_user(steam64).name for steam64 in result}
    scoreline = " vs ".join(
        f"**{names[steam64]}**" if steam64 in winners else names[steam64]
        for steam64 in result
    )

    notify.send_discord_channel(
        settings.queue_log_channel,
        embed={
            "title": mmconfig["gamemodes"][match.gamemode]["name"],
            "url": f"{settings.base_url}/match/{match.id}",
            "description": scoreline,
            "color": 0x00FF00,
            "fields": [
                {
                    "name": names[steam64],
                    "value": (
                        f"{stats['Kills']}K {stats['Deaths']}D"
                        + (
                            f" · {stats['Elo Change']:+} Elo"
                            if "Elo Change" in stats
                            else ""
                        )
                    ),
                    "inline": True,
                }
                for steam64, stats in result.items()
            ],
            "footer": {"text": f"Average Elo {avgElo} · {match.region}"},
        },
    )


def _player(steam64: int, matchId: int | None, ban) -> dict:
    """One player as every player-facing route here reports them: their rank, their ban,
    and where they should be. `matchId` without a `relayTo` means "matched, still
    choosing", and `choose` is what they are still choosing between."""
    match = None
    if matchId:
        with session() as db:
            match = db.query(data.Match).where(data.Match.id == matchId).first()

    profile = data.User.steam_user(steam64)
    elo = data.Stat.get_elo(steam64)

    return {
        "steam64": str(steam64),
        "name": profile.name,
        "avatar": profile.avatar,
        "elo": elo,
        "rankIcon": data.Rank.for_elo(elo, data.Stat.ladder_position(steam64)).icon,
        "matchId": matchId or None,
        "relayTo": match.connect_code() if match else None,
        "choose": _choices(match, steam64) if match else None,
        "ban": (
            {
                "reason": ban.reason,
                "expires": ban.endTime,
                "permanent": ban.permanent,
            }
            if ban
            else None
        ),
    }


def _choices(match: "data.Match", steam64: int) -> dict | None:
    """What this player still gets to decide about their match, and what they have
    decided so far. Null when there is nothing for them to decide; a list with one
    entry is not a choice and comes back empty."""
    userData = match.meta["userData"].get(str(steam64))
    if userData is None:
        return None

    gm = mmconfig["gamemodes"][match.gamemode]
    kits = list(gm["kitsAllowed"]) if len(gm["kitsAllowed"]) > 1 else []
    powerups = [p["name"] for p in gm["powerups"]] if len(gm["powerups"]) > 1 else []
    if not kits and not powerups:
        return None

    return {
        "kits": kits,
        "powerups": powerups,
        "kit": userData["kit"],
        "powerup": userData["powerup"],
    }


@router.post("/players/{steam64}/decide", response_model=contracts.Decision)
def decide_player(steam64: int, kit: str | None = None, powerup: str | None = None):
    """Lock this player's kit and/or powerup for the match they are on.

    Both fields are optional and checked against the gamemode. Once every player has
    locked both, the outstanding map/ballistics/movement picks are decided at once,
    since nobody picking in game is at a browser to make them.

    Answers with the same `choose` block `GET /players/{steam64}` returns.
    """
    matchId = data.Match.current_for(steam64)
    if not matchId:
        raise HTTPException(status_code=404, detail="No match found.")

    with session() as db:
        match = db.query(data.Match).where(data.Match.id == matchId).first()

    choices = _choices(match, steam64)
    if choices is None:
        raise HTTPException(status_code=404, detail="Not a player on this match.")

    picked = {}
    if kit is not None:
        if kit not in choices["kits"]:
            raise HTTPException(status_code=400, detail=f"Unknown kit '{kit}'.")
        picked["kit"] = kit
    if powerup is not None:
        if powerup not in choices["powerups"]:
            raise HTTPException(status_code=400, detail=f"Unknown powerup '{powerup}'.")
        picked["powerup"] = powerup

    if picked:
        with session() as db:
            match.meta = match.get_returned_meta(steam64, **picked)
            db.query(data.Match).where(data.Match.id == matchId).update(
                {"meta": match.meta}
            )

        if match.all_kits_locked():
            collapsed = match.collapsed_meta_if_nobody_is_picking()
            if collapsed is not None:
                with session() as db:
                    db.query(data.Match).where(data.Match.id == matchId).update(
                        {"meta": collapsed}
                    )
                match.meta = collapsed

    return {"matchId": matchId, "choose": _choices(match, steam64)}


@router.get("/gamemodes", response_model=contracts.Gamemodes)
def gamemodes():
    """What a player can queue for, in menu order, for a server that asks in game.
    `bit` is the gamemode's position in the whole list, which is what a queue mask means.
    """
    modes = []
    for gamemode in settings.visible_mmconfig["gamemodes"]:
        players = gamemode["playersPerTeam"]
        parts = [
            ", ".join(kit.upper() for kit in gamemode["kitsAllowed"]),
            "v".join([str(players)] * gamemode["teams"]),
        ]
        if gamemode["usesElo"]:
            parts.append("Ranked")
        modes.append(
            {
                "bit": gamemode["bit"],
                "name": gamemode["name"],
                "blurb": "   ".join(parts),
                "usesElo": gamemode["usesElo"],
            }
        )
    return {"gamemodes": modes}


@router.post(
    "/lobby/population",
    response_model=contracts.Status,
    openapi_extra=body_schema(contracts.LobbyPopulationReport),
)
def lobby_population(server: GameServer, payload: JsonBody):
    """How many are in this lobby right now, once per heartbeat.

    Body:
        players   connected to the lobby and not on a match.
        queueing  how many of those want a match. Optional.
        held      how many the lobby is keeping out of the queue. Optional.

    Optional counts are stored as absent rather than zero, so an older lobby that does
    not report them never reads as nobody wanting a match.
    """
    report = _validated(contracts.LobbyPopulationReport, payload)
    data.LobbyPopulation.report(server.address, report.players)
    data.ArenaReading.record(
        server.address, report.players, report.queueing, report.held
    )
    return {"status": "recorded"}


@router.get("/players/{steam64}", response_model=contracts.PlayerState)
def gameserver_player(steam64: int):
    """Everything a server needs to know about one player. Asking does not queue them."""
    with session() as db:
        ban = data.UserBan.barring([steam64], db).get(steam64)
    return _player(steam64, data.Match.current_for(steam64), ban)


@router.get("/players/{steam64}/kits", response_model=dict[str, contracts.SavedKit])
def player_kits(steam64: int):
    """Every kit this player has saved, resolved into ready-to-equip items, with the
    magazine broken out so a caller can keep the spare topped up."""
    return {
        category: {
            "items": _items(kit["items"]),
            "magazine": _items([kit["magazine"]])[0] if kit["magazine"] else None,
        }
        for category, kit in data.Kit.public_data(steam64).items()
    }


@router.put("/players/{steam64}/queue", response_model=contracts.PlayerState)
def join_queue(
    steam64: int, gamemodes: int = 1, region: str = "paris", ip: str | None = None
):
    """Put this player in the matchmaking queue, or keep them in it.

    Called on a timer for everyone in a lobby, so it is both "join" and "still here".
    `gamemodes` is a bitmask and `ip` the address they play from. Answers like
    `GET /players/{steam64}`. A barred player is refused, and the ban comes back on
    the `ban` field.
    """
    if ip:
        with session() as db:
            data.UserIP.remember(db, steam64, ip)

    matchId = data.Match.current_for(steam64)
    if matchId:
        with session() as db:
            ban = data.UserBan.barring([steam64], db).get(steam64)
    else:
        ban = data.User.add_to_queue(steam64, gamemodes, region=region)
    return _player(steam64, matchId, ban)


@router.delete("/players/{steam64}/queue", response_model=contracts.Status)
def leave_queue(steam64: int):
    data.User.remove_from_queue(steam64)
    return {"status": "ok"}


@router.post("/reports", response_model=contracts.ReportAccepted)
async def submit_report(
    reported: int = Form(...),
    reporter: int = Form(...),
    reason: str = Form(...),
    serverCode: str | None = Form(None),
    where: str = Form("a PeekSeek server"),
    channel: str | None = Form(None),
    staffRole: str | None = Form(None),
    damageDealt: int = Form(0),
    shotsFired: int = Form(0),
    shotsHit: int = Form(0),
    headshotHits: int = Form(0),
    screenshot: UploadFile | None = File(None),
):
    """An in-game /report, multipart so the reported player's screenshot arrives with it.

    The counters are the server's own, since a lobby and a match server reset them at
    different times.
    """
    image = await screenshot.read() if screenshot is not None else None

    sent = reports.send(
        reported=reported,
        reporter=reporter,
        reason=reason[:1000],
        stats={
            "damageDealt": damageDealt,
            "shotsFired": shotsFired,
            "shotsHit": shotsHit,
            "headshotHits": headshotHits,
        },
        serverCode=serverCode,
        where=where,
        screenshot=image,
        channel=channel or None,
        staffRole=staffRole or None,
    )
    # 200 even when Discord is down: there is nothing the server could do about it.
    return {"status": "ok", "delivered": sent}


@router.post(
    "/players/{steam64}/bans",
    response_model=contracts.Status,
    openapi_extra=body_schema(contracts.GuardBan),
)
def ban_player(steam64: int, payload: JsonBody):
    """A ban issued by anti-cheat on a game server. An empty body bans for "Cheating"."""
    reason = _validated(contracts.GuardBan, payload).reason or "Cheating"
    with session() as db:
        db.add(
            data.UserBan(
                steam64=steam64,
                endTime=data.UserBan.PERMANENT,
                reason=reason,
                moderatorNote="by PeekSeek Guard",
            )
        )
    return {"status": "ok"}
