from __future__ import annotations

from datetime import datetime
from typing import TYPE_CHECKING, Optional

from sqlalchemy import BigInteger, Column, DateTime, ForeignKey, JSON, text
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
    def bootstrapAdmin(steam64: int) -> None:
        """Give one account admin, creating it if it has never logged in.

        A fresh environment has nobody who can reach /admin, and no way to make
        anyone an admin through the site. Driven by BOOTSTRAP_ADMIN_STEAM64 so
        only the environments that set it are affected.
        """
        from src.core.db import open
        from uuid import uuid4

        bit = 2 ** settings.permissions.index("admin")
        with open() as db:
            user = db.query(User).where(User.steam64 == steam64).first()
            if user is None:
                # The Steam callback matches on steam64, so this row is picked up
                # on first login rather than being duplicated.
                db.add(User(steam64=steam64, uuid=str(uuid4()), permissions=bit))
            else:
                user.permissions |= bit

    @staticmethod
    def ensure(db, steam64) -> None:
        """A Users row for this steam64, created if the site has never seen them.

        Game servers report players the website has no account for — most people
        standing in a warm-up lobby have never opened it — and everything hung off
        Users by foreign key is refused outright by Postgres until the row exists.
        Stat.append has always done this for match results; the queue did not, so
        every one of those players 500'd the moment the lobby tried to queue them.

        The account is created with their Steam name and avatar already on it. That
        has to happen here, before the insert, because steam._persist_steam_data
        writes with an UPDATE: a lookup made while there is no row to update stores
        nothing and reports nothing wrong. That is how a player who joined a game
        server got an account with no name, and then showed up as "Nameless"
        everywhere the site drew them.
        """
        if db.query(User).where(User.steam64 == steam64).first():
            return

        from datetime import datetime, timezone
        from uuid import uuid4

        db.add(
            User(
                steam64=steam64,
                uuid=str(uuid4()),
                steam_data=User._steamProfile(steam64),
                steam_fetched_at=datetime.now(timezone.utc),
            )
        )
        # Before the caller's own insert, which is the one with the foreign key.
        db.flush()

    @staticmethod
    def _steamProfile(steam64) -> dict | None:
        """This player's name and avatar from Steam, or None if Steam would not say.

        None rather than a placeholder on purpose: storing "Nameless" would make the
        fallback permanent, since every later read finds stored data and stops asking.
        Leaving it empty means the next lookup fills it in — and by then there is a
        row for it to fill.
        """
        from src.core import steam

        try:
            raw = steam.get_steam_user_data(steam64)
        except Exception:
            raw = None

        if not raw:
            return None

        return {"name": raw.get("personaname"), "avatar": raw.get("avatarfull")}

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
            User.ensure(db, steam64)
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
    steam64: int = Field(
        sa_column=Column(BigInteger, ForeignKey("Users.steam64"), nullable=False)
    )
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
