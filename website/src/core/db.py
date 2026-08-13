import traceback

from sqlalchemy import inspect, text
from sqlalchemy.pool import StaticPool
from sqlmodel import Session, SQLModel, create_engine

from .settings import settings

_engine_kwargs: dict = {"echo": False}
if settings.db_url.startswith("sqlite"):
    # Sync endpoints run in Starlette's threadpool, so connections cross threads.
    _engine_kwargs["connect_args"] = {"check_same_thread": False}
    if ":memory:" in settings.db_url:
        # One shared connection, or each pool checkout gets its own empty database.
        _engine_kwargs["poolclass"] = StaticPool

engine = create_engine(settings.db_url, **_engine_kwargs)


class open:
    """Context manager that yields a SQLModel Session with auto-commit/rollback."""

    def __enter__(self) -> Session:
        self._session = Session(engine, expire_on_commit=False)
        self._session.rollback()
        return self._session

    def __exit__(self, exc_type, exc_val, exc_tb):
        if exc_type is not None:
            traceback.print_tb(exc_tb)
        try:
            self._session.commit()
        except Exception:
            self._session.rollback()
            traceback.print_exc()
        finally:
            self._session.close()


def _defaultClause(column) -> str:
    """`DEFAULT <value>` for a column whose model declares one, so rows already in the
    table get that value rather than NULL. `Stats.headshots` is why this matters: the
    profile page divides by it, and `None / 1` raises just as loudly as a missing column.
    """
    default = getattr(column.default, "arg", None)
    if default is None or callable(default):
        return ""
    return f" DEFAULT {column.type.literal_processor(dialect=engine.dialect)(default)}"


def addMissingColumns() -> None:
    """Add columns a model has gained since its table was first created.

    `create_all` only ever creates whole tables. A column added to a model whose table
    already exists is silently skipped, and every query naming it then fails at runtime.
    `MatchResults` was created before `MatchResult.matchId` existed, which is why every
    profile page in production 500'd on `column MatchResults.matchId does not exist`
    long after the model looked correct. `Stats` is three columns behind for the same
    reason.

    Columns are added nullable and without their foreign keys, since existing rows have
    nothing to point at and no value to be checked against. A database created from
    scratch still gets the full definition straight from `create_all`. Nothing here
    drops, renames or retypes anything, so a change needing any of those still has to be
    done by hand.
    """
    inspector = inspect(engine)
    statements = []
    for table in SQLModel.metadata.sorted_tables:
        if not inspector.has_table(table.name):
            continue
        present = {column["name"] for column in inspector.get_columns(table.name)}
        for column in table.columns:
            if column.name not in present:
                statements.append(
                    f'ALTER TABLE "{table.name}" ADD COLUMN "{column.name}" '
                    f"{column.type.compile(engine.dialect)}{_defaultClause(column)}"
                )

    if not statements:
        return

    with engine.begin() as connection:
        for statement in statements:
            print(f"Schema: {statement}")
            connection.execute(text(statement))


from . import models  # noqa: F401, E402 — ensures all tables are registered
from sqlalchemy.orm import configure_mappers

configure_mappers()
SQLModel.metadata.create_all(engine)
addMissingColumns()

if settings.bootstrap_admin:
    models.User.bootstrapAdmin(settings.bootstrap_admin)

if settings.bootstrap_servers:
    models.AuthorizedServer.bootstrap(settings.bootstrap_servers)
