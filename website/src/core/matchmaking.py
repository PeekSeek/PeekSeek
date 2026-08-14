"""Turning queued heartbeats into matches, and match results into stats.

The queue itself lives on UserHB (freshInQueue/dequeue) and stale-match
cancellation on Match.cancelStaleUnclaimed — this module only orchestrates.
"""

import logging
from time import time

from .db import open
from .settings import settings
from . import models as data

mmconfig = settings.mmconfig


def attemptCreateMatch(trackSteam64: int | None = None, region: str = "paris") -> bool:
    """Scan the queue and create a match per gamemode that has enough players.

    Returns True when trackSteam64 was placed into one of them.
    """
    with open() as db:
        queued = data.UserHB.freshInQueue(db)

    perGamemode: dict[int, list] = {
        gid: [] for gid in range(len(mmconfig["gamemodes"]))
    }
    for heartbeat in queued:
        if heartbeat.region != region:
            continue
        for gid in perGamemode:
            if heartbeat.gamemode & (2**gid):
                perGamemode[gid].append(heartbeat)

    tracked_matched = False
    for gid, queueList in perGamemode.items():
        players = _playersForMatch(queueList, mmconfig["gamemodes"][gid])
        if not players:
            continue
        _createMatch(gid, players, region)
        if trackSteam64 and str(trackSteam64) in [str(p) for p in players]:
            tracked_matched = True
    return tracked_matched


def modesFittingParty(gamemode: int, partySize: int) -> int:
    """The selected modes a party this size can actually be matched in.

    _playersForMatch refuses to split a party across two teams, so a party larger
    than one team of a mode can never fill it — leaving that mode selected only
    buys them a queue they never come out of. Dropping it here is what turns that
    silent wait into either a queue that can match or an outright refusal.
    """
    return sum(
        2**gid
        for gid, mode in enumerate(mmconfig["gamemodes"])
        if gamemode & (2**gid) and mode["playersPerTeam"] >= partySize
    )


def _partyGroups(heartbeats: list) -> list[list[int]]:
    """Queued players grouped by party — a solo player is a group of one — ordered by
    the earliest heartbeat in the group, so queueing together doesn't jump a party
    ahead of players who'd already been waiting."""
    groups: dict[object, list[int]] = {}
    earliest: dict[object, float] = {}
    for hb in heartbeats:
        if data.User.getCurrentMatchId(hb.steam64):
            continue
        key = hb.partyId or f"solo-{hb.steam64}"
        groups.setdefault(key, []).append(hb.steam64)
        earliest[key] = min(earliest.get(key, hb.time), hb.time)
    return [g for _, g in sorted(groups.items(), key=lambda kv: earliest[kv[0]])]


def _playersForMatch(queueList: list, mode: dict) -> list[int] | None:
    """Fills every team of the mode at once, keeping each party together on a single
    team — first-fit over the (small, fixed) number of teams. A party larger than one
    team can't be matched here at all and just keeps waiting; nothing in this loop
    ever splits one across two teams."""
    perTeam = mode["playersPerTeam"]
    groups = [g for g in _partyGroups(queueList) if len(g) <= perTeam]

    teams: list[list[int]] = [[] for _ in range(mode["teams"])]
    for group in groups:
        for team in teams:
            if len(team) + len(group) <= perTeam:
                team.extend(group)
                break
        if all(len(team) == perTeam for team in teams):
            break

    if not all(len(team) == perTeam for team in teams):
        return None
    return [player for team in teams for player in team]


def _createMatch(gamemode: int, players: list[int], region: str) -> None:
    perTeam = mmconfig["gamemodes"][gamemode]["playersPerTeam"]
    teams = [
        [str(s) for s in players[i * perTeam : (i + 1) * perTeam]]
        for i in range(mmconfig["gamemodes"][gamemode]["teams"])
    ]
    with open() as db:
        db.add(
            data.Match(
                gamemode=gamemode,
                teams=[
                    {
                        "name": f"team_{data.User.getSteamData(t[0]).name}"[:15],
                        "users": t,
                    }
                    for t in teams
                ],
                region=region,
            )
        )
    data.UserHB.dequeue(players)


def attemptCreateEventMatch(trackSteam64: int, eventId: int) -> bool:
    """Pair up event teams whose owners are queueing. True if trackSteam64's
    team got a match."""
    with open() as db:
        event = db.query(data.Event).where(data.Event.id == eventId).first()
        in_queue_time = any(
            start <= int(time()) <= end
            for start, end in event.data["meta"]["queueTimes"]
        )
        if not in_queue_time:
            return False

        teams = []
        for heartbeat in data.UserHB.freshInQueue(db, eventId=eventId):
            team = _eventTeam(db, heartbeat, event)
            if team:
                teams.append(team)

        if len(teams) < 2:
            return False

        db.add(
            data.Match(
                gamemode=event.data["meta"].get("gamemode", 1),
                teams=teams[:2],
                region=event.data["meta"].get("region", "paris"),
                eventId=eventId,
                spectators=[str(x) for x in event.data.get("spectators", [])],
            )
        )

    data.UserHB.dequeue([team["users"][0] for team in teams[:2]])
    return any(int(team["users"][0]) == int(trackSteam64) for team in teams[:2])


