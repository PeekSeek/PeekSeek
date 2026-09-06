"""Seasons: the periods the ladder is played in, and what carries between them."""

from __future__ import annotations

from datetime import datetime
from typing import Optional

from sqlalchemy import BigInteger, Column, DateTime, desc, text
from sqlmodel import Field, SQLModel

from src.core.settings import settings


class Season(SQLModel, table=True):
    __tablename__ = "Seasons"

    id: Optional[int] = Field(default=None, primary_key=True)
    # The whole name, prefix included. Stored rather than composed at render time so a
    # rename never rewrites what a finished season was called on somebody's profile.
    name: str = Field(unique=True, nullable=False)
    created_at: Optional[datetime] = Field(
        default=None,
        sa_column=Column(
            DateTime(timezone=True), server_default=text("CURRENT_TIMESTAMP")
        ),
    )

    @staticmethod
    def current() -> "Season":
        """The season being played now, creating the first one if there has never been
        a season before.

        Every Elo the ladder has ever recorded belongs to some season, and that season
        needs a row before a profile can name it. Rather than a migration that has to
        be run once and in the right order, the first caller makes it and files every
        standing that predates seasons under it -- so nobody is treated as owing a
        refresher match for a season that was invented underneath them.
        """
        from src.core.db import open
        from .stat import Stat

        with open() as db:
            season = db.query(Season).order_by(desc(Season.id)).first()
            if season:
                return season

            season = Season(name="Season 1")
            db.add(season)
            db.flush()
            db.query(Stat).where(Stat.season.is_(None)).update({"season": season.id})
            db.refresh(season)
            return season

    @staticmethod
    def all() -> list["Season"]:
        """Every season, newest first — the order a dropdown wants them in."""
        from src.core.db import open

        Season.current()
        with open() as db:
            return db.query(Season).order_by(desc(Season.id)).all()

    @staticmethod
    def find(seasonId) -> "Season | None":
        from src.core.db import open

        if seasonId is None:
            return None
        with open() as db:
            return db.query(Season).where(Season.id == int(seasonId)).first()

    @staticmethod
    def start(name: str) -> "Season":
        """Open a new season, filing everybody's standings under the one that just
        ended.

        The archive is taken here rather than as each player comes back, because a
        player who never plays again still has a season worth showing on their profile.
        Nothing is reset by this: a player's live row keeps the Elo it had and the
        season it belongs to, and only their first ranked match of the new season moves
        them across. Until then they are simply not in the new season's standings,
        which is true -- they have not played in it.
        """
        from src.core.db import open
        from .stat import SeasonStat, Stat

        ending = Season.current()
        # "Season " is the site's, not the manager's, so a name typed with it already
        # on the front does not end up saying it twice.
        label = name.strip()
        if label.lower().startswith("season "):
            label = label[len("season ") :].strip()

        with open() as db:
            # Names are unique, and "26Q3" typed twice is a likelier mistake than a
            # deliberate one. Caught here so the caller gets a sentence rather than an
            # IntegrityError surfacing as a 500 halfway through the archive.
            if db.query(Season).where(Season.name == f"Season {label}").first():
                raise ValueError(f"There is already a Season {label}.")

            db.query(SeasonStat).where(SeasonStat.season == ending.id).delete()
            for row in db.query(Stat).where(Stat.season == ending.id).all():
                db.add(SeasonStat.snapshot(row, ending.id))

            season = Season(name=f"Season {label}")
            db.add(season)
            db.flush()
            db.refresh(season)
            return season

    def window(self) -> tuple:
        """(start, end) — the stretch of time this season covers, either end open.

        A match carries no season of its own, so the only honest way to show a season's
        match history is the matches played while it was the season being played. The
        first season has no start, because everything the ladder recorded before seasons
        existed happened during it.
        """
        from src.core.db import open

        with open() as db:
            earlier = (
                db.query(Season).where(Season.id < self.id).order_by(Season.id).first()
            )
            later = (
                db.query(Season)
                .where(Season.id > self.id)
                .order_by(Season.id)
                .first()
            )
        return (self.created_at if earlier else None, later.created_at if later else None)

    def covers(self, when) -> bool:
        """Whether something that happened at `when` happened during this season."""
        start, end = self.window()
        if when is None:
            return False
        if start is not None and when < start:
            return False
        return end is None or when < end

    @staticmethod
    def carryOver(elo: int) -> int:
        """The Elo a player starts a new season on, given what they finished the last
        one with.

        A season is a soft reset rather than a wipe, and it pulls from both ends. Nobody
        opens above the cap, so the top of the ladder is re-earned every time and the
        badges up there keep meaning this season. Nobody opens below the floor either --
        that is the seed a new account gets, so a season is a fresh start for anybody it
        went badly for rather than a hole they are still in.

        Between those, the drop always bites, so nobody opens where they closed. The one
        thing it will not take is the last of a player's clearance over the seed: someone
        who finished ahead still opens ahead, by `keep` at least.
        """
        floor, keep = settings.season_carry_floor, settings.season_carry_keep
        if elo < floor:
            return floor

        kept = min(elo - settings.season_carry_drop, settings.season_carry_cap)
        return max(kept, floor + keep if elo > floor + keep else floor)
