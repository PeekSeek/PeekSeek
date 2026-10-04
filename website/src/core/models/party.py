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
        has gone quiet. The table only holds live parties, so it is scanned.

        Being looked up is the liveness signal: the play page polls /party/ every few
        seconds, so a party lasts exactly as long as somebody has it on screen.
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
        """Live heartbeats for a queue, deleting any that have gone stale or barred.

        Checked here as well as on joining, because a ban can land while somebody is
        already waiting.
        """
        from .user import UserBan

        queue = (
            UserHB.eventId.is_(None) if eventId is None else UserHB.eventId == eventId
        )
        rows = db.query(UserHB).where(queue).all()
        banned = UserBan.barring([row.steam64 for row in rows], db)

        fresh = []
        for heartbeat in rows:
            stale = time() - heartbeat.time > settings.queue_timeout
            if stale or heartbeat.steam64 in banned:
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
            return UserHB._fresh(db).where(UserHB.region == region).count()

    @staticmethod
    def countAllFresh() -> int:
        """How many players are waiting for a match anywhere.

        Separate from countFresh because that one answers a game server asking about
        its own region, and this one answers "is anything happening here at all",
        where a queue in Utah counts exactly as much as one in Paris.
        """
        from src.core.db import open

        with open() as db:
            return UserHB._fresh(db).count()

    @staticmethod
    def _fresh(db, eventId: int | None = None):
        """Matchmaking heartbeats young enough to count: the open queue by default, or
        one event's own queue. A read only: `freshInQueue` is what sweeps out the stale
        and the barred."""
        return db.query(UserHB).where(
            UserHB.eventId.is_(None) if eventId is None else UserHB.eventId == eventId,
            UserHB.time >= time() - settings.queue_timeout,
        )

    @staticmethod
    def countFreshForEvent(eventId: int) -> int:
        """How many players are waiting for one event's match right now.

        Shown on the event page beside its join button, so a visitor can see whether
        there is anybody to play against before they launch the game. It goes through
        `_fresh` rather than `freshInQueue` because that one deletes what it finds and
        a page render has no business writing.
        """
        from src.core.db import open

        with open() as db:
            return UserHB._fresh(db, eventId).count()

    @staticmethod
    def claim(steam64s) -> bool:
        """Take all of these players out of the queue, or none of them, and say
        whether this call got them. Done before the match is built, so nobody else can
        find them queued while it is."""
        from src.core.db import open

        steam64s = [int(s) for s in steam64s]
        with open() as session:
            removed = (
                session.query(UserHB)
                .where(UserHB.steam64.in_(steam64s))
                .delete(synchronize_session=False)
            )
            if removed == len(steam64s):
                return True
            session.rollback()
            return False
