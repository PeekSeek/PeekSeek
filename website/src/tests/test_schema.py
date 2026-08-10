from sqlalchemy.dialects import postgresql
from sqlmodel import SQLModel

import src.core.models  # noqa: F401 — registers every table on the metadata


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
        if "steam64" in column.name.lower()
        and column.type.compile(dialect) != "BIGINT"
    ]
    assert not undersized, f"steam64 columns too small for a Steam64: {undersized}"
