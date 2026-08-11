import traceback

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


from . import models  # noqa: F401, E402 — ensures all tables are registered
from sqlalchemy.orm import configure_mappers

configure_mappers()
SQLModel.metadata.create_all(engine)

if settings.bootstrap_admin:
    models.User.bootstrapAdmin(settings.bootstrap_admin)

if settings.bootstrap_servers:
    models.AuthorizedServer.bootstrap(settings.bootstrap_servers)
