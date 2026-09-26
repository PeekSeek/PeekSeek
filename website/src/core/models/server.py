from __future__ import annotations

from datetime import datetime, timedelta, timezone
from time import time
from typing import Optional

from sqlalchemy import (
    BigInteger,
    Column,
    DateTime,
    ForeignKey,
    String,
    desc,
    func,
    text,
)
from sqlmodel import Field, SQLModel

from src.core.settings import settings


class AuthorizedServer(SQLModel, table=True):
    __tablename__ = "AuthorizedServers"

    address: str = Field(primary_key=True)
    region: Optional[str] = None

    @staticmethod
    def bootstrap(servers: dict[str, str]) -> None:
        """Allow-list game-server IPs, so a fresh environment can accept /unt calls.

        There is no page or endpoint that adds one, and an unlisted server gets a
        401 from every /unt route. Driven by BOOTSTRAP_SERVERS so only the
        environments that set it are affected.
        """
        from src.core.db import open

        with open() as db:
            known = {server.address for server in db.query(AuthorizedServer).all()}
            for address, region in servers.items():
                if address not in known:
                    db.add(AuthorizedServer(address=address, region=region))


class LobbyPopulation(SQLModel, table=True):
    """How many players one lobby server has standing in its arena right now. One row
    per server, overwritten; the history is `ArenaReading`'s."""

    __tablename__ = "LobbyPopulations"

    address: str = Field(
        sa_column=Column(
            String, ForeignKey("AuthorizedServers.address"), primary_key=True
        )
    )
    players: int = Field(nullable=False)
    at: int = Field(sa_column=Column(BigInteger, nullable=False))

    @staticmethod
    def report(address: str, players: int) -> None:
        from src.core.db import open

        with open() as db:
            row = (
                db.query(LobbyPopulation)
                .where(LobbyPopulation.address == address)
                .first()
            )
            if row is None:
                db.add(
                    LobbyPopulation(address=address, players=players, at=int(time()))
                )
                return
            row.players = players
            row.at = int(time())

    @staticmethod
    def countFresh(maxAgeSeconds: int = 30) -> int:
        """Everyone in an arena right now, across every lobby that is still reporting.

        A server that has stopped reporting is left out rather than trusted: a lobby
        that crashed with eleven people in it would otherwise keep advertising them
        for as long as its row survives.
        """
        from src.core.db import open

        cutoff = int(time()) - maxAgeSeconds
        with open() as db:
            return sum(
                row.players
                for row in db.query(LobbyPopulation).all()
                if row.at >= cutoff
            )

    @staticmethod
    def activity(maxAgeSeconds: int = 30) -> dict:
        """Per region, what a player deciding where to queue would want to hear: how
        many are in that region's lobbies right now, and how long ago its last match
        ended, in minutes. Only lobbies still reporting count, as in `countFresh`.

        This is for the play page, which shows a number only when it invites: a lobby
        with one person in it and a region whose last match was three hours ago are
        both left unsaid, because somebody about to queue is somebody else's match,
        and a bad number scares them off before they are anybody's.
        """
        from src.core.db import open
        from .match import MatchResult

        cutoff = int(time()) - maxAgeSeconds
        activity: dict = {}
        with open() as db:
            regions = {
                server.address: server.region
                for server in db.query(AuthorizedServer).all()
            }
            for row in db.query(LobbyPopulation).all():
                if row.at < cutoff or regions.get(row.address) is None:
                    continue
                region = activity.setdefault(
                    regions[row.address], {"players": 0, "lastMatchMinutes": None}
                )
                region["players"] += row.players

            # The page only mentions a match from the last half hour.
            recent = datetime.now(timezone.utc) - timedelta(minutes=31)
            lastEnded = (
                db.query(MatchResult.region, func.max(MatchResult.endTime))
                .where(MatchResult.endTime >= recent)
                .group_by(MatchResult.region)
                .all()
            )

        for regionName, ended in lastEnded:
            region = activity.setdefault(
                regionName, {"players": 0, "lastMatchMinutes": None}
            )
            # Written naive UTC; SQLite reads it back naive, which .timestamp() would
            # take for local time.
            if ended.tzinfo is None:
                ended = ended.replace(tzinfo=timezone.utc)
            since = (datetime.now(timezone.utc) - ended).total_seconds()
            region["lastMatchMinutes"] = max(0, int(since // 60))
        return activity

    @staticmethod
    def playingNow() -> int:
        """Everyone playing anywhere: lobbies report who stands in them, and every
        roster the site handed out is on a match. A lobby counts only players not on a
        match, so nobody is counted twice."""
        from .match import Match

        return LobbyPopulation.countFresh() + len(Match.playersInAMatch())


class ArenaReading(SQLModel, table=True):
    """What one lobby's arena looked like at one moment: how many stood in it, how many
    wanted a match, and how many the lobby held back. Written only when a reading
    changes, so the gap between two rows is how long a state lasted."""

    __tablename__ = "ArenaReadings"

    id: Optional[int] = Field(default=None, primary_key=True)
    address: str = Field(
        sa_column=Column(
            String, ForeignKey("AuthorizedServers.address"), nullable=False
        )
    )
    at: int = Field(sa_column=Column(BigInteger, nullable=False))
    players: int = Field(nullable=False)

    # How many of those players want a match, and how many of those the lobby is not
    # offering up yet. Nullable because a lobby build that predates them reports neither,
    # and a missing reading is honestly different from a reading of zero.
    queueing: Optional[int] = Field(default=None, nullable=True)
    held: Optional[int] = Field(default=None, nullable=True)

    @staticmethod
    def record(address: str, players: int, queueing: int, held: int) -> None:
        """Note this reading if it says anything new, and announce an arena waking up.

        Both jobs here because both need the previous reading, and reading it twice
        would be two chances to disagree about whether the arena was empty.
        """
        from src.core.db import open

        with open() as db:
            previous = (
                db.query(ArenaReading)
                .where(ArenaReading.address == address)
                .order_by(desc(ArenaReading.at))
                .first()
            )
            unchanged = previous is not None and (
                previous.players,
                previous.queueing,
                previous.held,
            ) == (players, queueing, held)
            if unchanged:
                return

            db.add(
                ArenaReading(
                    address=address,
                    at=int(time()),
                    players=players,
                    queueing=queueing,
                    held=held,
                )
            )
            server = (
                db.query(AuthorizedServer)
                .where(AuthorizedServer.address == address)
                .first()
            )
            region = server.region if server else None

        if ArenaReading._isWakingUp(previous, players):
            ArenaReading._announceWakeUp(players, region)

    @staticmethod
    def _isWakingUp(previous: "ArenaReading | None", players: int) -> bool:
        """Whether this reading is the first sign of life after a long quiet spell.

        The quiet has to have lasted, because an arena flickers: people join an empty
        server, find nobody in it and leave within seconds, and announcing each of those
        would train everybody to ignore the announcement that matters. A lobby nobody has
        reported for is treated as quiet, so a server coming back up with people already
        in it still counts as waking.
        """
        if players < 1:
            return False
        if previous is None:
            return True
        if previous.players > 0:
            return False
        return time() - previous.at >= settings.wake_ping_after

    @staticmethod
    def _announceWakeUp(players: int, region: str | None) -> None:
        """Tell Discord a long-empty arena has a player in it, while they are still
        there to be joined."""
        from src.core import notify

        where = settings.region_names.get(region, "the arena")
        notify.send_discord_channel(
            settings.queue_log_channel,
            content=(
                f"**Someone is in the arena.** {players} "
                f"player{'' if players == 1 else 's'} in `{where}` right now — "
                f"join them at [{settings.domain}](<{settings.base_url}>) "
                "before they get bored and leave."
            ),
        )


class ServerRequest(SQLModel, table=True):
    """Audit log for every /unt request received from a game server."""

    __tablename__ = "ServerRequests"

    id: Optional[int] = Field(default=None, primary_key=True)
    address: str = Field(foreign_key="AuthorizedServers.address", nullable=False)
    path: str = Field(nullable=False)
    method: str = Field(nullable=False)
    match_id: Optional[int] = Field(default=None, nullable=True)
    created_at: Optional[datetime] = Field(
        default=None,
        sa_column=Column(
            DateTime(timezone=True), server_default=text("CURRENT_TIMESTAMP")
        ),
    )
