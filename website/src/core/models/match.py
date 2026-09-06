from __future__ import annotations

from datetime import datetime
from time import time
from typing import ClassVar, Optional

from sqlalchemy import BigInteger, Column, DateTime, JSON, desc, text
from sqlmodel import Field, SQLModel

from src.core.settings import settings

mmconfig = settings.mmconfig


class Match(SQLModel, table=True):
    __tablename__ = "Matches"

    id: Optional[int] = Field(default=None, primary_key=True)
    claimed: Optional[str] = None
    region: str
    gamemode: int
    time: int = Field(sa_column=Column(BigInteger, nullable=False))
    teams: list = Field(default_factory=list, sa_column=Column(JSON, nullable=False))
    spectators: list = Field(
        default_factory=list, sa_column=Column(JSON, nullable=False)
    )
    meta: dict = Field(default_factory=dict, sa_column=Column(JSON, nullable=False))
    livedata: dict = Field(default_factory=dict, sa_column=Column(JSON, nullable=False))
    finished: bool = False
    created_at: Optional[datetime] = Field(
        default=None,
        sa_column=Column(
            DateTime(timezone=True), server_default=text("CURRENT_TIMESTAMP")
        ),
    )
    updated_at: Optional[datetime] = Field(
        default=None,
        sa_column=Column(
            DateTime(timezone=True), server_default=text("CURRENT_TIMESTAMP")
        ),
    )

    # What a team name is cut to. Long enough to recognise, short enough for the
    # game server to show beside a score.
    NAME_LIMIT: ClassVar[int] = 15

    @staticmethod
    def _withDistinctNames(teams: list) -> list:
        """The same teams, with names guaranteed to differ from one another.

        The game server groups players by team name, so two teams called the same
        thing are not two teams — everyone lands in one group, nobody can damage
        anybody, and the match cannot be won or lost. That has happened: a match
        between two players the Steam lookup had failed for, who both came back as
        "Nameless", so both sides were built as "team_Nameless".

        Truncation is the other way in. Names are cut to fit, and two different long
        names that start alike cut to the same string, so the cut has to happen
        before the comparison rather than after it.
        """
        seen: set[str] = set()
        distinct = []

        for index, team in enumerate(teams):
            wanted = (team.get("name") or f"team_{index + 1}")[: Match.NAME_LIMIT]

            # Whoever got there first keeps the readable name; later duplicates take
            # a suffix. The counter only ever grows, so this always terminates.
            name, suffix = wanted, 2
            while name in seen:
                name = f"{wanted[: Match.NAME_LIMIT - 3]}_{suffix}"
                suffix += 1

            seen.add(name)
            distinct.append(team | {"name": name})

        return distinct

    def __init__(
        self,
        gamemode: int,
        teams: list | None = None,
        matchSettings: dict | None = None,
        spectators: list | None = None,
        region: str = "paris",
        eventId: int | None = None,
        createdBy: int | None = None,
        **kwargs,
    ):
        from src.core.db import open
        from src.core.webhook import Webhook
        from .user import User

        teams = teams or []
        spectators = spectators or []

        self.time = int(time())
        self.gamemode = gamemode
        self.meta = {"userData": {}}
        self.teams = Match._withDistinctNames(teams)
        self.livedata = {}
        self.finished = False
        self.region = region

        if createdBy is not None:
            self.meta["createdBy"] = str(createdBy)
        if eventId:
            self.meta["eventId"] = eventId

        all_users = [str(user) for team in teams for user in team["users"]]

        admin_bit = 2 ** settings.permissions.index("admin")
        with open() as db:
            admins = [
                str(u.steam64)
                for u in db.query(User).all()
                if u.permissions & admin_bit
            ]
            self.spectators = [
                x
                for x in dict.fromkeys([*admins, *spectators])
                if str(x) not in all_users
            ]

        gm = mmconfig["gamemodes"][self.gamemode]
        if matchSettings is None:
            for item in ["ballistics", "map", "movement"]:
                self.meta[item] = gm[item][0] if len(gm[item]) == 1 else None
        else:
            self.meta = self.meta | matchSettings

        # Nothing stops the current picker from just never clicking — without a deadline
        # that stalls the match forever. Only start the clock when there's actually a
        # pick left to make (matchSettings can arrive fully pre-decided, e.g. a custom
        # match or these tests' fixtures).
        if any(self.meta[o] is None for o in ["ballistics", "map", "movement"]):
            self.meta["pickDeadline"] = time() + mmconfig["pickTimeout"]

        creator = "matchmaking"
        if createdBy:
            creator = (
                f"[{User.getSteamData(createdBy).name}]"
                f"({settings.profile_url(createdBy)}) `{createdBy}`"
            )

        Webhook("Match info").info(
            "Match started",
            description=(
                f"Match started in `{region}` by {creator} with:\n"
                + "\n".join(
                    f"- [{User.getSteamData(u).name}]({settings.profile_url(u)}) `{u}`"
                    for u in all_users
                )
            ),
        )

        with open() as db:
            for user in db.query(User).where(User.steam64.in_(all_users)).all():
                user.notify(
                    "Go to match",
                    "A match started with you in it.",
                    url=f"{settings.base_url}/match",
                )

        for u in all_users:
            self.meta["userData"][str(u)] = {"kit": None, "powerup": None}

        for k, v in kwargs.items():
            setattr(self, k, v)

    # The four states a match can be in. They are derived from `finished`, `claimed`
    # and `meta["hostedAt"]` rather than stored — a fourth place to write the same fact
    # is a fourth place for it to disagree. ClassVar keeps SQLModel from reading them
    # as columns.
    PENDING: ClassVar[str] = "pending"  # exists, no game server holds it
    PICKING: ClassVar[str] = "picking"  # a server holds it while its players choose
    LIVE: ClassVar[str] = "live"  # handed over; the server is running it
    FINISHED: ClassVar[str] = "finished"  # scored, abandoned or cancelled

    STATUS_LABELS: ClassVar[dict] = {
        PENDING: "Waiting for a server",
        PICKING: "Players choosing",
        LIVE: "In progress",
        FINISHED: "Over",
    }

    @property
    def status(self) -> str:
        if self.finished:
            return Match.FINISHED
        if self.meta.get("hostedAt"):
            return Match.LIVE
        return Match.PICKING if self.claimed else Match.PENDING

    @property
    def statusLabel(self) -> str:
        """`status` in words, for the spectate and admin lists."""
        return Match.STATUS_LABELS[self.status]

    def selectionEndsAt(self) -> float:
        return self.time + mmconfig["selectKitsTime"]

    def readyToHost(self) -> bool:
        """Whether everything the game server needs to run this match has been decided.

        Deliberately not "is a server available" — that is the caller's business. This
        answers only "is there anything left to wait for", which is every map/ballistics/
        movement pick made, and either every player locked in or the selection window
        run out.
        """
        return (
            not self.finished
            and self.meta["ballistics"] is not None
            and self.meta["map"] is not None
            and self.meta["movement"] is not None
            and (time() >= self.selectionEndsAt() or self.allKitsLocked())
        )

    def waitingOn(self) -> list[str]:
        """The players who still have a pick to make, for the server to name while it
        waits. Empty once selection is done, however it ended."""
        gm = mmconfig["gamemodes"][self.gamemode]
        needsKit = len(gm["kitsAllowed"]) > 1
        needsPowerup = len(gm["powerups"]) > 1
        return [
            steam64
            for steam64, user in self.meta["userData"].items()
            if (needsKit and user["kit"] is None)
            or (needsPowerup and user["powerup"] is None)
        ]

    def allKitsLocked(self) -> bool:
        """True once every player has made every pick the gamemode asks for them to
        make, so the match can start the moment picking is actually done rather than
        always waiting out the full selection timer."""
        gm = mmconfig["gamemodes"][self.gamemode]
        needsKit = len(gm["kitsAllowed"]) > 1
        needsPowerup = len(gm["powerups"]) > 1
        return all(
            (not needsKit or user["kit"] is not None)
            and (not needsPowerup or user["powerup"] is not None)
            for user in self.meta["userData"].values()
        )

    def assignTo(self, serverCode: str) -> "Match | None":
        """Hand this match to a game server, if it is still going spare. From here on
        that server is the only one that can host it, and the only one whose result
        report will be believed.

        The update *is* the claim, which is why `claimed IS NULL` is in the WHERE and
        the row count is checked: two free servers in one region both find the same
        pending match, and this is what tells the second one it lost. Written
        unconditionally, both wrote their own code over the other's and both went on to
        host the same match — the pending-match twin of the queue race `UserHB.claim`
        already closes. None means somebody else got there first.
        """
        from src.core.db import open

        with open() as db:
            taken = (
                db.query(Match)
                .where(Match.id == self.id, Match.claimed.is_(None))
                .update({"claimed": serverCode})
            )
        if not taken:
            return None

        self.claimed = serverCode
        return self

    def markHosting(self) -> None:
        """Record that the server has now been handed the match and is running it.

        This is what makes the session poll idempotent: a server that asks again gets
        told to carry on rather than being handed the same match a second time, which
        would restart the round and kick everyone already playing.
        """
        from src.core.db import open

        self.meta = self.meta | {"hostedAt": time()}
        with open() as db:
            db.query(Match).where(Match.id == self.id).update({"meta": self.meta})

        self._tellPlayersToJoin()

    def _tellPlayersToJoin(self) -> None:
        """DM everyone on the match a button that launches the game.

        More than a quarter of all matches ever made were never played: matchmaking
        worked, a server was assigned, and nobody turned up. The existing DM goes out
        when the match is *created*, which is minutes earlier and before there is
        anywhere to go, so by the time there is, the only thing that told them has
        scrolled away.

        This one fires at the moment there is something to join and carries the button
        that joins it, so the whole path from "your match is ready" to being in the
        server is one press.

        Failures are swallowed on purpose. This runs inside the session poll that hands
        a server its match, and a Discord outage must not stop a match being played.
        """
        import logging

        from src.core.db import open
        from src.core.settings import settings
        from .user import User

        code = self.connectCode()
        if not code:
            return

        joinButton = [
            {
                "type": 1,
                "components": [
                    {
                        "type": 2,
                        "style": 5,
                        "label": "Join the match",
                        "url": f"{settings.base_url}/join/{code}",
                    }
                ],
            }
        ]

        try:
            with open() as db:
                players = [int(u) for team in self.teams for u in team["users"]]
                for user in db.query(User).where(User.steam64.in_(players)).all():
                    user.notify(
                        "Your match is ready",
                        "Press the button to join. It opens Unturned for you.",
                        url=f"{settings.base_url}/match",
                        components=joinButton,
                    )
        except Exception as error:
            logging.warning("Could not send match-ready DMs for %s: %s", self.id, error)

    @staticmethod
    def pendingFor(region: str) -> "Match | None":
        """The oldest match in this region that nobody is hosting yet.

        Oldest first, not newest: a match that has been waiting is the one closest to
        having its players give up on it, so it gets the next free server.
        """
        from src.core.db import open

        with open() as db:
            return (
                db.query(Match)
                .where(
                    Match.claimed.is_(None),
                    Match.finished.is_(False),
                    Match.region == region,
                )
                .order_by(Match.time)
                .first()
            )

    @staticmethod
    def undeliveredFor(serverCode: str) -> "Match | None":
        """A match this server holds but has never been told to host.

        Matchmaking runs inside the poll that asks for work, so the match exists before
        the answer describing it does. Lose that answer — the poll timed out on a slow
        Steam or Discord call, the server restarted — and the server comes back asking
        as a free server while already holding a match it has never heard of. Left
        alone, the next poll abandoned that match and built a second one for the same
        players, and the poll after that a third: one pairing, a new match and a new
        "Match started" post every few seconds.

        Handing the same match back instead is what makes the poll idempotent for a
        match that has been created but not yet delivered, the way `markHosting` already
        makes it idempotent for one that has.

        Only ever a match that was never hosted. One this server was actually running
        and has now gone quiet about is abandoned rather than handed back, which is the
        line `releaseAbandoned` draws from the other side.
        """
        if not serverCode or serverCode == "0":
            return None

        from src.core.db import open

        with open() as db:
            held = (
                db.query(Match)
                .where(Match.finished.is_(False), Match.claimed == serverCode)
                .order_by(Match.time)
                .all()
            )

        return next((match for match in held if not match.meta.get("hostedAt")), None)

    @staticmethod
    def playersInAMatch() -> set[str]:
        """Every steam64 that is on a match which has not finished.

        `User.getCurrentMatchId` answers this one player at a time, which is fine for a
        page about that player and wrong for matchmaking: scanning the queue asked it
        per heartbeat, and each of those reread every unfinished match. One read of the
        same table answers it for the whole queue at once, and is what lets the scan
        drop players who are already in a match before it starts pairing anyone up.
        """
        from src.core.db import open

        with open() as db:
            return {
                str(user)
                for match in db.query(Match).where(Match.finished.is_(False)).all()
                for team in match.teams
                for user in team["users"]
            }

    @staticmethod
    def releaseAbandoned(serverCode: str) -> None:
        """A server reporting itself free has no match — so if it still holds one that
        never finished, that match's only "I'm done" signal never arrived. Nothing else
        revisits a match a server is holding, so left alone its players stay stuck in it
        forever.

        A server with a result to report keeps naming the match in its polls until the
        report is acknowledged, so reaching here means the server genuinely has nothing
        left to say about it. The match is recorded as abandoned rather than merely
        closed: nobody's Elo moves, because no winner was ever reported, but the match
        stops being invisible.

        A match this server was never told to host is the one exception, and it is
        given its selection window plus the host grace before being written off. That
        is a match the site created and then failed to deliver, not one a server
        dropped, and `undeliveredFor` is about to hand it straight back.
        """
        if not serverCode or serverCode == "0":
            return

        from src.core.db import open
        from src.core.webhook import Webhook

        abandoned = []
        with open() as db:
            for match in (
                db.query(Match)
                .where(Match.finished.is_(False), Match.claimed == serverCode)
                .all()
            ):
                # Never handed over, so there is nothing here to have been abandoned:
                # the answer that would have told this server about it was lost.
                # `undeliveredFor` gives it back on this same poll instead, and only
                # once that window has run out is it written off like any other.
                deadline = match.selectionEndsAt() + settings.host_grace
                if not match.meta.get("hostedAt") and time() < deadline:
                    continue

                match.finished = True
                abandoned.append(match.id)

        for matchId in abandoned:
            MatchResult.recordUnscored(matchId, "abandoned")
            Webhook(f"Match.{matchId}").error(
                f"Server `{serverCode}` reported itself free without ever reporting "
                "this match's results — recorded as abandoned so its players aren't "
                "stuck in it. No Elo was applied, because no winner was ever reported."
            )

    @staticmethod
    def cancelStranded() -> None:
        """Cancel every match that has run out of ways to ever be played.

        Matchmaking only makes a match for a server that has just said it is free, so
        the only matches that reach here are ones created without one — a custom match,
        an event pairing — that no server picked up. Once selection has closed on such a
        match, `hostGrace` more seconds is all it gets: game servers poll every few
        seconds, so a match that has gone that long without one has none coming.

        Leaving them alone is how players used to end up locked out of matchmaking by a
        match that was never going to start; `getCurrentMatchId` counts any unfinished
        match they are in.
        """
        from src.core.db import open
        from src.core.webhook import Webhook

        stranded = []
        with open() as db:
            for match in (
                db.query(Match)
                .where(Match.finished.is_(False), Match.claimed.is_(None))
                .all()
            ):
                if time() < match.selectionEndsAt() + settings.host_grace:
                    continue
                match.finished = True
                stranded.append(match.id)

        for matchId in stranded:
            MatchResult.recordUnscored(matchId, "noServer")
            Webhook(f"Match.{matchId}").error(
                "Cancelled — selection finished and no game server ever became free to "
                "host it."
            )

    @staticmethod
    def resolveExpiredPicks() -> None:
        """Auto-decide every stalled map/ballistics/movement pick that is past its
        deadline, across every unfinished match.

        Enforced here, from the game servers' own polling, rather than only when a
        player's match page happens to ask: a match whose players all closed the tab
        used to sit on an undecided pick forever, never becoming hostable, never being
        cancelled, and never letting its players queue for anything else.
        """
        from src.core.db import open

        with open() as db:
            for match in db.query(Match).where(Match.finished.is_(False)).all():
                resolved = match.resolvedMetaIfExpired()
                if resolved is not None:
                    db.query(Match).where(Match.id == match.id).update(
                        {"meta": resolved}
                    )

    @staticmethod
    def setLiveData(match_id, livedata: dict) -> None:
        from src.core.db import open

        if isinstance(match_id, Match):
            match_id = match_id.id
        with open() as db:
            db.query(Match).where(Match.id == match_id).update({"livedata": livedata})

    def getReturnedMeta(
        self,
        steam64: int,
        pick: bool | str | None = None,
        kit: str | None = None,
        powerup: str | None = None,
    ):
        if not self.teams:
            raise ValueError("Match.teams list cannot be empty.")

        m = self.meta.copy()
        steam64 = str(steam64)

        if kit is not None:
            m["userData"][steam64]["kit"] = kit
        if powerup is not None:
            m["userData"][steam64]["powerup"] = powerup
        if pick is not None:
            if isinstance(pick, str):
                pick = bool(int(pick))

            current = m.get("currentPick", self.teams[0]["users"][0])
            if str(current) != steam64:
                return m

            Match._advancePick(m, self.teams)

            not_picked = [o for o in ["ballistics", "map", "movement"] if m[o] is None]
            if not_picked:
                m[not_picked[0]] = pick
                m["pickDeadline"] = Match._nextPickDeadline(m)

        return m

    @staticmethod
    def _advancePick(m: dict, teams: list) -> None:
        """Move `currentPick` to the next team's first player, in place."""
        current = m.get("currentPick", teams[0]["users"][0])
        next_team = 0
        for i, team in enumerate(teams):
            if current in team["users"]:
                next_team = (i + 1) % len(teams)
                break
        m["currentPick"] = teams[next_team]["users"][0]

    @staticmethod
    def _nextPickDeadline(m: dict) -> float | None:
        """A fresh pickTimeout if anything's still undecided, else None — each of
        ballistics/map/movement gets its own full window rather than sharing the match's
        original deadline, so a slow first pick doesn't starve the next one."""
        if any(m[o] is None for o in ["ballistics", "map", "movement"]):
            return time() + mmconfig["pickTimeout"]
        return None

    def resolvedMetaIfExpired(self) -> dict | None:
        """Auto-decide the current ballistics/map/movement pick once its deadline passes,
        so a player can't stall the match indefinitely by never clicking. Falls back to
        the gamemode's first configured option — the same "index 0" convention
        kits/powerups already use when nobody picks. Returns None when there's nothing to
        resolve, so callers only write to the DB when something actually changed."""
        deadline = self.meta.get("pickDeadline")
        if deadline is None or time() < deadline:
            return None

        not_picked = [
            o for o in ["ballistics", "map", "movement"] if self.meta[o] is None
        ]
        if not not_picked:
            return None

        gm = mmconfig["gamemodes"][self.gamemode]
        m = self.meta.copy()
        field = not_picked[0]
        m[field] = gm[field][0]

        Match._advancePick(m, self.teams)
        m["pickDeadline"] = Match._nextPickDeadline(m)
        return m

    def collapsedMetaIfNobodyIsPicking(self) -> dict | None:
        """Every outstanding ballistics/map/movement pick decided at once, or None when
        there is nothing left to decide.

        The pick phase is only worth waiting on while somebody is at a browser to use
        it. Once every player has locked their kit and powerup from in-game, nobody is,
        and what is left of the phase is three `pickTimeout` windows of dead time
        between a match being ready and anyone being teleported into it. Falls back to
        the same "index 0" option `resolvedMetaIfExpired` uses, so a collapsed pick and
        an expired one produce the same match.
        """
        notPicked = [
            o for o in ["ballistics", "map", "movement"] if self.meta[o] is None
        ]
        if not notPicked:
            return None

        gm = mmconfig["gamemodes"][self.gamemode]
        m = self.meta.copy()
        for field in notPicked:
            m[field] = gm[field][0]
        m["pickDeadline"] = None
        return m

    def localJson(self) -> dict:
        """The match as the site's own pages read it — the match screen, the stream
        overlay, and the spectate and admin lists.

        `status` is what tells the match screen which of its three faces to show, and
        `serverCode` is null until there is genuinely something to connect to. They are
        separate on purpose: a match has a server from the moment it is made, so "a
        server exists" stopped being the same question as "the match has started".

        Call this *outside* the session the match was loaded in. It reads Steam for every
        player, and holding a pooled connection across a call to a third party is what
        drained the pool: the three routes that build this are the polled ones, so a slow
        Steam reply took every connection with it and load_user started timing out on
        every route in the site.
        """
        return {
            "id": self.id,
            "status": self.status,
            "serverCode": self.connectCode(),
            "teams": self.teams,
            "currentPick": self.meta.get("currentPick", self.teams[0]["users"][0]),
            "params": self.meta,
            "claimed": self.claimed,
            "users": self.getSteamUserData(),
            "liveData": self.livedata,
            "startsAfter": self.selectionEndsAt(),
            "config": mmconfig["gamemodes"][self.gamemode],
        }

    def connectCode(self) -> str | None:
        """What a player types into Unturned to join this match, once there is anything
        to join. Null while the match is still being set up, and null for the "0"
        sentinel a server too old to report its own code holds it with."""
        if self.status != Match.LIVE or self.claimed in (None, "0"):
            return None
        return self.claimed

    def getSteamUserData(self) -> dict:
        """Everything the site knows about each player in this match that the game
        server does not: the Steam persona to call them by before they have connected,
        and the gun they will spawn holding. Both are what the overlay's intermission
        screen draws while the match is still filling up."""
        from src.core import steam
        from .kit import Kit

        return {
            user: steam.User(user).toDict() | {"gun": Kit.gunFor(user, self)}
            for team in self.teams
            for user in team["users"]
        }

    @staticmethod
    def findLastCreatedBy(steam64) -> int | None:
        """A premium perk: a stable personal overlay link that always resolves to
        whichever match this steam64 is most recently playing in, matchmade or custom
        — not literally who administratively created it (the name is kept because it's
        the public ?createdBy= URL param streamers already have saved). Gated on
        premium so it stays a paid feature rather than working for anyone's steam64."""
        from src.core.db import open
        from .user import User

        if not User.getPermission(steam64, "premium"):
            return None

        with open() as db:
            for match in db.query(Match).order_by(desc(Match.id)).all():
                users = [str(u) for team in match.teams for u in team["users"]]
                if str(steam64) in users:
                    return match.id
        return None


