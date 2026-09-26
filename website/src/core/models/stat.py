from __future__ import annotations

from time import time
from typing import Optional
from uuid import uuid4

from sqlalchemy import BigInteger, Column, ForeignKey, Integer, desc
from sqlmodel import Field, Relationship, SQLModel

from src.core.settings import settings

from .rank import Rank
from .season import Season

_top10Cache: list = [None, 0]
_topIdsCache: list = [None, 0]


class _Entry:
    """One row of a leaderboard: the player, their Elo, the badge it earns and the ring
    they wear. Built the same way from a live standing or an archived one."""

    def __init__(self, **fields):
        for key, value in fields.items():
            setattr(self, key, value)


def _entries(rows: list) -> list[_Entry]:
    """Leaderboard rows for standings already in Elo order.

    One query for every ring on the page, and one Steam lookup per player -- which is
    the expensive half, and the reason nothing outside a leaderboard calls this.
    """
    from .user import User

    rings = User.ringsFor([row.steam64 for row in rows])
    return [
        _Entry(
            u=User.getSteamData(row.steam64),
            elo=row.elo,
            rank=Rank.forElo(row.elo, position),
            ring=rings.get(str(row.steam64)),
        )
        for position, row in enumerate(rows, 1)
    ]


class Stat(SQLModel, table=True):
    __tablename__ = "Stats"

    steam64: int = Field(
        sa_column=Column(BigInteger, ForeignKey("Users.steam64"), primary_key=True),
    )
    game: str = Field(default="unt", nullable=False)
    # Which season this standing is for. A row left on a season that is over is a player
    # who has not come back yet: they keep last season's Elo until their first ranked
    # match carries them across, and until then they are in no season's standings.
    season: Optional[int] = Field(default=None, foreign_key="Seasons.id", nullable=True)
    # The ladder: the number players see, rank on and grind. Seasons reset it.
    elo: int = Field(default=300, nullable=False)
    # The hidden estimate matchmaking balances on, and how unsure it still is. Separate
    # from `elo` so the ladder can be a grind without the matchmaker believing it: skill
    # moves on wins and losses alone and no season touches it. Empty on a standing that
    # predates it, which getSkill reads as its Elo.
    skill: Optional[float] = Field(default=None, nullable=True)
    skillDeviation: Optional[float] = Field(default=None, nullable=True)
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
                # A first-ever standing starts in the season being played, not in one
                # it has to be carried out of.
                stats = Stat(
                    steam64=steam64, game=game, elo=300, season=Season.current().id
                )
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
        """The current season's top ten, ready to draw."""
        from src.core.db import open

        if _top10Cache[0] is not None and time() - _top10Cache[1] < 60:
            return _top10Cache[0]

        season = Season.current().id
        with open() as db:
            rows = (
                db.query(Stat)
                .where(Stat.season == season)
                .order_by(desc(Stat.elo), desc(Stat.wins), desc(Stat.kills))
                .limit(10)
                .all()
            )

        _top10Cache[0] = _entries(rows)
        _top10Cache[1] = time()
        return _top10Cache[0]

    @staticmethod
    def leaderboard(seasonId=None) -> list:
        """The top ten of any season.

        The one being played comes from the live table and is cached, because every
        visitor asks for it; a finished one is read straight out of its archive, which
        never changes again.
        """
        current = Season.current()
        if seasonId is None or int(seasonId) == current.id:
            return Stat.getTop10()
        return SeasonStat.top10(int(seasonId))

    @property
    def rank(self) -> Rank:
        return Rank.forElo(self.elo, Stat.ladderPosition(self.steam64))

    @staticmethod
    def forgetTop() -> None:
        """Drop both cached views of the top of the ladder.

        There are two because a position and a rendered leaderboard row cost very
        different things to produce, and one call to clear them because anybody who
        wants the top rebuilt wants it rebuilt in both.
        """
        _top10Cache[0], _top10Cache[1] = None, 0
        _topIdsCache[0], _topIdsCache[1] = None, 0

    @staticmethod
    def topIds() -> list[int]:
        """The steam64s of the top ten, in order. One query, and no Steam lookups.

        A position is a database fact; the leaderboard's need for names and avatars to
        draw beside it is not. getTop10 has to have both, so it resolves ten Steam
        profiles and costs a blocking call each -- which is why anything that only wants
        to know whether somebody is top three asks this instead. Every page draws a
        ring, and only one of them draws the leaderboard.
        """
        from src.core.db import open

        if _topIdsCache[0] is not None and time() - _topIdsCache[1] < 60:
            return _topIdsCache[0]

        season = Season.current().id
        with open() as db:
            rows = (
                db.query(Stat.steam64)
                .where(Stat.season == season)
                .order_by(desc(Stat.elo), desc(Stat.wins), desc(Stat.kills))
                .limit(10)
                .all()
            )
        _topIdsCache[0] = [int(row[0]) for row in rows]
        _topIdsCache[1] = time()
        return _topIdsCache[0]

    @staticmethod
    def ladderPosition(steam64) -> int | None:
        """This player's 1-based place on the ladder, or None if they are not near the
        top of it.

        Only the top is worth answering for, because the two things position decides are
        whether a capped tier is held and whether a card wears a medal ring, and both
        run out after a handful of places.
        """
        for position, held in enumerate(Stat.topIds(), 1):
            if held == int(steam64):
                return position
        return None

    @staticmethod
    def worldPosition(steam64) -> int | None:
        """This player's place on the whole ladder, counted rather than looked up.

        ladderPosition only answers for the top ten, which is all a badge needs. A
        player sitting under a capped tier needs the real number: their bar is full and
        the only thing between them and the tier above is the people in front of them.
        Counted on demand because it is asked for on one page, by one player, and only
        once they are high enough for it to mean anything.
        """
        from src.core.db import open

        season = Season.current().id
        with open() as db:
            row = db.query(Stat).where(Stat.steam64 == steam64).first()
            if not row:
                return None
            return (
                db.query(Stat).where(Stat.season == season, Stat.elo > row.elo).count()
                + 1
            )

    @staticmethod
    def rankFor(steam64) -> Rank:
        """The badge this player wears, from a steam64 alone.

        For callers holding no Stat row -- the sidebar draws the same card as the
        leaderboard and only ever has the signed-in user's id. Two cheap queries and no
        Steam: this runs on every render of every page.
        """
        return Rank.forElo(Stat.getElo(steam64), Stat.ladderPosition(steam64))

    @staticmethod
    def startSeason(steam64) -> None:
        """Carry a player into the season being played, if they are not in it yet.

        This is the refresher: a returning player's first ranked match of a season is
        played on their carried-over Elo, so the season opens with a real result rather
        than a number that changed while they were away. Everything counted per season
        goes back to zero -- the match they are about to play is the first of the new
        one, not the last of the old.

        Called before the match is scored, so the Elo it is scored against is already
        the season's.
        """
        from src.core.db import open

        season = Season.current().id
        with open() as db:
            row = db.query(Stat).where(Stat.steam64 == steam64).first()
            if not row or row.season == season:
                return
            db.query(Stat).where(Stat.steam64 == steam64).update(
                {
                    "season": season,
                    "elo": Season.carryOver(row.elo),
                    "kills": 0,
                    "deaths": 0,
                    "wins": 0,
                    "losses": 0,
                    "headshots": 0,
                    "hits": 0,
                    "headshotHits": 0,
                    "damageDealt": 0,
                    "damageTaken": 0,
                }
            )
        Stat.forgetTop()

    @staticmethod
    def forSeason(steam64, seasonId=None):
        """This player's standing in one season: the live row for the season being
        played, the archived one for any season already finished."""
        from src.core.db import open

        current = Season.current()
        with open() as db:
            if seasonId is None or int(seasonId) == current.id:
                row = db.query(Stat).where(Stat.steam64 == steam64).first()
                return row if row and row.season == current.id else None
            return (
                db.query(SeasonStat)
                .where(
                    SeasonStat.steam64 == steam64,
                    SeasonStat.season == int(seasonId),
                )
                .first()
            )

    @staticmethod
    def getElo(steam64, game: str = "unt") -> int:
        from src.core.db import open

        with open() as db:
            r = db.query(Stat).where(Stat.steam64 == steam64, Stat.game == game).first()
            return r.elo if r else 300

    @staticmethod
    def getSkill(steam64, game: str = "unt") -> tuple[float, float]:
        """(skill, deviation) for this player.

        A standing from before skill existed is seeded from its Elo -- the best guess
        there is -- with a deviation wide enough that its first few matches correct it.
        Nobody at all gets the new-account seed and the widest deviation.
        """
        from src.core.db import open

        with open() as db:
            r = db.query(Stat).where(Stat.steam64 == steam64, Stat.game == game).first()
        if not r:
            return 300.0, float(settings.new_deviation)
        if r.skill is None:
            return float(r.elo), float(settings.seed_deviation)
        return r.skill, r.skillDeviation

    @staticmethod
    def setSkill(steam64, skill: float, deviation: float) -> None:
        """Overwrite a player's skill. Set rather than added, unlike everything
        Stat.append counts, because a deviation is not something that sums."""
        from src.core.db import open

        with open() as db:
            db.query(Stat).where(Stat.steam64 == steam64).update(
                {"skill": skill, "skillDeviation": deviation}
            )


