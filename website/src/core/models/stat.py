from __future__ import annotations

from time import time
from typing import Optional
from uuid import uuid4

from sqlalchemy import BigInteger, Column, ForeignKey, desc
from sqlmodel import Field, Relationship, SQLModel

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

    user: User = Relationship(back_populates="stats")

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

            db.query(Stat).where(Stat.steam64 == steam64).update(
                {
                    k: (getattr(stats, k, 0) or 0) + v
                    for k, v in kwargs.items()
                    if v != 0
                }
            )

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

            _top10Cache[0] = [
                _T(u=User.getSteamData(r.steam64), elo=r.elo) for r in results
            ]
            _top10Cache[1] = time()
            return _top10Cache[0]

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
