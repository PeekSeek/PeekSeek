"""The game-server API: `/server/v1/*`.

Everything a game server needs to run PeekSeek matches, and nothing else. It is
deliberately not Unturned-specific — a server for a different game implements the same
calls and speaks the same JSON.

Conventions, all of which hold for every route here:

- Auth is the caller's source IP, allow-listed in `AuthorizedServers`.
- Field names are camelCase.
- Steam64s are **strings**. They are 64-bit and JSON numbers are not, so anything
  parsing them as a number loses the bottom digits.
- Every response is a JSON object, never a bare string or array, so a field can be
  added later without changing the shape.
- Durations are seconds; timestamps are Unix seconds.
- Writes that can be retried are idempotent, and say which of the two happened.

The whole lifecycle runs through one call, `GET /session`. See its docstring.
"""

from fastapi import APIRouter, Depends, File, Form, HTTPException, UploadFile

from src.core.settings import settings
from src.core.db import open as opendb
from src.core import models as data
from src.core import matchmaking, notify, reports
from src.core.webhook import Webhook
from src.dependencies import GameServer, JsonBody, authorized_server

router = APIRouter(
    prefix="/server/v1",
    tags=["game server"],
    dependencies=[Depends(authorized_server)],
)

mmconfig = settings.mmconfig


def _items(rawItems: list) -> list[dict]:
    """Kit items as self-describing objects rather than positional tuples.

    Stored as `[itemId, amount, "1,2,3"]` because that is what the site's own kit
    editor writes; sent as `{"itemId": …, "amount": …, "state": [1, 2, 3]}` because a
    server implementing this API should not have to know that index 2 is a
    comma-separated byte string.
    """
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
        "gamemode": {"id": match.gamemode, "name": gamemode["name"]},
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
                "kit": _items(data.Kit.getItems(steam64, match)),
            }
            for steam64 in match.meta["userData"]
        ],
        "spectators": [str(s) for s in match.spectators],
    }


@router.get("/session")
def session(server: GameServer, serverCode: str = "0", matchId: int = 0):
    """The one call a game server makes on a loop. Answers what it should be doing.

    `matchId` is whatever match the server currently considers itself responsible for —
    including one it has finished playing but not yet successfully reported. `0` means
    it is free, and a free server is what makes a match: matchmaking runs *here*, so a
    match is only ever created for a server that is standing by to host it.

    Exactly one of four answers comes back, and `action` is the only field to switch on:

    - `{"action": "idle"}` — nothing to do. Ask again shortly.
    - `{"action": "wait", "matchId": n, "waitingOn": [...], "selectionEndsAt": ts}` —
      this match is yours, but its players are still choosing. Hold it, keep sending
      this `matchId`, and do not let anyone in yet.
    - `{"action": "host", "match": {…}}` — start it. Sent exactly once per match: ask
      again while hosting and the answer is `idle`, so a slow poll cannot restart a
      round that is already being played.
    - `{"action": "abort", "matchId": n, "reason": "…"}` — drop it. It is not a match
      any more, and nobody is coming.

    A server that still names a match is never told that match was abandoned, which is
    what makes the result report safe to retry indefinitely: for as long as the server
    keeps saying "I am still holding match 42", the site will not decide match 42 was
    dropped.
    """
    # Both housekeeping sweeps only ever touch matches this call is not about — one
    # closes matches held by a server that has just said it holds none, the other
    # cancels matches no server holds at all — so neither can disturb a live match.
    if matchId:
        answer = _answerAboutHeldMatch(matchId, serverCode)
        data.Match.cancelStranded()
        return _stamped(answer, server)

    data.Match.releaseAbandoned(serverCode)
    data.Match.resolveExpiredPicks()

    # Assign before cancelling, or a match could be scrapped in the same breath as the
    # server that was about to take it reported itself free.
    match = matchmaking.matchForServer(server.region, serverCode)
    data.Match.cancelStranded()

    answer = _hostOrWait(match) if match else {"action": "idle"}
    return _stamped(answer, server)


def _stamped(answer: dict, server: data.AuthorizedServer) -> dict:
    """Every answer carries the region this server is registered in, and how many
    players are queued there.

    A server only ever gets matches from its own region, and that region is set in the
    database rather than by the server itself — so a mismatch is invisible from the
    server's side and looks exactly like "nobody is queueing". Saying both here turns a
    silent no-op into something an operator can read straight off the console.
    """
    return answer | {
        "region": server.region,
        "queuedInRegion": data.UserHB.countFresh(server.region),
    }


