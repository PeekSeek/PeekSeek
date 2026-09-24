"""Turning queued players into matches, and match results into stats.

The queue itself lives on UserHB (freshInQueue/claim) and a match's lifecycle on
Match — this module only orchestrates.

Matchmaking runs when a game server asks for work, never when a player asks for a
match. A player queueing only ever adds themselves to the queue; the moment a server
in their region is free, that server's own poll is what scans the queue, forms the
best match it can, and takes it. That inversion is the whole point: a match cannot be
created without a server already holding it, so there is no such thing as a match with
nowhere to be played.
"""

import logging
import threading
from datetime import datetime, timedelta, timezone
from itertools import combinations
from time import time

from sqlalchemy import false, or_

from .db import open
from .settings import settings
from . import models as data
from . import notify

mmconfig = settings.mmconfig


def matchForServer(region: str, serverCode: str) -> "data.Match | None":
    """Find this free server the best match it can host right now, and give it to it.

    Tried in order:

    1. A match that already exists and has nobody to host it — a custom match, or an
       event pairing. Those are made by players rather than by matchmaking, so they are
       the ones actually waiting on a server.
    2. A fresh match built out of the queue, created already assigned to this server.

    Returns the match this server is now holding, or None when there is nothing to do.
    """
    waiting = data.Match.pendingFor(region)
    if waiting:
        # None when another server claimed it between the read and the write. Falling
        # through rather than returning it is the point: this server is still free, and
        # the queue may well have a match in it for them.
        taken = waiting.assignTo(serverCode)
        if taken:
            return taken

    return _matchFromQueue(region, serverCode)