class MatchResult(SQLModel, table=True):
    """The durable per-match record: the map/settings, the kill-by-kill log, and (once the
    match ends) each player's final line. `data` is JSON on purpose — kills and results are
    two things this row grows to hold, and neither is a fixed shape the way the frozen
    /unt/* payloads are, so new fields go straight in rather than needing a migration.
    """

    __tablename__ = "MatchResults"

    id: Optional[int] = Field(default=None, primary_key=True)
    matchId: Optional[int] = Field(default=None, foreign_key="Matches.id")
    region: str
    gamemode: int
    startTime: datetime = Field(
        sa_column=Column(DateTime(timezone=True), nullable=False)
    )
    endTime: datetime = Field(
        default_factory=datetime.utcnow,
        sa_column=Column(DateTime(timezone=True), nullable=False),
    )
    data: dict = Field(default_factory=dict, sa_column=Column(JSON, nullable=False))

    @staticmethod
    def _row(db, matchId: int) -> "MatchResult":
        """This match's row, created from the match's own settings if it has none yet.
        Both the kill log and the final line arrive independently and either can be first,
        so neither can assume the other already made the row."""
        result = db.query(MatchResult).where(MatchResult.matchId == matchId).first()
        if result is not None:
            return result

        match = db.query(Match).where(Match.id == matchId).first()
        result = MatchResult(
            matchId=matchId,
            region=match.region if match else "unknown",
            gamemode=match.gamemode if match else 0,
            startTime=datetime.utcnow(),
            data={
                "map": match.meta.get("map") if match else None,
                "ballistics": match.meta.get("ballistics") if match else None,
                "movement": match.meta.get("movement") if match else None,
                "kills": [],
            },
        )
        db.add(result)
        db.flush()
        return result

    @staticmethod
    def appendKill(matchId: int, kill: dict) -> None:
        """One kill, as it happens. Creates the row on the first kill of a match and
        appends to it on every one after."""
        from src.core.db import open

        with open() as db:
            result = MatchResult._row(db, matchId)
            db.query(MatchResult).where(MatchResult.id == result.id).update(
                {"data": result.data | {"kills": [*result.data.get("kills", []), kill]}}
            )

    @staticmethod
    def hasResults(matchId: int) -> bool:
        """Whether this match's final per-player line has already been recorded.

        The guard against applying a match's Elo twice. A game server retries its result
        report until the site acknowledges it, so a reply that gets lost on the way back
        means the same result arrives again — that retry must not move anyone's Elo a
        second time.
        """
        from src.core.db import open

        with open() as db:
            result = db.query(MatchResult).where(MatchResult.matchId == matchId).first()
            return bool(result and result.data.get("results"))

    @staticmethod
    def recordFinalResults(
        matchId: int, results: dict, score: list | None = None
    ) -> None:
        """`results` is matchmaking.matchResults()'s per-player summary, keyed by steam64.
        A match with no kills (a walkover) still gets a row here, since this is also the
        only place a player's final per-match line is kept.

        `score` is the round tally, copied off the last live snapshot as it is recorded
        because scoring a match wipes that snapshot. Without it a finished match knows
        who won and not by how much, and 3-0 and 3-2 read identically forever after.
        """
        from src.core.db import open

        with open() as db:
            result = MatchResult._row(db, matchId)
            db.query(MatchResult).where(MatchResult.id == result.id).update(
                {
                    "endTime": datetime.utcnow(),
                    "data": result.data | {"results": results, "score": score or []},
                }
            )

    @staticmethod
    def findFor(matchId: int) -> "MatchResult | None":
        """This match's durable record, or None if it never got one. Read-only, unlike
        `_row`, which exists to create one."""
        from src.core.db import open

        with open() as db:
            row = db.query(MatchResult).where(MatchResult.matchId == matchId).first()
            if row is not None:
                db.expunge(row)
            return row

    @staticmethod
    def recordUnscored(matchId: int, reason: str) -> None:
        """Close out a match that ends without a score: nobody turned up for it, or its
        server never reported one.

        No results block is invented. There is no trustworthy winner, so nobody's Elo
        moves — but the match is recorded rather than simply vanishing, so an unscored
        match is something an operator can see and count instead of a gap.

        `reason` is `notPlayed` or `abandoned`.
        """
        from src.core.db import open

        with open() as db:
            result = MatchResult._row(db, matchId)
            if result.data.get("results"):
                return
            db.query(MatchResult).where(MatchResult.id == result.id).update(
                {
                    "endTime": datetime.utcnow(),
                    "data": result.data | {"unscored": reason},
                }
            )

    @staticmethod
    def latestFor(steam64, limit: int = 10) -> list["MatchResult"]:
        """Every match this player has a final line in, most recent first.

        Filtered in Python rather than with a JSON path operator in SQL. The `->`/`->>`
        pair this used to use is not portable enough to rely on: it reads the row on
        Postgres and on a recent SQLite, and silently matches nothing on an older one,
        which is exactly what it did on CI — every profile page rendered as though the
        player had never played a ranked match. A profile only ever wants the last
        handful of rows, so the scan costs less than a result that depends on which
        SQLite the host happens to ship.
        """
        from src.core.db import open

        key = str(steam64)
        with open() as db:
            rows = db.query(MatchResult).order_by(desc(MatchResult.endTime)).all()

        return [row for row in rows if key in (row.data.get("results") or {})][:limit]
