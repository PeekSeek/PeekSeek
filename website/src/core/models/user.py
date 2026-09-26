from __future__ import annotations

from datetime import datetime
from typing import ClassVar, Optional

from sqlalchemy import BigInteger, Column, DateTime, ForeignKey, Index, JSON, text
from sqlmodel import Field, Relationship, SQLModel

from src.core.settings import settings


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

    @property
    def ring(self) -> str | None:
        """The colour drawn around this player's avatar wherever the site names them.

        A ring can only say one thing, so the rarest standing wins and admin is checked
        first -- getPermission answers True to every permission for an admin, so asking
        about premium first would call every admin a supporter. A place in the top three
        outranks all of these, but that is the ladder's to know rather than this
        account's, so it is applied where the card is drawn.
        """
        if self.getPermission("admin"):
            return "admin"
        if self.getPermission("premium"):
            return "premium"
        if self.discord:
            return "discord"
        return None

    @staticmethod
    def ringsFor(steam64s) -> dict[str, str]:
        """Every ring on a page in one query, keyed by steam64 as a string.

        A match table names up to ten players and the ring is one column of one row
        each; asked one at a time that is ten round trips for a colour.
        """
        from src.core.db import open

        with open() as db:
            users = (
                db.query(User).where(User.steam64.in_([int(s) for s in steam64s])).all()
            )
            return {str(u.steam64): u.ring for u in users if u.ring}

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
        """A Users row for this steam64, created if the site has never seen them, since
        most players in a lobby have never opened the site. The Steam profile is looked
        up first, because storing it later is an UPDATE that needs the row."""
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
        Never a placeholder, which would be stored and never asked about again."""
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
        from src.core.db import MATCHMAKER_LOCK, alongside, open
        from .match import Match, MatchPlayer
        from .party import UserHB
        from time import time

        if isinstance(steam64, User):
            steam64 = steam64.steam64
        with open() as db:
            # Every route into the queue comes through here, so this is the one place
            # a ban has to be checked for queueing to mean anything.
            ban = UserBan.barring([steam64], db).get(int(steam64))
            if ban is not None:
                db.query(UserHB).where(UserHB.steam64 == steam64).delete()
                return ban

            # Never between a match being built and being written: a heartbeat that
            # checked just before would queue somebody already on their way to a match,
            # and they would be matched again the moment it ended.
            alongside(db, MATCHMAKER_LOCK)
            onAMatch = (
                db.query(MatchPlayer.matchId)
                .join(Match, Match.id == MatchPlayer.matchId)
                .where(MatchPlayer.steam64 == int(steam64), Match.finished.is_(False))
                .first()
            )
            if onAMatch is not None:
                db.query(UserHB).where(UserHB.steam64 == steam64).delete()
                return None

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
            return None

    @staticmethod
    def removeFromQueue(steam64):
        from src.core.db import open
        from .party import UserHB

        if isinstance(steam64, User):
            steam64 = steam64.steam64
        with open() as db:
            db.query(UserHB).where(UserHB.steam64 == steam64).delete()


class UserIP(SQLModel, table=True):
    """Every address an account has been seen on, from the site and from the lobby."""

    __tablename__ = "UserIPs"
    __table_args__ = (
        Index("ix_UserIPs_steam64_address", "steam64", "address"),
        Index("ix_UserIPs_address", "address"),
    )

    id: Optional[int] = Field(default=None, primary_key=True)
    steam64: int = Field(
        sa_column=Column(BigInteger, ForeignKey("Users.steam64"), nullable=False)
    )
    address: str = Field(nullable=False)

    @staticmethod
    def remember(db, steam64: int, address: str) -> None:
        """Record this address for this account, once, creating the account if the
        site has never seen it."""
        if not address:
            return
        seen = (
            db.query(UserIP.id)
            .where(UserIP.steam64 == int(steam64), UserIP.address == address)
            .first()
        )
        if seen is None:
            User.ensure(db, int(steam64))
            db.add(UserIP(steam64=int(steam64), address=address))
            db.flush()


class UserBan(SQLModel, table=True):
    __tablename__ = "UserBans"

    id: Optional[int] = Field(default=None, primary_key=True)
    steam64: int = Field(sa_column=Column(BigInteger, nullable=False, index=True))
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

    # Far enough out to be permanent in every sense that matters, and the value
    # POST /server/v1/players/{steam64}/bans has always written. Rows predating this
    # constant also use 9999999999999999; anything beyond now reads as permanent, so
    # both are honoured and only new bans are normalised onto this one.
    PERMANENT: ClassVar[int] = 9999999999

    @property
    def permanent(self) -> bool:
        return self.endTime >= UserBan.PERMANENT

    @property
    def expiryLabel(self) -> str:
        from datetime import datetime, timezone

        if self.permanent:
            return "Permanent"
        return datetime.fromtimestamp(self.endTime, timezone.utc).strftime(
            "%d %b %Y, %H:%M UTC"
        )

    @property
    def remainingLabel(self) -> str:
        """How much longer this lasts, in the unit somebody would say out loud.

        Days once there is more than a day left, hours below that, and "less than an
        hour" at the end rather than counting down minutes: a ban is not a timer
        anybody should be watching tick.
        """
        from time import time

        if self.permanent:
            return "permanently"

        left = self.endTime - int(time())
        if left <= 0:
            return "no longer"
        if left < 3600:
            return "less than an hour"
        if left < 86400:
            hours = left // 3600
            return f"another {hours} hour{'' if hours == 1 else 's'}"

        days = left // 86400
        return f"another {days} day{'' if days == 1 else 's'}"

    @staticmethod
    def activeFor(steam64, db=None) -> "UserBan | None":
        """This player's ban if one is in force right now, else None.

        The single answer to "may this player play", so the queue, the matchmaker, the
        game-server API and the admin page cannot drift apart on it. Takes an optional
        session because the callers inside matchmaking already hold one and opening a
        second per player is what made the old per-player lookups expensive.
        """
        from time import time

        def query(session):
            return (
                session.query(UserBan)
                .where(UserBan.steam64 == int(steam64), UserBan.endTime > int(time()))
                .order_by(UserBan.endTime.desc())
                .first()
            )

        if db is not None:
            return query(db)

        from src.core.db import open

        with open() as session:
            ban = query(session)
            if ban is not None:
                session.expunge(ban)
            return ban

    @staticmethod
    def barring(steam64s, db) -> dict[int, "UserBan"]:
        """The ban that keeps each of these players out of queues and matches.

        A player is barred by their own active ban, or by an active ban on any account
        that has been seen on one of their addresses. Players nothing bars are left out.
        Account-only questions, such as the banner and the admin drawer, use `activeFor`.
        """
        from time import time

        from sqlalchemy.orm import aliased

        ids = {int(s) for s in steam64s}
        if not ids:
            return {}

        now = int(time())
        found = [
            (ban.steam64, ban)
            for ban in db.query(UserBan)
            .where(UserBan.steam64.in_(ids), UserBan.endTime > now)
            .all()
        ]
        mine, shared = aliased(UserIP), aliased(UserIP)
        found += (
            db.query(mine.steam64, UserBan)
            .join(shared, shared.address == mine.address)
            .join(UserBan, UserBan.steam64 == shared.steam64)
            .where(mine.steam64.in_(ids), UserBan.endTime > now)
            .all()
        )

        barred: dict[int, UserBan] = {}
        for steam64, ban in found:
            held = barred.get(steam64)
            if held is None or ban.endTime > held.endTime:
                barred[steam64] = ban
        return barred

    @staticmethod
    def activeAmong(steam64s, db=None) -> set:
        """Which of these players are banned, in one query rather than one each.

        The matchmaker asks about every player in the queue on every poll, so asking
        per player would put a round trip per queued player on the hot path.
        """
        from time import time

        ids = [int(s) for s in steam64s]
        if not ids:
            return set()

        def query(session):
            return {
                row.steam64
                for row in session.query(UserBan)
                .where(UserBan.steam64.in_(ids), UserBan.endTime > int(time()))
                .all()
            }

        if db is not None:
            return query(db)

        from src.core.db import open

        with open() as session:
            return query(session)

    @staticmethod
    def historyFor(steam64) -> list["UserBan"]:
        """Every ban this player has ever had, newest first, expired ones included.

        An admin deciding what to do about somebody needs the pattern, not just whether
        they happen to be banned at this second.
        """
        from src.core.db import open

        with open() as db:
            rows = (
                db.query(UserBan)
                .where(UserBan.steam64 == int(steam64))
                .order_by(UserBan.created_at.desc())
                .all()
            )
            for row in rows:
                db.expunge(row)
            return rows