def _eventTeam(db, heartbeat, event) -> dict | None:
    """The heartbeat owner's team entry, or None if the team can't play now."""
    if data.User.getCurrentMatchId(heartbeat.steam64):
        return None

    signup = (
        db.query(data.EventSignup)
        .where(data.EventSignup.ownerSteam64 == heartbeat.steam64)
        .first()
    )
    if not signup:
        return None
    if signup.matchesplayed >= event.data["meta"]["maxMatchesPerTeam"]:
        return None

    # In event queues heartbeat.gamemode carries the teammate pick as a bitmask.
    teammates = [
        s
        for i, s in enumerate(signup.users)
        if f"{heartbeat.gamemode:0{len(signup.users)}b}"[i] == "1"
    ]
    if len(teammates) + 1 > event.data["meta"]["maxPlayersPerTeam"]:
        return None

    return {
        "name": signup.teamname,
        "users": [str(x) for x in [heartbeat.steam64, *teammates]],
    }


def matchResults(gamemodeInfo: dict, *users: dict) -> tuple[dict, int]:
    """Record every player's kills/deaths/win and, for ranked modes, their Elo
    change. Returns the per-player summary and the match's average Elo."""
    avgElo = sum(data.Stat.getElo(u["steam64"]) for u in users) / len(users)
    results: dict = {}

    for user in users:
        won = bool(user["win"])
        # .get() with a default so results from a plugin build that predates a given
        # stat (e.g. the deployed 1.0) don't fail the whole match report.
        headshots = user.get("headshots", 0)
        damageDealt = user.get("damageDealt", 0)
        damageTaken = user.get("damageTaken", 0)
        summary = {
            "Kills": user["kills"],
            "Deaths": user["deaths"],
            "Headshots": headshots,
            "Won": user["win"],
        }

        eloDelta = 0
        if gamemodeInfo["usesElo"]:
            currentElo = data.Stat.getElo(user["steam64"])
            eloDelta = _eloDelta(currentElo, avgElo, user["kills"], user["deaths"], won)
            summary = {"Elo Change": eloDelta, "Current Elo": currentElo} | summary

        results[user["steam64"]] = summary
        data.Stat.append(
            user["steam64"],
            "unt",
            kills=user["kills"],
            deaths=user["deaths"],
            wins=int(won),
            losses=int(not won),
            elo=eloDelta,
            headshots=headshots,
            damageDealt=damageDealt,
            damageTaken=damageTaken,
        )

    return results, int(avgElo)


def _eloDelta(
    currentElo: int, avgElo: float, kills: int, deaths: int, won: bool
) -> int:
    """The ranked Elo rule: tiny losses for underdogs, tiny gains for smurfs,
    otherwise performance-scaled — and never below the floor of 100."""
    if currentElo + 150 < avgElo and not won:
        delta = -2
    elif currentElo - 150 > avgElo and won:
        delta = 5
    else:
        performance = 20 - (0 if won else 40) + int(3.5 * kills) - int(2.5 * deaths)
        delta = int(performance * min(avgElo / currentElo, 2.5))
    return max(delta, 100 - currentElo)


def eventResults(eventId: int, matchid: int, *users: dict) -> str:
    """Bump event scores/matches-played from a finished match and format the
    Discord summary."""
    if not matchid:
        raise ValueError("Invalid Match Id")

    with open() as db:
        match = db.query(data.Match).where(data.Match.id == matchid).first()
        results: list[dict] = []

        for team in match.teams:
            owner = int(team["users"][0])
            signup = (
                db.query(data.EventSignup)
                .where(
                    data.EventSignup.event_id == eventId,
                    data.EventSignup.ownerSteam64 == owner,
                )
                .first()
            )
            if not signup:
                logging.error("No signup for team %s on event %s", team, eventId)
                continue

            signup.matchesplayed += 1

            ownerResult = next((u for u in users if int(u["steam64"]) == owner), None)
            if not ownerResult:
                continue
            if ownerResult["win"]:
                signup.score += 1

            results.append(
                {
                    "name": signup.teamname,
                    "win": ownerResult["win"],
                    "users": "\n".join(
                        f"**[{u['steam64']}]({settings.profile_url(u['steam64'])})**"
                        f" - [steam profile](https://steamcommunity.com/profiles/{u['steam64']})"
                        f"\n**Kills** {u['kills']}\n**Deaths** {u['deaths']}"
                        for u in users
                        if str(u["steam64"]) in team["users"]
                    ),
                }
            )

        return "\n\n".join(
            f"team\\_**{t['name']}**\n**win** {t['win']}\n{t['users']}" for t in results
        )
