"""Test bootstrap — runs *before* any src import."""

import base64
import json
import os
import sys
from pathlib import Path
from uuid import uuid4

# Point the DB at an in-memory SQLite so tests don't touch Postgres.
os.environ["DB"] = "sqlite:///:memory:"
os.environ.setdefault("STEAM_API_KEY", "test-steam-key")
os.environ.setdefault("BOT_TOKEN", "test-bot-token")
os.environ.setdefault("DISCORD_CLIENT_SECRET", "test-discord-secret")

# Ensure the repo root is on sys.path
ROOT = Path(__file__).resolve().parent.parent.parent
sys.path.insert(0, str(ROOT))

import pytest  # noqa: E402
from itsdangerous import TimestampSigner  # noqa: E402

from src.core import db as core_db  # noqa: E402
from src.core import models as data  # noqa: E402
from src.core.settings import settings  # noqa: E402
from sqlmodel import SQLModel, Session  # noqa: E402


@pytest.fixture(scope="session", autouse=True)
def _create_schema():
    SQLModel.metadata.create_all(core_db.engine)
    yield
    SQLModel.metadata.drop_all(core_db.engine)


@pytest.fixture(autouse=True)
def _clean_tables():
    """Wipe tables between tests so each one starts fresh."""
    yield
    with Session(core_db.engine) as s:
        for table in reversed(SQLModel.metadata.sorted_tables):
            s.execute(table.delete())
        s.commit()


@pytest.fixture
def user_factory():
    created = []

    def _make(**overrides):
        steam64 = overrides.pop("steam64", 76561197960287930 + len(created))
        uuid = overrides.pop("uuid", str(uuid4()))
        user = data.User(steam64=steam64, uuid=uuid, **overrides)
        with Session(core_db.engine) as s:
            s.add(user)
            s.commit()
            s.refresh(user)
            s.expunge(user)
        created.append(user)
        return user

    return _make


@pytest.fixture
def client():
    from fastapi.testclient import TestClient
    from src.app import app

    return TestClient(app, follow_redirects=False)


@pytest.fixture
def login():
    """Give a client a signed session cookie, exactly as SessionMiddleware writes it."""

    def _login(client, user, **extra_session):
        # Scope the cookie exactly as a response from the app would, or the jar
        # keeps ours *and* the app's as two separate entries: logging in as a
        # second user would not switch identity, and a logout clearing the
        # session would delete only the app's copy and leave ours signed in.
        client.cookies.clear()
        payload = {"uuid": user.uuid, **extra_session}
        signer = TimestampSigner(settings.secret_key)
        encoded = base64.b64encode(json.dumps(payload).encode())
        client.cookies.set(
            "session",
            signer.sign(encoded).decode(),
            domain="testserver.local",
            path="/",
        )

    return _login


@pytest.fixture
def party_factory():
    """A real Parties row, returning its id.

    Queue heartbeats carry partyId as a foreign key, so a made-up number is a row
    the database will not accept — and since these ids only ever come from a party
    the site actually created, a test using one it invented is testing a state that
    cannot happen.
    """

    def _make(*members):
        from time import time

        with Session(core_db.engine) as s:
            party = data.Party(
                steam64=members[0].steam64,
                lastSeen=int(time()),
                data={"members": [str(m.steam64) for m in members], "invites": []},
            )
            s.add(party)
            s.commit()
            return party.id

    return _make