def _answerAboutHeldMatch(matchId: int, serverCode: str) -> dict:
    """What to tell a server that is already holding a match."""
    with opendb() as db:
        match = db.query(data.Match).where(data.Match.id == matchId).first()

    if match is None:
        return {"action": "abort", "matchId": matchId, "reason": "unknownMatch"}
    if match.finished:
        return {"action": "abort", "matchId": matchId, "reason": "matchOver"}
    if match.claimed not in (serverCode, "0", None):
        return {"action": "abort", "matchId": matchId, "reason": "hostedElsewhere"}

    if match.status == data.Match.LIVE:
        return {"action": "idle"}

    resolved = match.resolvedMetaIfExpired()
    if resolved is not None:
        with opendb() as db:
            db.query(data.Match).where(data.Match.id == matchId).update(
                {"meta": resolved}
            )
        match.meta = resolved

    return _hostOrWait(match)


def _hostOrWait(match: data.Match) -> dict:
    """Hand over a match whose selection is done, or say what it is still waiting on."""
    if not match.readyToHost():
        return {
            "action": "wait",
            "matchId": match.id,
            "waitingOn": match.waitingOn(),
            "selectionEndsAt": int(match.selectionEndsAt()),
        }

    payload = _match(match)
    # Stamped only once the payload has been built without raising: a match marked as
    # hosted is never handed out again, so a serialiser that failed here would leave a
    # match nobody was ever told to run.
    match.markHosting()
    return {"action": "host", "match": payload}


@router.get("/matches/{matchId}")
def match(matchId: int):
    """A match's current state, for anything waiting on it.

    `relayTo` is the whole point: it is the server code to send a waiting player to, and
    it stays null until the match is actually being hosted. Anything holding players —
    the lobby server, in particular — can send them the moment it is not null and be
    sure they will be let in.
    """
    with opendb() as db:
        current = db.query(data.Match).where(data.Match.id == matchId).first()
    if current is None:
        raise HTTPException(status_code=404, detail="No such match.")

    return {
        "id": current.id,
        "region": current.region,
        "status": current.status,
        "relayTo": current.connectCode(),
    }


@router.post("/matches/{matchId}/state")
def matchState(matchId: int, payload: JsonBody):
    """The live snapshot the stream overlay renders — scoreboard, health, positions.

    Sent on a timer during play. Lossy on purpose: each one replaces the last, so a
    dropped snapshot costs nothing and no server should retry one.
    """
    data.Match.setLiveData(matchId, payload)
    return {"status": "ok"}


@router.post("/matches/{matchId}/kills")
def matchKill(matchId: int, payload: JsonBody):
    """One kill, reported as it happens rather than batched at the end, so a server
    that dies mid-match does not take the whole match's kill log with it."""
    data.MatchResult.appendKill(matchId, payload)
    return {"status": "ok"}


@router.post("/matches/{matchId}/result")
def matchResult(matchId: int, payload: JsonBody):
    """The match's final per-player line. This is what moves Elo, and what ends a match.

    Safe to retry, and meant to be: a server keeps sending this until it gets a reply,
    because this call is the only thing that scores a match. Repeats are answered with
    `alreadyRecorded` and change nothing, so a reply lost on the way back costs a
    duplicate request rather than a doubled Elo swing.

    Accepted even for a match already marked finished, because being marked finished is
    exactly what happens to a match whose result was slow to arrive.

    Body:
        played       false when the match was never actually played (nobody turned up).
                     A false here scores nothing and just releases the match.
        winningTeam  the `name` of the winning team, or null.
        players      every player on the roster — including ones who never joined and
                     ones who left. A player the server omits is a player whose Elo
                     never moves, which is why leaving used to be free.
    """
    with opendb() as db:
        current = db.query(data.Match).where(data.Match.id == matchId).first()
    if current is None:
        raise HTTPException(status_code=404, detail="No such match.")

    if data.MatchResult.hasResults(matchId):
        return {"status": "alreadyRecorded", "matchId": matchId}

    try:
        _score(current, payload)
    except Exception as error:
        Webhook(f"Match.{matchId}").error(
            "Result rejected",
            f"`{error}`\n```json\n{payload}\n```",
        )
        raise HTTPException(status_code=400, detail=f"Could not score result: {error}")

    with opendb() as db:
        db.query(data.Match).where(data.Match.id == matchId).update(
            {"finished": True, "livedata": {}, "spectators": []}
        )
    return {"status": "recorded", "matchId": matchId}


