from __future__ import annotations

from datetime import datetime
from time import time
from typing import ClassVar, Optional

from sqlalchemy import (
    BigInteger,
    Column,
    DateTime,
    JSON,
    String,
    asc,
    cast,
    desc,
    event,
    text,
)
from sqlmodel import Field, SQLModel

from src.core.settings import settings

mmconfig = settings.mmconfig


class Match(SQLModel, table=True):
    __tablename__ = "Matches"

    id: Optional[int] = Field(default=None, primary_key=True)
    claimed: Optional[str] = None
    region: str
    gamemode: int
    time: int = Field(sa_column=Column(BigInteger, nullable=False, index=True))
    teams: list = Field(default_factory=list, sa_column=Column(JSON, nullable=False))
    spectators: list = Field(
        default_factory=list, sa_column=Column(JSON, nullable=False)
    )
    meta: dict = Field(default_factory=dict, sa_column=Column(JSON, nullable=False))
    # Team name to rounds won, as the game server last reported it. Stored in the column
    # the old live snapshot used, which is NOT NULL with no default.
    roundWins: dict = Field(
        default_factory=dict, sa_column=Column("livedata", JSON, nullable=False)
    )
    # Indexed because every game server's poll looks for the few unfinished matches.
    finished: bool = Field(default=False, index=True)
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

    # What a team name is cut to, so the game server can show it beside a score.
    NAME_LIMIT: ClassVar[int] = 15

    @staticmethod
    def _withDistinctNames(teams: list) -> list:
        """The same teams, with names guaranteed to differ from one another.

        The game server groups players by team name, so two teams with one name become
        one group that cannot win or lose. Names are cut before they are compared,
        because two long names that start alike cut to the same string.
        """
        seen: set[str] = set()
        distinct = []

        for index, team in enumerate(teams):
            wanted = (team.get("name") or f"team_{index + 1}")[: Match.NAME_LIMIT]

            # The first keeps the readable name. At most len(teams) suffixes are tried.
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
        from .user import User, UserBan

        teams = teams or []
        spectators = spectators or []

        self.time = int(time())
        self.gamemode = gamemode
        self.meta = {"userData": {}}
        self.teams = Match._withDistinctNames(teams)
        self.roundWins = {}
        self.finished = False
        self.region = region

        if createdBy is not None:
            self.meta["createdBy"] = str(createdBy)
        if eventId:
            self.meta["eventId"] = eventId

        all_users = list(dict.fromkeys(str(u) for team in teams for u in team["users"]))

        admin_bit = 2 ** settings.permissions.index("admin")
        with open() as db:
            # Every caller keeps barred players out before it gets here.
            assert not UserBan.barring(all_users, db), "A barred player was matched."
            admins = [
                str(steam64)
                for (steam64,) in db.query(User.steam64)
                .where(User.permissions.op("&")(admin_bit) != 0)
                .all()
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

        # A picker who never clicks would stall the match, so an open pick has a clock.
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

    # The four states a match can be in, derived from `finished`, `claimed` and
    # `meta["hostedAt"]` rather than stored. ClassVar keeps SQLModel from reading them
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
        """Whether every map, ballistics and movement pick is made, and every player
        has locked in or the selection window has run out."""
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
        """True once every player has made every pick the gamemode asks of them."""
        gm = mmconfig["gamemodes"][self.gamemode]
        needsKit = len(gm["kitsAllowed"]) > 1
        needsPowerup = len(gm["powerups"]) > 1
        return all(
            (not needsKit or user["kit"] is not None)
            and (not needsPowerup or user["powerup"] is not None)
            for user in self.meta["userData"].values()
        )

    def assignTo(self, serverCode: str) -> "Match | None":
        """Hand this match to a game server if nobody holds it yet, or None if another
        server got there first. The conditional update is the claim, so two servers that
        find the same pending match cannot both take it."""
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
        self._tellPlayersToJoin()
        return self

    def markHosting(self) -> None:
        """Record that the server has been handed the match, so asking again is told
        to carry on instead of being handed it twice."""
        from src.core.db import open

        self.meta = self.meta | {"hostedAt": time()}
        with open() as db:
            db.query(Match).where(Match.id == self.id).update({"meta": self.meta})

    def _tellPlayersToJoin(self) -> None:
        """DM everyone on the match a button that launches the game, the moment there
        is a server to join. Failures are logged, not raised: this runs inside a game
        server's poll, and a Discord outage must not stop a match."""
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
        """The oldest match in this region that nobody is hosting yet."""
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
        """A match this server holds but was never told to host, because the answer
        that described it was lost. Handed back instead of building a second match for
        the same players."""
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
        """Every steam64 that is on a match which has not finished."""
        from src.core.db import open

        with open() as db:
            return {
                str(steam64)
                for (steam64,) in db.query(MatchPlayer.steam64)
                .join(Match, Match.id == MatchPlayer.matchId)
                .where(Match.finished.is_(False))
                .all()
            }

    @staticmethod
    def currentFor(steam64) -> int | None:
        """The unfinished match this player is on, or None."""
        from src.core.db import open

        with open() as db:
            return (
                db.query(MatchPlayer.matchId)
                .join(Match, Match.id == MatchPlayer.matchId)
                .where(MatchPlayer.steam64 == int(steam64), Match.finished.is_(False))
                .order_by(Match.id)
                .limit(1)
                .scalar()
            )

    @staticmethod
    def releaseAbandoned(serverCode: str) -> None:
        """Record as abandoned every match a server that says it is free still holds.

        A server keeps naming a match until its result is acknowledged, so a free server
        has nothing left to report. Nobody's Elo moves. A match it was never told to host
        gets its selection window and the host grace first, since `undeliveredFor` is
        about to hand it back.
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
        """Cancel every custom or event match no server took within its selection
        window and the host grace. Left alone it would keep its players out of
        matchmaking forever."""
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
        """Auto-decide every map, ballistics or movement pick past its deadline, from
        the game servers' polls, so a match nobody is watching still becomes hostable.
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
    def recordRoundWins(matchId: int, wins: dict) -> None:
        from src.core.db import open

        with open() as db:
            db.query(Match).where(Match.id == matchId).update({Match.roundWins: wins})

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
        """A fresh pickTimeout while anything is undecided, so each pick gets a full
        window of its own. None once everything is decided."""
        if any(m[o] is None for o in ["ballistics", "map", "movement"]):
            return time() + mmconfig["pickTimeout"]
        return None

    def resolvedMetaIfExpired(self) -> dict | None:
        """The current pick decided as the gamemode's first option once its deadline
        passes, or None when there is nothing to decide."""
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
        """Every outstanding pick decided at once as the gamemode's first option, or
        None when there is nothing left. Used once everyone has locked in from in game,
        since nobody is at a browser to make the rest."""
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
        """The match as the match page reads it.

        Call this outside the session the match was loaded in: it reads Steam for every
        player, and holding a pooled connection across that call drains the pool.
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
            "startsAfter": self.selectionEndsAt(),
            "config": mmconfig["gamemodes"][self.gamemode],
            "joinTimeoutSeconds": mmconfig.get("joinTimeoutSeconds", 120),
        }

    def connectCode(self) -> str | None:
        """What a player types into Unturned to join this match: the holding server's
        code from the moment it holds the match, else None."""
        if self.status not in (Match.PICKING, Match.LIVE) or self.claimed in (
            None,
            "0",
        ):
            return None
        return self.claimed

    def getSteamUserData(self) -> dict:
        """Each player's Steam name and avatar, keyed by steam64."""
        from src.core import steam

        return {
            user: steam.User(user).toDict()
            for team in self.teams
            for user in team["users"]
        }


class MatchResult(SQLModel, table=True):
    """The durable per-match record: the settings it was played on and, once it ends,
    each player's final line or the reason it went unscored."""

    __tablename__ = "MatchResults"

    id: Optional[int] = Field(default=None, primary_key=True)
    matchId: Optional[int] = Field(default=None, foreign_key="Matches.id", index=True)
    region: str
    gamemode: int
    startTime: datetime = Field(
        sa_column=Column(DateTime(timezone=True), nullable=False)
    )
    endTime: datetime = Field(
        default_factory=datetime.utcnow,
        sa_column=Column(DateTime(timezone=True), nullable=False, index=True),
    )
    data: dict = Field(default_factory=dict, sa_column=Column(JSON, nullable=False))

    @staticmethod
    def _row(db, matchId: int) -> "MatchResult":
        """This match's row, created from the match's own settings if it has none yet."""
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
            },
        )
        db.add(result)
        db.flush()
        return result

    @staticmethod
    def hasResults(matchId: int) -> bool:
        """Whether this match has been scored. A server retries its result until it is
        acknowledged, so this is what stops a retry moving Elo twice."""
        from src.core.db import open

        with open() as db:
            result = db.query(MatchResult).where(MatchResult.matchId == matchId).first()
            return bool(result and result.data.get("results"))

    @staticmethod
    def recordFinalResults(matchId: int, results: dict) -> None:
        """`results` is matchmaking.matchResults()'s per-player summary, keyed by steam64."""
        from src.core.db import open

        with open() as db:
            result = MatchResult._row(db, matchId)
            db.query(MatchResult).where(MatchResult.id == result.id).update(
                {
                    "endTime": datetime.utcnow(),
                    "data": result.data | {"results": results},
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
        """Close out a match that ends without a score, so it is counted rather than
        lost. `reason` is `notPlayed`, `abandoned` or `noServer`."""
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
    def scoredFor(db, steam64, limit: int, newestFirst: bool = True) -> list:
        """Up to `limit` matches this player has a final line in.

        Their matches come off the MatchPlayers index. Rows older than the matchId
        column link to no match, so Postgres narrows those by text first. The results
        key is checked in Python because a JSON path operator is not portable between
        Postgres and SQLite.
        """
        assert limit > 0
        key = str(steam64)
        order = desc(MatchResult.endTime) if newestFirst else asc(MatchResult.endTime)
        linked = (
            db.query(MatchResult)
            .join(MatchPlayer, MatchPlayer.matchId == MatchResult.matchId)
            .where(MatchPlayer.steam64 == int(steam64))
            .order_by(order)
        )
        legacy = (
            db.query(MatchResult)
            .where(
                MatchResult.matchId.is_(None),
                cast(MatchResult.data, String).like(f'%"{key}"%'),
            )
            .order_by(order)
        )
        # Every legacy row is older than every linked one.
        sources = (linked, legacy) if newestFirst else (legacy, linked)

        scored = []
        for source in sources:
            for row in source.yield_per(100):
                if key in (row.data.get("results") or {}):
                    scored.append(row)
                if len(scored) == limit:
                    return scored
        return scored

    @staticmethod
    def latestFor(steam64, limit: int = 10) -> list["MatchResult"]:
        """This player's most recent scored matches, newest first."""
        from src.core.db import open

        with open() as db:
            rows = MatchResult.scoredFor(db, steam64, limit)
            for row in rows:
                db.expunge(row)
            return rows


class MatchPlayer(SQLModel, table=True):
    """Who is on which match, so a player's matches are one indexed lookup instead of a
    scan of every match's teams."""

    __tablename__ = "MatchPlayers"

    matchId: int = Field(foreign_key="Matches.id", primary_key=True)
    steam64: int = Field(sa_column=Column(BigInteger, primary_key=True, index=True))

    @staticmethod
    def backfill() -> None:
        """Fill the table from every match's teams, once, when it is first created."""
        from src.core.db import open

        with open() as db:
            if db.query(MatchPlayer).first() is not None:
                return
            for matchId, teams in db.query(Match.id, Match.teams).all():
                db.add_all(
                    MatchPlayer(matchId=matchId, steam64=steam64)
                    for steam64 in MatchPlayer.onTeams(teams)
                )

    @staticmethod
    def onTeams(teams: list) -> set[int]:
        return {int(u) for team in teams for u in team["users"]}


@event.listens_for(Match, "after_insert")
def _recordPlayers(mapper, connection, match: Match) -> None:
    """Every new match writes its players, whichever code path created it."""
    rows = [
        {"matchId": match.id, "steam64": steam64}
        for steam64 in MatchPlayer.onTeams(match.teams)
    ]
    if rows:
        connection.execute(MatchPlayer.__table__.insert(), rows)
