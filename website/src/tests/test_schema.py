from sqlalchemy import text
from sqlalchemy.dialects import postgresql
from sqlmodel import Session, SQLModel

import src.core.models  # noqa: F401 — registers every table on the metadata
from src.core import db as core_db
from src.core import models as data


def test_steam64_columns_are_bigint():
    """A Steam64 is ~7.6e16, which overflows Postgres INTEGER at 2.1e9.

    SQLModel maps a bare `steam64: int` to INTEGER, so any column declared that
    way silently accepts the model and then fails on the first real insert.
    """
    dialect = postgresql.dialect()
    undersized = [
        f"{table}.{column.name}"
        for table, definition in SQLModel.metadata.tables.items()
        for column in definition.columns
        if "steam64" in column.name.lower() and column.type.compile(dialect) != "BIGINT"
    ]
    assert not undersized, f"steam64 columns too small for a Steam64: {undersized}"


def test_a_column_added_after_its_table_existed_is_created_on_startup():
    """`MatchResults` was created before `MatchResult.matchId` was added to the model,
    and `create_all` never touches a table it did not just create, so the column was
    missing on every database that predated it. `MatchResult.latestFor` is the query
    behind /u/{steam64}, which meant every profile page in production 500'd on
    `column MatchResults.matchId does not exist`.
    """
    with Session(core_db.engine) as db:
        db.execute(text('DROP TABLE "MatchResults"'))
        db.execute(
            text(
                'CREATE TABLE "MatchResults" ('
                "id INTEGER NOT NULL PRIMARY KEY, "
                "region VARCHAR NOT NULL, "
                "gamemode INTEGER NOT NULL, "
                '"startTime" DATETIME NOT NULL, '
                '"endTime" DATETIME NOT NULL, '
                "data JSON NOT NULL)"
            )
        )
        db.commit()

    core_db.addMissingColumns()

    assert data.MatchResult.latestFor(76561198344422592) == []


def test_a_column_added_with_a_default_backfills_the_rows_already_there():
    """`Stats` predates headshots, damageDealt and damageTaken the same way. Adding those
    back as plain NULL columns would only move the failure: the profile page divides
    headshots by kills, and None is not a number.
    """
    steam64 = 76561198344422592
    with Session(core_db.engine) as db:
        db.execute(text('DROP TABLE "Stats"'))
        db.execute(
            text(
                'CREATE TABLE "Stats" ('
                "steam64 BIGINT NOT NULL PRIMARY KEY, "
                "game VARCHAR NOT NULL, "
                "elo INTEGER NOT NULL, "
                "kills INTEGER NOT NULL, "
                "deaths INTEGER NOT NULL, "
                "wins INTEGER NOT NULL, "
                "losses INTEGER NOT NULL)"
            )
        )
        db.execute(
            text(f"""INSERT INTO "Stats" VALUES ({steam64}, 'unt', 300, 12, 4, 3, 1)""")
        )
        db.commit()

    core_db.addMissingColumns()

    with Session(core_db.engine) as db:
        stat = db.query(data.Stat).where(data.Stat.steam64 == steam64).first()
        assert stat.headshots == 0
        assert stat.damageDealt == 0
        assert stat.damageTaken == 0
        assert stat.kills == 12
