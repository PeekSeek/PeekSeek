from __future__ import annotations

from datetime import datetime
from typing import TYPE_CHECKING, Optional

from sqlalchemy import BigInteger, Column, DateTime, JSON, text
from sqlmodel import Field, Relationship, SQLModel

from src.core.settings import settings

if TYPE_CHECKING:
    pass


class User(SQLModel, table=True):
    __tablename__ = "Users"

    steam64: int = Field(
        sa_column=Column(BigInteger, primary_key=True, autoincrement=True)
    )
    uuid: str = Field(unique=True, nullable=False)
    permissions: int = Field(default=0, nullable=False)
    discord: Optional[int] = Field(
        default=None, sa_column=Column(BigInteger, nullable=True)
    )
    steam_data: Optional[dict] = Field(
        default=None,
        sa_column=Column(JSON, nullable=True),
    )
    steam_fetched_at: Optional[datetime] = Field(
        default=None,
        sa_column=Column(DateTime(timezone=True), nullable=True),
    )
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

    kits: Kit = Relationship(back_populates="user")
    stats: Stat = Relationship(back_populates="user")
    parties: Party = Relationship(back_populates="user")
    heartbeats: UserHB = Relationship(back_populates="user")

    def notify(self, title: str, message: str, **kwargs) -> None:
        from src.core.notify import send_discord_dm

        if self.discord:
            send_discord_dm(self.discord, title, message, **kwargs)

    @staticmethod
    def getSteamData(steam64) -> object:
        from src.core import steam

        if isinstance(steam64, User):
            steam64 = steam64.steam64
        return steam.User(steam64)

    def getCurrentMatchId(self, onlyFirst: bool = True):
        from src.core.db import open
        from .match import Match

        steam64 = self.steam64 if isinstance(self, User) else self
        output: list | int = []
        with open() as db:
            for match in db.query(Match).where(Match.finished.is_(False)).all():
                users = [str(u) for team in match.teams for u in team["users"]]
                if str(steam64) not in users:
                    continue
                if onlyFirst:
                    return match.id
                output.append(match.id)
        return output or None

    def getPermission(self, item: str) -> bool:
        from src.core.db import open

        if item not in settings.permissions:
            raise TypeError("Permission does not exist.")

        if isinstance(self, User):
            permissions = self.permissions
        else:
            with open() as db:
                u = db.query(User).where(User.steam64 == self).first()
                permissions = u.permissions if u else 0

        if item != "admin" and User.getPermission(self, "admin"):
            return True

        bit = 2 ** settings.permissions.index(item)
        return (permissions & bit) == bit

    def setPermission(self, item: str, value: bool) -> None:
        from src.core.db import open

        if item not in settings.permissions:
            raise TypeError("Permission does not exist.")
        bit = 2 ** settings.permissions.index(item)
        if value:
            self.permissions |= bit
        else:
            self.permissions &= ~bit
        with open() as db:
            db.query(User).where(User.steam64 == self.steam64).update(
                {"permissions": self.permissions}
            )

    @staticmethod
    def addToQueue(
        steam64,
        gamemode: int,
        partyId: int = None,
        region: str = "paris",
        eventId: int = None,
    ):
        from src.core.db import open
        from .party import UserHB
        from time import time

        if isinstance(steam64, User):
            steam64 = steam64.steam64
        with open() as db:
            db.query(UserHB).where(UserHB.steam64 == steam64).delete()
            db.add(
                UserHB(
                    steam64=steam64,
                    time=time(),
                    gamemode=gamemode,
                    partyId=partyId,
                    region=region,
                    eventId=eventId,
                )
            )

    @staticmethod
    def removeFromQueue(steam64):
        from src.core.db import open
        from .party import UserHB

        if isinstance(steam64, User):
            steam64 = steam64.steam64
        with open() as db:
            db.query(UserHB).where(UserHB.steam64 == steam64).delete()


class UserIP(SQLModel, table=True):
    __tablename__ = "UserIPs"

    id: Optional[int] = Field(default=None, primary_key=True)
    steam64: int = Field(foreign_key="Users.steam64", nullable=False)
    address: str = Field(nullable=False)


class UserBan(SQLModel, table=True):
    __tablename__ = "UserBans"

    id: Optional[int] = Field(default=None, primary_key=True)
    steam64: int = Field(sa_column=Column(BigInteger, nullable=False))
    endTime: int = Field(sa_column=Column(BigInteger, nullable=False))
    reason: Optional[str] = None
    moderatorNote: Optional[str] = None
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
