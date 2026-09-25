from __future__ import annotations

from datetime import datetime
from typing import Optional
from uuid import uuid4

from sqlalchemy import BigInteger, Column, DateTime, ForeignKey, JSON, text
from sqlmodel import Field, Relationship, SQLModel


class Event(SQLModel, table=True):
    __tablename__ = "Events"

    id: Optional[int] = Field(default=None, primary_key=True)
    ownerSteam64: int = Field(
        sa_column=Column(BigInteger, ForeignKey("Users.steam64"), nullable=False),
    )
    name: Optional[str] = None
    timeCreated: int = Field(nullable=False)
    gamemode: int = Field(nullable=False)
    data: dict = Field(default_factory=dict, sa_column=Column(JSON, nullable=False))

    signups: EventSignup = Relationship(back_populates="event")

    @property
    def continent(self):
        return {
            "paris": "EU",
            "brazil": "SA",
            "singapore": "AS",
            "utah": "NA",
            "debug": "Development",
        }[self.data["meta"]["region"]]


class EventSignup(SQLModel, table=True):
    __tablename__ = "EventSignups"

    id: Optional[int] = Field(default=None, primary_key=True)
    ownerSteam64: int = Field(
        sa_column=Column(BigInteger, ForeignKey("Users.steam64"), nullable=False),
    )
    users: list = Field(default_factory=list, sa_column=Column(JSON, nullable=False))
    score: int = Field(default=0, nullable=False)
    matchesplayed: int = Field(default=0, nullable=False)
    joincode: str = Field(nullable=False)
    teamname: str = Field(nullable=False)
    event_id: int = Field(foreign_key="Events.id", nullable=False)
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

    event: Event = Relationship(back_populates="signups")

    def __init__(self, ownerSteam64: int, eventId: int, teamname: str):
        from src.core.db import open
        from .user import User

        self.ownerSteam64 = ownerSteam64
        self.event_id = eventId
        self.teamname = teamname
        self.joincode = str(uuid4())
        self.users = []
        self.score = 0
        self.matchesplayed = 0

        with open() as db:
            user = db.query(User).where(User.steam64 == ownerSteam64).first()
            if user:
                user.notify(
                    "Event signup created",
                    f"You created an event signup with the join code `{self.joincode}`.",
                    footer="You can share this code with your friends to let them join your team.",
                )
