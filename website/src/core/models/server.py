from __future__ import annotations

from datetime import datetime
from time import time
from typing import Optional

from sqlalchemy import BigInteger, Column, DateTime, ForeignKey, String, text
from sqlmodel import Field, SQLModel


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

    One row per server, overwritten in place: this is a current reading, not a
    history, and nothing here wants to know what the arena looked like an hour ago.
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
            row = db.query(LobbyPopulation).where(
                LobbyPopulation.address == address
            ).first()
            if row is None:
                db.add(LobbyPopulation(address=address, players=players, at=int(time())))
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