def _score(match: data.Match, payload: dict) -> None:
    """Apply one result payload: event standings, or ranked Elo, or nothing at all for a
    match that was never played."""
    players = payload.get("players") or []

    eventId = match.meta.get("eventId")
    if eventId:
        Webhook(f"Event.{eventId}").info(
            f"Match.{match.id} ({match.region}) Results",
            matchmaking.eventResults(eventId, match.id, *players),
        )
        return

    if not payload.get("played") or not players:
        data.MatchResult.recordUnscored(match.id, "notPlayed")
        return

    gamemode = mmconfig["gamemodes"][match.gamemode]
    result, avgElo = matchmaking.matchResults(gamemode, *players)
    # Taken off the live snapshot here because recording the result is what clears it.
    data.MatchResult.recordFinalResults(match.id, result, _roundScore(match))

    lines = "\n\n".join(
        f"{data.User.getSteamData(s).name} ({s})\n"
        f"**[PeekSeek profile]({settings.profile_url(s)})** - "
        f"[Steam profile](https://steamcommunity.com/profiles/{s})\n"
        + "\n".join(f"**{name}** {value}" for name, value in stats.items())
        for s, stats in result.items()
    )
    Webhook(f"Match.{match.id} ({match.region})").info(
        "Results", f"***Average Elo** {avgElo}*\n\n{lines}"
    )
    _postResultPublicly(match, result, avgElo)


def _postResultPublicly(match: data.Match, result: dict, avgElo: int) -> None:
    """The same result again, in the channel players actually read.

    Separate from the Webhook call above rather than replacing it: that one goes to
    the ops log and carries whatever an on-call reader needs, this one is a scoreboard
    for people deciding whether anything is happening here. Twenty of these on a
    Friday evening is the cheapest proof the game is alive that we have.
    """
    winners = [steam64 for steam64, stats in result.items() if stats.get("Won")]
    names = {steam64: data.User.getSteamData(steam64).name for steam64 in result}
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


def _roundScore(match: data.Match) -> list:
    """The rounds each team won, read off the match's last live snapshot.

    The snapshot is the only place the round tally exists — the result report says who
    won, not by how much — and it is cleared the moment the result is recorded, so this
    is the last instant it can be kept.
    """
    return [
        {
            "name": team.get("teamName"),
            "wins": team.get("wins", 0),
            "losses": team.get("losses", 0),
        }
        for team in (match.livedata or {}).get("teams", [])
    ]


