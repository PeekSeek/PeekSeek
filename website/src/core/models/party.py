from __future__ import annotations

from time import time
from typing import Optional

from sqlalchemy import BigInteger, Column, ForeignKey, JSON
from sqlmodel import Field, Relationship, SQLModel

from src.core.settings import settings


class Party(SQLModel, table=True):
    __tablename__ = "Parties"

    id: Optional[int] = Field(default=None, primary_key=True)
    steam64: int = Field(
        sa_column=Column(BigInteger, ForeignKey("Users.steam64"), nullable=False)
    )
    data: dict = Field(default_factory=dict, sa_column=Column(JSON, nullable=False))
    # Nullable because parties that predate this column have no honest value for
    # it; find() backfills those with the time it first sees them.
    lastSeen: Optional[int] = Field(
        default=None, sa_column=Column(BigInteger, nullable=True)
    )

    user: User = Relationship(back_populates="parties")
    heartbeats: UserHB = Relationship(back_populates="party")

    @staticmethod
    def find(db, steam64) -> "Party | None":
        """The party this player currently belongs to, if any, disbanding any that
        has gone quiet. Scans every party rather than indexing membership — the same
        "small table, filter in Python" approach User.getCurrentMatchId already takes
        over Matches.

        Being looked up *is* the liveness signal. The play page polls /party/ every
        few seconds and the queue heartbeat looks its own party up, so a party lasts
        exactly as long as somebody still has it on screen — close every tab and it
        disbands on its own instead of following its members into next week.
        """
        steam64 = str(steam64)
        now = int(time())
        mine = None

        for party in db.query(Party).all():
            party.lastSeen = party.lastSeen or now
            if now - party.lastSeen > settings.party_timeout:
                # Heartbeats carry the party's id as a foreign key, so they go
                # first or Postgres refuses the delete. Nothing is lost by it: a
                # party nobody has looked at in ten minutes has no business still
                # sitting in the queue under someone else's name.
                db.query(UserHB).where(UserHB.partyId == party.id).delete()
                db.delete(party)
                continue
            if steam64 in party.data.get("members", []):
                party.lastSeen = now
                mine = party

        return mine

    @staticmethod
    def pendingInvites(db, steam64) -> list["Party"]:
        steam64 = str(steam64)
        return [
            p for p in db.query(Party).all() if steam64 in p.data.get("invites", [])
        ]


class UserHB(SQLModel, table=True):
    """A queue heartbeat — one row per player currently waiting for a match."""

    __tablename__ = "UserHB"

    steam64: int = Field(
        sa_column=Column(
            BigInteger, ForeignKey("Users.steam64"), primary_key=True, nullable=False
        ),
    )
    partyId: Optional[int] = Field(default=None, foreign_key="Parties.id")
    time: int = Field(sa_column=Column(BigInteger, nullable=False))
    region: str = Field(nullable=False)
    gamemode: int = Field(nullable=False)
    eventId: Optional[int] = Field(default=None, nullable=True)

    user: User = Relationship(back_populates="heartbeats")
    party: Party = Relationship(back_populates="heartbeats")

    @staticmethod
    def freshInQueue(db, eventId: int | None = None) -> list["UserHB"]:
        """Live heartbeats for a queue, deleting any that have gone stale."""
        queue = (
            UserHB.eventId.is_(None) if eventId is None else UserHB.eventId == eventId
        )
        fresh = []
        for heartbeat in db.query(UserHB).where(queue).all():
            if time() - heartbeat.time > settings.queue_timeout:
                db.delete(heartbeat)
                continue
            fresh.append(heartbeat)
        return fresh

    @staticmethod
    def countFresh(region: str | None) -> int:
        """How many players are waiting for a match in one region right now.

        Reported back to game servers so "nothing happened" can be told apart from
        "nobody is queueing here" without reading the database.
        """
        from src.core.db import open

        with open() as db:
            return len(
                [
                    heartbeat
                    for heartbeat in UserHB.freshInQueue(db)
                    if heartbeat.region == region
                ]
            )

    @staticmethod
    def dequeue(steam64s) -> None:
        from src.core.db import open

        with open() as db:
            db.query(UserHB).where(
                UserHB.steam64.in_([int(s) for s in steam64s])
            ).delete()
