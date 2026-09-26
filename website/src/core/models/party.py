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
        """Live heartbeats for a queue, deleting any that have gone stale or banned.

        The ban filter is here rather than only at the point of queueing because a ban
        can land while someone is already waiting. Their row was written before anyone
        banned them, so nothing else would ever reconsider it, and the matchmaker would
        put them in the next roster it built. Dropping the row rather than skipping it
        keeps them out of the population counts a game server reads too.
        """
        from .user import UserBan

        queue = (
            UserHB.eventId.is_(None) if eventId is None else UserHB.eventId == eventId
        )
        rows = db.query(UserHB).where(queue).all()
        banned = UserBan.activeAmong([row.steam64 for row in rows], db)

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
            return len(
                [
                    heartbeat
                    for heartbeat in UserHB.freshInQueue(db)
                    if heartbeat.region == region
                ]
            )

    @staticmethod
    def countAllFresh() -> int:
        """How many players are waiting for a match anywhere.

        Separate from countFresh because that one answers a game server asking about
        its own region, and this one answers "is anything happening here at all",
        where a queue in Utah counts exactly as much as one in Paris.
        """
        from src.core.db import open

        with open() as db:
            return len(UserHB.freshInQueue(db))

    @staticmethod
    def claim(steam64s) -> bool:
        """Take these players out of the queue, and say whether this call is the one
        that got them.

        This is the lock that stops two game servers building a match out of the same
        people. Both scan the queue, both like the same roster, and both end up here,
        but each delete runs in a transaction of its own, so the second one waits for
        the first to commit and then finds nothing left to remove. Being told False
        means somebody else got there first, and the caller must not create anything.

        All of them or none. A partial claim would take players out of a queue they
        are still waiting in, having handed the rest of their roster to another
        server, so anything short of the full set is rolled back and they stay queued.

        Leaving the queue is also what makes them unavailable everywhere else, which is
        why it happens before the match is built rather than alongside it: building one
        means Steam lookups and Discord messages, and for that whole second these
        players would still be sitting in the queue for the next server to find.
        """
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