def _player(steam64: int) -> dict:
    """One player as every player-facing route here reports them: their rank, and where
    they should be. `matchId` without a `relayTo` means "matched, still choosing", and
    `choose` is what they are still choosing between."""
    matchId = data.User.getCurrentMatchId(steam64)
    match = None
    if matchId:
        with opendb() as db:
            match = db.query(data.Match).where(data.Match.id == matchId).first()

    # Reported on every player read, not just on the queue call, so a lobby learns
    # about a ban that lands while somebody is already standing in the arena rather
    # than only when it next tries to queue them.
    ban = data.UserBan.activeFor(steam64)

    return {
        "steam64": str(steam64),
        "elo": data.Stat.getElo(steam64),
        "matchId": matchId or None,
        "relayTo": match.connectCode() if match else None,
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
    decided so far. Null when there is nothing for them to decide.

    A list with one entry in it is not a choice — `Kit.getItems` resolves those itself
    — so it comes back empty rather than as a menu with one item on it. That is what
    lets a caller treat "empty" as "do not prompt" without knowing the gamemode.
    """
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


@router.post("/players/{steam64}/decide")
def decidePlayer(steam64: int, kit: str | None = None, powerup: str | None = None):
    """Lock this player's kit and/or powerup for the match they are on.

    The in-game half of the pick screen. Until this existed the only place a pick could
    be made was the match page on the site, so a player standing in a lobby had no way
    to make one at all — and `readyToHost` waits on every pick being made or
    `selectKitsTime` running out, which is the whole of the delay between "you have a
    match" and being teleported into it.

    Both fields are optional and independent, so a caller can lock a kit the moment it
    knows one and leave the powerup for the player to choose. Values are checked
    against the gamemode rather than trusted: an unknown kit category reaches
    `Kit.getItems` as a missing key, and an unknown powerup equips nothing at all.

    Once every player has locked both, the outstanding map/ballistics/movement picks
    are collapsed rather than waited on. Nobody who locked from in-game is at a browser
    to make them, so what is left of that phase is dead time.

    Answers with the same `choose` block `GET /players/{steam64}` returns, so a caller
    can prompt again off the reply without a second call.
    """
    matchId = data.User.getCurrentMatchId(steam64)
    if not matchId:
        raise HTTPException(status_code=404, detail="No match found.")

    with opendb() as db:
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
        with opendb() as db:
            match.meta = match.getReturnedMeta(steam64, **picked)
            db.query(data.Match).where(data.Match.id == matchId).update(
                {"meta": match.meta}
            )

        if match.allKitsLocked():
            collapsed = match.collapsedMetaIfNobodyIsPicking()
            if collapsed is not None:
                with opendb() as db:
                    db.query(data.Match).where(data.Match.id == matchId).update(
                        {"meta": collapsed}
                    )
                match.meta = collapsed

    return {"matchId": matchId, "choose": _choices(match, steam64)}


@router.post("/lobby/population")
def lobbyPopulation(server: GameServer, payload: JsonBody):
    """What this lobby's arena looks like right now.

    Sent once per heartbeat for the whole server rather than per player, because every
    number here is one fact about the server and not a fact about anybody in it. Nothing
    reads the reply — this exists so the site can say how alive the game is somewhere the
    Unturned browser cannot reach, which today means the Discord counter, the arena
    history, and the announcement when an arena that has been dead for hours wakes up.

    Body:
        players   how many are connected to the lobby and not already on a match.
        queueing  how many of those want a match. Optional.
        held      how many of those the lobby is keeping out of the queue to protect
                  its own browser listing. Optional.

    `queueing` and `held` are optional because a lobby build that predates them reports
    neither, and a lobby that does no holding of its own has nothing to say about either.
    They are stored as absent rather than as zero, so "this build does not report it"
    never reads as "nobody wanted a match".
    """
    players = int(payload.get("players", 0))
    data.LobbyPopulation.report(server.address, players)
    data.ArenaReading.record(
        server.address,
        players,
        _optionalCount(payload, "queueing"),
        _optionalCount(payload, "held"),
    )
    return {"status": "recorded"}


def _optionalCount(payload: dict, field: str) -> int | None:
    value = payload.get(field)
    return None if value is None else int(value)


@router.get("/players/{steam64}")
def player(steam64: int):
    """Everything a server needs to know about one player, in one call.

    Deliberately side-effect free — asking does not put anyone in a queue. That is what
    makes it safe to poll for players who have opted out of matchmaking but could still
    be pulled into a match made elsewhere (on the site, or by their party).
    """
    return _player(steam64)


@router.get("/players/{steam64}/kits")
def playerKits(steam64: int):
    """Every kit this player has saved, resolved into ready-to-equip items.

    One entry per category (`meta`, `sniper`, …). `magazine` is broken out from `items`
    rather than being one of them so a caller can keep the spare topped up without
    having to work out which item in the list was the magazine.
    """
    return {
        category: {
            "items": _items(kit["items"]),
            "magazine": _items([kit["magazine"]])[0] if kit["magazine"] else None,
        }
        for category, kit in data.Kit.publicData(steam64).items()
    }


@router.put("/players/{steam64}/queue")
def joinQueue(steam64: int, gamemodes: int = 1, region: str = "paris"):
    """Put this player in the matchmaking queue, or keep them in it.

    Called on a timer for everyone standing in a server-side lobby, so it is both "join"
    and "still here". `gamemodes` is a bitmask of the gamemodes the player will accept.
    Answers with the same shape as `GET /players/{steam64}`, so one call both queues
    them and says whether they now have somewhere to be.

    Queueing never creates a match. Matches are made by free game servers asking for
    work — see `GET /session` — so a player who queues when every server is busy waits
    in the queue rather than in a match that has nowhere to be played.

    No party support, on purpose: a lobby server knows who is connected to it, not who
    is grouped with whom on the site.
    """
    # addToQueue refuses a banned player and says so; the ban reaches the lobby on the
    # `ban` field of the reply either way, so there is nothing extra to do with it here.
    if not data.User.getCurrentMatchId(steam64):
        data.User.addToQueue(steam64, gamemodes, region=region)
    return _player(steam64)


@router.delete("/players/{steam64}/queue")
def leaveQueue(steam64: int):
    data.User.removeFromQueue(steam64)
    return {"status": "ok"}


@router.post("/reports")
async def submitReport(
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
    """An in-game /report, with the reported player's spy screenshot attached.

    Multipart rather than JSON because the screenshot is the point: it is captured by
    asking the reported player's own client (`Player.sendScreenshot`), which only the
    game server can do, and it has to arrive with the report rather than as a second
    call that could fail on its own and leave a report with no evidence.

    The counters are sent rather than derived here. A lobby server and a match server
    count "this round" differently, one resetting on an arena wipe and the other on a
    round, and only they know which of the two they are.
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
        # A server may route its own reports elsewhere; empty means the site's.
        channel=channel or None,
        staffRole=staffRole or None,
    )
    # 200 either way: the report reached the site, and a game server has nothing it
    # could usefully do about Discord being down. It is logged on this side instead.
    return {"status": "ok", "delivered": sent}


@router.post("/players/{steam64}/bans")
def banPlayer(steam64: int, payload: JsonBody):
    """A ban issued by anti-cheat on a game server. An empty body bans for "Cheating"."""
    reason = payload.get("reason") or "Cheating"
    with opendb() as db:
        db.add(
            data.UserBan(
                steam64=steam64,
                endTime=data.UserBan.PERMANENT,
                reason=reason,
                moderatorNote="by PeekSeek Guard",
            )
        )
    return {"status": "ok"}
