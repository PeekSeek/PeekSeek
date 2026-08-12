from __future__ import annotations

from datetime import datetime
from time import time
from typing import Optional

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
        self.teams = teams
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

    def canBeClaimed(self) -> bool:
        return (
            self.finished is not True
            and self.claimed is None
            and self.meta["ballistics"] is not None
            and self.meta["map"] is not None
            and self.meta["movement"] is not None
            and (
                time() >= (self.time + mmconfig["selectKitsTime"])
                or self.allKitsLocked()
            )
        )

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

    def claim(self, code: str = "0") -> "Match":
        from src.core.db import open

        with open() as db:
            db.query(Match).where(Match.id == self.id).update({"claimed": code})
        return self

    @staticmethod
    def claimMatchIfPossible(region: str, code: str = "0") -> dict | None:
        from src.core.db import open

        with open() as db:
            for match in (
                db.query(Match)
                .where(Match.claimed.is_(None), Match.region == region)
                .order_by(desc(Match.time))
                .all()
            ):
                if match.canBeClaimed():
                    j = match.json()
                    match.claim(code)
                    return j
        return None

    @staticmethod
    def releaseAbandoned(code: str) -> None:
        """A server polling `matchid=0` has no live match right now — if it still holds
        the claim on an unfinished match, that match's one and only "I'm done" signal
        (the plugin's fire-and-forget POST to /unt/results) never arrived, and nothing
        else ever revisits a *claimed* match (cancelStaleUnclaimed only sweeps unclaimed
        ones). Left alone, that match's players stay stuck in it forever. Skipped for the
        "0" sentinel code, since that's shared by every server too old to send a real one
        — finishing on it could close a match a different old server is still playing.
        """
        if code == "0":
            return

        from src.core.db import open
        from src.core.webhook import Webhook

        with open() as db:
            for match in (
                db.query(Match)
                .where(Match.finished.is_(False), Match.claimed == code)
                .all()
            ):
                match.finished = True
                Webhook(f"Match.{match.id}").error(
                    f"Server `{code}` polled idle without ever reporting this match's "
                    "results — force-closed so its players aren't stuck in it."
                )

    @staticmethod
    def cancelStaleUnclaimed() -> None:
        """Finish matches no server claimed within the timeout, and log it."""
        from src.core.db import open
        from src.core.webhook import Webhook

        with open() as db:
            for match in (
                db.query(Match)
                .where(Match.finished.is_(False), Match.claimed.is_(None))
                .all()
            ):
                if time() - match.time <= settings.claim_timeout:
                    continue
                match.finished = True
                Webhook(f"Match.{match.id}").error("Cancelled due to timeout.")

    @staticmethod
    def setLiveData(match_id, livedata: dict) -> None:
        from src.core.db import open

        if isinstance(match_id, Match):
            match_id = match_id.id
        with open() as db:
            db.query(Match).where(Match.id == match_id).update({"livedata": livedata})

    def json(self) -> dict:
        from .kit import Kit

        return {
            "head": "start",
            "matchid": self.id,
            "meta": {"rounds": 3},
            "settings": {
                "ballistics": self.meta["ballistics"],
                "map": self.meta["map"],
                "movement": self.meta["movement"],
            },
            "teams": self.teams,
            "users": [
                {"kit": Kit.getItems(steam64, self), "userId": str(steam64)}
                for steam64 in self.meta["userData"]
            ],
            "spectators": self.spectators,
        }

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

            next_team = 0
            for i, team in enumerate(self.teams):
                if current in team["users"]:
                    next_team = (i + 1) % len(self.teams)
                    break

            m["currentPick"] = self.teams[next_team]["users"][0]

            not_picked = [o for o in ["ballistics", "map", "movement"] if m[o] is None]
            if not_picked:
                m[not_picked[0]] = pick

        return m

    def localJson(self) -> dict:
        return {
            "id": self.id,
            "teams": self.teams,
            "currentPick": self.meta.get("currentPick", self.teams[0]["users"][0]),
            "params": self.meta,
            "claimed": self.claimed,
            "users": self.getSteamUserData(),
            "liveData": self.livedata,
            "startsAfter": self.time + mmconfig["selectKitsTime"],
            "config": mmconfig["gamemodes"][self.gamemode],
        }

    def getSteamUserData(self) -> dict:
        from src.core import steam

        return {
            user: steam.User(user).toDict()
            for team in self.teams
            for user in team["users"]
        }

    @staticmethod
    def findLastCreatedBy(steam64) -> int | None:
        from src.core.db import open

        with open() as db:
            found = (
                db.query(Match)
                .filter(Match.meta.op("->>")("createdBy") == str(steam64))
                .order_by(desc(Match.id))
                .first()
            )
            return found.id if found else None


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
    def appendKill(matchId: int, kill: dict) -> None:
        """Called from /unt/kills, once per kill. Creates the row on the first kill of a
        match and appends to it on every one after."""
        from src.core.db import open

        with open() as db:
            result = db.query(MatchResult).where(MatchResult.matchId == matchId).first()
            if result is None:
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

            db.query(MatchResult).where(MatchResult.id == result.id).update(
                {"data": result.data | {"kills": [*result.data.get("kills", []), kill]}}
            )

    @staticmethod
    def recordFinalResults(matchId: int, results: dict) -> None:
        """Called from /unt/results. `results` is matchmaking.matchResults()'s per-player
        summary, keyed by steam64. A match with no kills (a walkover) still gets a row here,
        since this is also the only place a player's final per-match line is kept."""
        from src.core.db import open

        with open() as db:
            result = db.query(MatchResult).where(MatchResult.matchId == matchId).first()
            if result is None:
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

            db.query(MatchResult).where(MatchResult.id == result.id).update(
                {
                    "endTime": datetime.utcnow(),
                    "data": result.data | {"results": results},
                }
            )

    @staticmethod
    def latestFor(steam64, limit: int = 10) -> list["MatchResult"]:
        """Every match this player has a final line in, most recent first. Uses the same
        ->/->> JSON path operators as Match.findLastCreatedBy — supported by both Postgres
        and the SQLite the tests run against, unlike Postgres-only JSONB operators."""
        from src.core.db import open

        with open() as db:
            return (
                db.query(MatchResult)
                .filter(
                    MatchResult.data.op("->")("results")
                    .op("->>")(str(steam64))
                    .isnot(None)
                )
                .order_by(desc(MatchResult.endTime))
                .limit(limit)
                .all()
            )