def _matchFromQueue(region: str, serverCode: str) -> "data.Match | None":
    """The first gamemode in this region with a full complement of queued players, made
    into a match and handed to the calling server.

    One match per call on purpose: one server can only host one match, and a second
    match made in the same breath would have no server of its own to go to.

    Anyone already on an unfinished match is dropped before the scan starts rather than
    checked once a roster has been built. A player can be in both places at once: lobby
    servers re-queue everyone standing in them on a timer, and a player matched a moment
    ago is still standing there. The queue on its own is not a list of who is free.
    """
    inAMatch = data.Match.playersInAMatch()
    with open() as db:
        queued = [
            heartbeat
            for heartbeat in data.UserHB.freshInQueue(db)
            if heartbeat.region == region and str(heartbeat.steam64) not in inAMatch
        ]

    for gamemodeId, gamemode in enumerate(mmconfig["gamemodes"]):
        candidates = [hb for hb in queued if hb.gamemode & (2**gamemodeId)]
        players = _playersForMatch(candidates, gamemode)
        if players:
            return _createMatch(gamemodeId, players, region, serverCode)
    return None


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
    ahead of players who'd already been waiting.

    Everyone here is already known to be free: _matchFromQueue drops the players who
    are on a match before it hands the queue over.
    """
    groups: dict[object, list[int]] = {}
    earliest: dict[object, float] = {}
    for hb in heartbeats:
        key = hb.partyId or f"solo-{hb.steam64}"
        groups.setdefault(key, []).append(hb.steam64)
        earliest[key] = min(earliest.get(key, hb.time), hb.time)
    return [g for _, g in sorted(groups.items(), key=lambda kv: earliest[kv[0]])]


# How far down the queue to look for a better roster than the one at the front. Every
# combination of this many groups is tried, so it is a hard cap on the work done per
# poll: at 8 the very worst case is 70 rosters to score, which is nothing.
CANDIDATE_GROUPS = 8


def _playersForMatch(queueList: list, mode: dict) -> list[int] | None:
    """The best full roster this queue can currently produce, or None.

    "Best" is the first roster, taken longest-waiting-players first, that clears both
    of the fairness rules: teams within `max_team_elo_gap` of each other, and nobody
    facing an opponent they have already played in the last `rematch_cooldown`. Failing
    both is a real outcome — those players stay queued rather than being handed a
    one-sided match or the same fixture over and over.

    Parties are never split: a group either goes onto one team whole or waits.
    """
    perTeam = mode["playersPerTeam"]
    needed = perTeam * mode["teams"]
    groups = [g for g in _partyGroups(queueList) if len(g) <= perTeam]
    recentOpponents = _recentOpponentPairs()

    for roster in _rosters(groups[:CANDIDATE_GROUPS], needed):
        teams = _balancedTeams(roster, mode)
        if teams is None:
            continue
        if _eloGap(teams) > settings.max_team_elo_gap:
            continue
        if _wouldRepeatAFixture(teams, recentOpponents):
            continue
        return [player for team in teams for player in team]
    return None


def _rosters(groups: list[list[int]], needed: int) -> list[list[list[int]]]:
    """Every set of party groups that exactly fills the mode, ordered so the ones built
    from the longest-waiting players come first.

    Combinations rather than a single first-fit pass because the front of the queue is
    not always a legal or a fair match: sorting by group position keeps waiting time as
    the tie-breaker while still letting a fair roster be found one player deeper.
    """
    matching = [
        roster
        for size in range(1, len(groups) + 1)
        for roster in combinations(list(enumerate(groups)), size)
        if sum(len(group) for _, group in roster) == needed
    ]
    matching.sort(key=lambda roster: [position for position, _ in roster])
    return [[group for _, group in roster] for roster in matching]


def _balancedTeams(groups: list[list[int]], mode: dict) -> list[list[int]] | None:
    """Split these party groups across the mode's teams as evenly by Elo as they go.

    Biggest groups first onto whichever team is currently weakest and still has room —
    the standard greedy partition, which is more than enough at these sizes and keeps
    every party whole. None when the groups simply do not fit the shape of the mode.
    """
    perTeam = mode["playersPerTeam"]
    teams: list[list[int]] = [[] for _ in range(mode["teams"])]

    for group in sorted(groups, key=lambda g: (-len(g), -_groupElo(g))):
        room = [team for team in teams if len(team) + len(group) <= perTeam]
        if not room:
            return None
        min(room, key=_teamElo).extend(group)

    return teams if all(len(team) == perTeam for team in teams) else None


def _groupElo(group: list[int]) -> int:
    return sum(data.Stat.getElo(steam64) for steam64 in group)


def _teamElo(team: list[int]) -> float:
    """A team's strength: the average Elo of the players on it. An average rather than a
    total so the comparison still means something in a mode where a team can be short a
    player."""
    return _groupElo(team) / len(team) if team else 0


def _eloGap(teams: list[list[int]]) -> float:
    strengths = [_teamElo(team) for team in teams]
    return max(strengths) - min(strengths)


def _recentOpponentPairs() -> set[frozenset]:
    """Every pair of players who have been put on opposing teams recently.

    Read off the matches themselves rather than kept in a table of its own — a match
    already records who played whom and when, and a second copy of that is a second
    thing to keep in step. Counts matches that are still being played, not just
    finished ones, so back-to-back rematches are caught while the first one is live.
    """
    cooldown = settings.rematch_cooldown
    endedSince = datetime.now(timezone.utc) - timedelta(seconds=cooldown)

    with open() as db:
        # Three ways a match is still too fresh to run back, because Match.time is when
        # a match was *made*: a twenty-minute match is already outside a five-minute
        # cooldown by the time anybody could rematch, which is why this rule never fired
        # in production while passing every test -- the tests all rematch off a match
        # created seconds ago.
        endedRecently = {
            row.matchId
            for row in db.query(data.MatchResult)
            .where(data.MatchResult.endTime >= endedSince)
            .all()
            if row.matchId
        }
        recent = (
            db.query(data.Match)
            .where(
                or_(
                    # Still being played, so the rematch clock has not started at all.
                    data.Match.finished.is_(False),
                    # Scored or written off within the cooldown.
                    data.Match.id.in_(endedRecently) if endedRecently else false(),
                    # Made within it, for a finished match that never got a result row.
                    data.Match.time >= time() - cooldown,
                )
            )
            .all()
        )

    pairs = set()
    for match in recent:
        for index, team in enumerate(match.teams):
            for opposing in match.teams[index + 1 :]:
                for player in team["users"]:
                    for opponent in opposing["users"]:
                        pairs.add(frozenset((str(player), str(opponent))))
    return pairs


def _wouldRepeatAFixture(
    teams: list[list[int]], recentOpponents: set[frozenset]
) -> bool:
    """Whether this roster puts any two players against each other again too soon.

    One repeated pairing is enough to reject the whole roster, which is what makes
    (a+b vs c+d) block (a+b vs c+e): a and b would be facing c for a second time even
    though the fourth player changed.
    """
    return any(
        frozenset((str(player), str(opponent))) in recentOpponents
        for index, team in enumerate(teams)
        for opposing in teams[index + 1 :]
        for player in team
        for opponent in opposing
    )


def _createMatch(
    gamemode: int, players: list[int], region: str, serverCode: str
) -> "data.Match | None":
    """This roster, taken out of the queue and made into a match for the calling server.

    None when the queue would not give them up, which means another server claimed at
    least one of them between this scan and this call. Nothing is created in that case:
    the server is told to idle and asks again a second later.
    """
    # Before anything else, and before anyone is told a match exists. Building one means
    # a Steam lookup per player, a Discord webhook and a DM each, and every one of those
    # is time these players would otherwise spend still sitting in the queue looking
    # free. That window is what produced two matches for one pairing, five "Match
    # started" posts deep.
    if not data.UserHB.claim(players):
        return None

    perTeam = mmconfig["gamemodes"][gamemode]["playersPerTeam"]
    teams = [
        [str(s) for s in players[i * perTeam : (i + 1) * perTeam]]
        for i in range(mmconfig["gamemodes"][gamemode]["teams"])
    ]
    match = data.Match(
        gamemode=gamemode,
        teams=[
            # Cut to length and made distinct by Match itself, so no caller can
            # produce two teams that the game server would read as one.
            {"name": f"team_{data.User.getSteamData(t[0]).name}", "users": t}
            for t in teams
        ],
        region=region,
        # Assigned in the same breath as it is created: a matchmade match is only ever
        # made for a server that has just said it is free, so it never exists in the
        # unassigned state the way a custom or event match briefly does.
        claimed=serverCode,
    )
    with open() as db:
        db.add(match)
        db.flush()
        db.refresh(match)
        db.expunge(match)

    return match


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
        teams = _eventPairing(teams)
        if teams is None:
            return False
        meta = event.data["meta"]
        spectators = [str(x) for x in event.data.get("spectators", [])]

    # Same claim-first order as a matchmade match, for the same reason: two owners
    # queueing at once both used to reach here with the same two teams in hand.
    if not data.UserHB.claim([team["users"][0] for team in teams]):
        return False

    with open() as db:
        db.add(
            data.Match(
                gamemode=meta.get("gamemode", 1),
                teams=teams,
                region=meta.get("region", "paris"),
                eventId=eventId,
                spectators=spectators,
            )
        )

    return any(int(team["users"][0]) == int(trackSteam64) for team in teams)


def _eventPairing(teams: list[dict]) -> list[dict] | None:
    """The first two queued teams who have not just played each other, or None.

    Queue order first, so the pair that has been waiting longest still goes first when
    nothing is stale. Searching past that pair rather than stopping at it is what keeps
    a three-team event moving: the front two being on cooldown finds the third team a
    match instead of holding the whole event until the clock runs out. An event with
    only two teams does wait, which is what a cooldown is.
    """
    recentOpponents = _recentOpponentPairs()
    for home, away in combinations(teams, 2):
        roster = [[int(u) for u in home["users"]], [int(u) for u in away["users"]]]
        if not _wouldRepeatAFixture(roster, recentOpponents):
            return [home, away]
    return None


def _eventTeam(db, heartbeat, event) -> dict | None:
    """The heartbeat owner's team entry, or None if the team can't play now."""
    if data.User.getCurrentMatchId(heartbeat.steam64):
        return None

    # Scoped to this event. Without the event_id a player who had ever signed up for
    # anything got whichever row came back first, which is the oldest -- so a finished
    # event's signup, with its matchesplayed already at that event's cap, refused them a
    # match in the event they were actually queueing for. Deleting the old signups
    # "fixed" it because it left only one row to find.
    signup = (
        db.query(data.EventSignup)
        .where(
            data.EventSignup.event_id == event.id,
            data.EventSignup.ownerSteam64 == heartbeat.steam64,
        )
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


def normalizePlayerResult(player: dict) -> dict:
    """One game server's report of one player, in the shape matchResults works in.

    Accepts both the current server API's field names and the older `/unt/results`
    ones, so a server that has not been updated yet still scores identically. Every
    counter defaults, so a build that predates a given stat reports what it has rather
    than failing the whole match.
    """
    return {
        "steam64": str(player["steam64"]),
        "won": bool(player.get("won", player.get("win", False))),
        "kills": int(player.get("kills", 0)),
        "deaths": int(player.get("deaths", 0)),
        "headshotKills": int(player.get("headshotKills", player.get("headshots", 0))),
        "hits": int(player.get("hits", 0)),
        "headshotHits": int(player.get("headshotHits", 0)),
        "damageDealt": int(player.get("damageDealt", 0)),
        "damageTaken": int(player.get("damageTaken", 0)),
    }


def matchResults(gamemodeInfo: dict, *users: dict) -> tuple[dict, int]:
    """Record every player's kills/deaths/win and, for ranked modes, their Elo
    change. Returns the per-player summary and the match's average Elo.

    Every player the server lists is scored, including ones who never fired a shot or
    who disconnected — the server reports its whole roster, so leaving a match is a
    loss like any other rather than a way to keep your Elo.
    """
    players = [normalizePlayerResult(u) for u in users]

    # Before anything is read: a player returning after a new season opened plays this
    # match on their carried-over Elo, and it has to be the Elo the match is scored
    # against as well as the one the result lands on.
    if gamemodeInfo["usesElo"]:
        for player in players:
            data.Stat.startSeason(player["steam64"])

    avgElo = sum(data.Stat.getElo(p["steam64"]) for p in players) / len(players)
    results: dict = {}

    for player in players:
        won = player["won"]
        summary = {
            "Kills": player["kills"],
            "Deaths": player["deaths"],
            "Headshots": player["headshotKills"],
            "Hits": player["hits"],
            "HeadshotHits": player["headshotHits"],
            "Damage": player["damageDealt"],
            "Taken": player["damageTaken"],
            "Won": won,
        }

        eloDelta = 0
        if gamemodeInfo["usesElo"]:
            currentElo = data.Stat.getElo(player["steam64"])
            eloDelta = _eloDelta(
                currentElo, avgElo, player["kills"], player["deaths"], won
            )
            summary = {"Elo Change": eloDelta, "Current Elo": currentElo} | summary

        results[player["steam64"]] = summary
        data.Stat.append(
            player["steam64"],
            "unt",
            kills=player["kills"],
            deaths=player["deaths"],
            wins=int(won),
            losses=int(not won),
            elo=eloDelta,
            headshots=player["headshotKills"],
            hits=player["hits"],
            headshotHits=player["headshotHits"],
            damageDealt=player["damageDealt"],
            damageTaken=player["damageTaken"],
        )

    if gamemodeInfo["usesElo"]:
        _syncRankRoles([p["steam64"] for p in players])

    return results, int(avgElo)


def _syncRankRoles(steam64s: list) -> None:
    """Bring everybody's Discord rank role in line with the badge this match left them
    on, off the request that reported it.

    Two Discord calls per player, against a game server waiting to hear its result was
    recorded. The roles are decoration and the result is not, so they go on a thread and
    the report returns without them -- the same trade Steam profile lookups make.
    """

    def run():
        for steam64 in steam64s:
            notify.sync_rank_role(steam64)

    threading.Thread(target=run, daemon=True).start()


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


def eventResults(eventId: int, matchid: int, *rawUsers: dict) -> str:
    """Bump event scores/matches-played from a finished match and format the
    Discord summary."""
    if not matchid:
        raise ValueError("Invalid Match Id")

    users = [normalizePlayerResult(u) for u in rawUsers]

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
            if ownerResult["won"]:
                signup.score += 1

            results.append(
                {
                    "name": signup.teamname,
                    "win": ownerResult["won"],
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
