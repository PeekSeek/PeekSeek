from __future__ import annotations

from time import time
from typing import Optional
from uuid import uuid4

from sqlalchemy import BigInteger, Column, ForeignKey, desc
from sqlmodel import Field, Relationship, SQLModel

from .rank import Rank

_top10Cache: list = [None, 0]


class Stat(SQLModel, table=True):
    __tablename__ = "Stats"

    steam64: int = Field(
        sa_column=Column(BigInteger, ForeignKey("Users.steam64"), primary_key=True),
    )
    game: str = Field(default="unt", nullable=False)
    elo: int = Field(default=300, nullable=False)
    kills: int = Field(default=0, nullable=False)
    deaths: int = Field(default=0, nullable=False)
    wins: int = Field(default=0, nullable=False)
    losses: int = Field(default=0, nullable=False)
    headshots: int = Field(default=0, nullable=False)
    damageDealt: int = Field(default=0, nullable=False)
    damageTaken: int = Field(default=0, nullable=False)
    # Every shot that connected, and how many of those hit the head. Headshot *kills*
    # (`headshots` above) are a far rarer thing than what a shooter means by "HS%": a
    # kill only counts as one when the final blow of the fight happened to land on the
    # skull, so in a mode where nothing dies to a single bullet that number sits at zero
    # for players who hit heads constantly. HS% is derived from these two instead.
    hits: int = Field(default=0, nullable=False)
    headshotHits: int = Field(default=0, nullable=False)

    user: User = Relationship(back_populates="stats")

    @property
    def headshotPercent(self) -> float:
        """Share of landed shots that hit the head, to one decimal.

        Falls back to headshot kills over kills for players whose record predates hit
        tracking, so an old profile still shows the number it always showed rather than
        dropping to zero.
        """
        if self.hits:
            return round(self.headshotHits / self.hits * 100, 1)
        if self.kills:
            return round(self.headshots / self.kills * 100, 1)
        return 0.0

    @staticmethod
    def append(steam64, game: str, **kwargs) -> None:
        from src.core.db import open
        from .user import User

        with open() as db:
            if not isinstance(steam64, Stat):
                stats = db.query(Stat).where(Stat.steam64 == steam64).first()
            else:
                stats, steam64 = steam64, steam64.steam64

            if not stats:
                user = db.query(User).where(User.steam64 == steam64).first()
                if not user:
                    db.add(User(steam64=steam64, uuid=str(uuid4())))
                stats = Stat(steam64=steam64, game=game, elo=300)
                db.add(stats)

            increments = {
                k: (getattr(stats, k, 0) or 0) + v for k, v in kwargs.items() if v != 0
            }
            # An UPDATE with nothing to set is not valid SQL — it raises rather than
            # doing nothing, which is what a result where every counter happened to be
            # zero used to do to a whole match report.
            if not increments:
                return
            db.query(Stat).where(Stat.steam64 == steam64).update(increments)

    @staticmethod
    def getTop10():
        from src.core.db import open
        from .user import User

        if _top10Cache[0] is not None and time() - _top10Cache[1] < 60:
            return _top10Cache[0]

        with open() as db:
            results = (
                db.query(Stat)
                .order_by(desc(Stat.elo), desc(Stat.wins), desc(Stat.kills))
                .limit(10)
                .all()
            )

            class _T:
                def __init__(self, **kw):
                    for k, v in kw.items():
                        setattr(self, k, v)

            rings = User.ringsFor([r.steam64 for r in results])
            _top10Cache[0] = [
                _T(
                    u=User.getSteamData(r.steam64),
                    elo=r.elo,
                    rank=Rank.forElo(r.elo, position),
                    ring=rings.get(str(r.steam64)),
                )
                for position, r in enumerate(results, 1)
            ]
            _top10Cache[1] = time()
            return _top10Cache[0]

    @property
    def rank(self) -> Rank:
        return Rank.forElo(self.elo, Stat.ladderPosition(self.steam64))

    @staticmethod
    def ladderPosition(steam64) -> int | None:
        """This player's 1-based place on the ladder, or None if they are not near the
        top of it.

        Only the top is worth answering for, because the one thing position decides is
        whether a capped tier is held and those caps are a handful of seats. Reads the
        cached top ten the leaderboard already draws, so asking costs no query.
        """
        for position, entry in enumerate(Stat.getTop10(), 1):
            if int(entry.u.steam64) == int(steam64):
                return position
        return None

    @staticmethod
    def rankFor(steam64) -> Rank:
        """The badge this player wears, from a steam64 alone.

        For callers holding no Stat row -- the sidebar draws the same card as the
        leaderboard and only ever has the signed-in user's id. One query: the ladder
        position behind it comes from the cached top ten.
        """
        return Rank.forElo(Stat.getElo(steam64), Stat.ladderPosition(steam64))

    @staticmethod
    def getElo(steam64, game: str = "unt") -> int:
        from src.core.db import open

        with open() as db:
            r = db.query(Stat).where(Stat.steam64 == steam64, Stat.game == game).first()
            return r.elo if r else 300


class Hlwn25(SQLModel, table=True):
    __tablename__ = "Hlwn25"

    steam64: int = Field(
        sa_column=Column(BigInteger, ForeignKey("Users.steam64"), primary_key=True),
    )
    score: int = Field(nullable=False)

    @staticmethod
    def append(steam64: int) -> None:
        from src.core.db import open

        with open() as db:
            q = db.query(Hlwn25).where(Hlwn25.steam64 == steam64)
            if q.first():
                q.update({"score": Hlwn25.score + 1})
                return
            db.add(Hlwn25(steam64=steam64, score=1))


class UnturnedItem(SQLModel, table=True):
    __tablename__ = "unturned_items"

    item_id: int = Field(primary_key=True)
    item_name: Optional[str] = None