class SeasonStat(SQLModel, table=True):
    """One player's finished season, frozen.

    A copy rather than a view: the live row is reset when its owner comes back, so the
    only place a finished season survives is here. Taken for everybody the moment a
    season ends, because a player who never returns still has a season worth showing.
    """

    __tablename__ = "SeasonStats"

    steam64: int = Field(
        sa_column=Column(BigInteger, ForeignKey("Users.steam64"), primary_key=True),
    )
    season: int = Field(
        sa_column=Column(Integer, ForeignKey("Seasons.id"), primary_key=True),
    )
    elo: int = Field(default=300, nullable=False)
    kills: int = Field(default=0, nullable=False)
    deaths: int = Field(default=0, nullable=False)
    wins: int = Field(default=0, nullable=False)
    losses: int = Field(default=0, nullable=False)
    headshots: int = Field(default=0, nullable=False)
    damageDealt: int = Field(default=0, nullable=False)
    damageTaken: int = Field(default=0, nullable=False)
    hits: int = Field(default=0, nullable=False)
    headshotHits: int = Field(default=0, nullable=False)

    @property
    def headshotPercent(self) -> float:
        """Share of landed shots that hit the head, worked out the same way the live
        standing does it."""
        if self.hits:
            return round(self.headshotHits / self.hits * 100, 1)
        if self.kills:
            return round(self.headshots / self.kills * 100, 1)
        return 0.0

    @property
    def rank(self) -> Rank:
        """The badge this season ended on. No ladder position, because the seats a
        capped tier hands out belong to the season being played, not a finished one."""
        return Rank.forElo(self.elo)

    @staticmethod
    def snapshot(row: "Stat", seasonId: int) -> "SeasonStat":
        return SeasonStat(
            steam64=row.steam64,
            season=seasonId,
            elo=row.elo,
            kills=row.kills,
            deaths=row.deaths,
            wins=row.wins,
            losses=row.losses,
            headshots=row.headshots,
            damageDealt=row.damageDealt,
            damageTaken=row.damageTaken,
            hits=row.hits,
            headshotHits=row.headshotHits,
        )

    @staticmethod
    def top10(seasonId: int) -> list:
        from src.core.db import open

        with open() as db:
            rows = (
                db.query(SeasonStat)
                .where(SeasonStat.season == seasonId)
                .order_by(
                    desc(SeasonStat.elo),
                    desc(SeasonStat.wins),
                    desc(SeasonStat.kills),
                )
                .limit(10)
                .all()
            )
        return _entries(rows)


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
