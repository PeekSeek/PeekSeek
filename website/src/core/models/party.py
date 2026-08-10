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

    user: User = Relationship(back_populates="parties")
    heartbeats: UserHB = Relationship(back_populates="party")


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
    def dequeue(steam64s) -> None:
        from src.core.db import open

        with open() as db:
            db.query(UserHB).where(
                UserHB.steam64.in_([int(s) for s in steam64s])
            ).delete()
