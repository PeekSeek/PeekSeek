from __future__ import annotations

from datetime import datetime
from time import time
from typing import Optional

from sqlalchemy import BigInteger, Column, DateTime, ForeignKey, String, desc, text
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
    """How many players one lobby server currently has standing in its arena.

    The queue is not this number and cannot stand in for it. A lobby holds players
    back from the queue on purpose to keep its own browser listing worth joining, so
    a busy arena can honestly report nobody queueing at all — and "0 in queue" is the
    one thing the Discord counter must never say while six people are fighting.

    One row per server, overwritten in place: this is a current reading, and the only
    thing that reads it wants to know what is true now. What the arena looked like an
    hour ago is `ArenaReading`'s job, and it is kept apart precisely so that the
    counter's question stays a single-row lookup no matter how long the history grows.
    """

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
    def playingNow() -> int:
        """Everyone playing PeekSeek anywhere: in a lobby arena, or in a live match.

        The two halves come from opposite directions and that is the point. Lobbies
        report themselves, because only they know who is standing in them. Match
        servers are never asked — the site already knows every roster it handed out,
        so twelve match servers need no code at all to be counted here.

        They partition rather than overlap: a lobby counts only the players who are
        not on a match, so somebody still in the arena picking their kit is counted
        once, by the match rather than by the room they happen to be standing in.
        """
        from .match import Match

        return LobbyPopulation.countFresh() + len(Match.playersInAMatch())


class ArenaReading(SQLModel, table=True):
    """What one lobby's arena looked like at one moment, kept for as long as it is
    useful to look back at.

    This table exists because the only question that matters — why are there not more
    matches per day — was unanswerable. Matches are recorded, and nothing else was: how
    many people walked into the arena, how many of them asked for a match, how long the
    arena sat empty, and how much of the waiting was this lobby's own hold rather than
    an absence of opponents. All four are readable from these rows and from nothing else
    the site stores.

    `held` is the one worth having. A lobby keeps a floor of players back from the queue
    so its browser listing stays worth joining, which is a trade — matches given up now
    for players attracted later — and until this column existed there was no number
    anywhere on either side of it.

    Written only when a reading differs from the one before it, so a quiet arena costs
    one row rather than one every five seconds, and the gap between two rows is itself
    how long that state lasted.
    """

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
        """Tell Discord the arena is live.

        This is the only push the game has for the moment that decides the whole day. An
        arena that has been empty for hours does not refill on its own — the Unturned
        browser sorts on player count, so an empty server is invisible and the person who
        joins it anyway finds nobody, leaves, and takes the next few hours with them. The
        one lever there is is to tell the people who would come if they knew, while that
        first player is still standing in it.

        Deliberately not the same thing as the counter channel, which is ambient and only
        moves twice per ten minutes. This is a message somebody gets notified about.
        """
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
