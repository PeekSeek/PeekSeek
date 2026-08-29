"""The moderation drawer's endpoints, and the page a banned player actually sees."""

from time import time

import pytest
from sqlmodel import Session

from src.core import models as data
from src.core.db import engine, open as opendb


@pytest.fixture(autouse=True)
def _noDiscord(monkeypatch):
    monkeypatch.setattr(
        "src.core.notify.send_discord_channel", lambda *a, **k: True, raising=False
    )


@pytest.fixture
def admin(user_factory):
    from src.core.settings import settings

    bit = 2 ** settings.permissions.index("admin")
    return user_factory(permissions=bit)


def ban(steam64, endTime=None, reason="Cheating"):
    with Session(engine) as session:
        row = data.UserBan(
            steam64=steam64,
            endTime=endTime if endTime is not None else data.UserBan.PERMANENT,
            reason=reason,
        )
        session.add(row)
        session.commit()
        session.refresh(row)
        return row.id


def test_the_drawer_gets_everything_in_one_call(client, login, admin, user_factory):
    """It has to: the blast radius must be on screen before an admin can decide."""
    target = user_factory()
    login(client, admin)

    body = client.get(f"/admin/player/{target.steam64}").json()

    assert body["steam64"] == str(target.steam64)
    assert body["ban"] is None
    assert body["history"] == []
    assert "preview" in body and "matches" in body["preview"]
    assert set(body["presets"]) == {"cheating", "matchfixing", "toxicity"}


def test_the_drawer_shows_an_active_ban_and_the_history(client, login, admin, user_factory):
    target = user_factory()
    ban(target.steam64, endTime=int(time()) - 60, reason="Toxicity")
    ban(target.steam64, reason="Cheating")
    login(client, admin)

    body = client.get(f"/admin/player/{target.steam64}").json()

    assert body["ban"]["reason"] == "Cheating"
    assert body["ban"]["permanent"] is True
    assert len(body["history"]) == 2


def test_a_non_admin_cannot_open_the_drawer(client, login, user_factory):
    target = user_factory()
    login(client, user_factory())

    assert client.get(f"/admin/player/{target.steam64}").status_code == 401


def test_banning_from_the_dashboard_uses_the_same_path(client, login, admin, user_factory):
    target = user_factory()
    login(client, admin)

    r = client.post(
        "/admin/ban",
        json={"steam64": str(target.steam64), "preset": "toxicity", "note": "slurs"},
    )

    assert r.status_code == 200
    stored = data.UserBan.activeFor(target.steam64)
    assert stored.reason == "Toxicity"
    # The note records who did it without the admin having to type their own name.
    assert stored.moderatorNote.startswith("by ")
    assert stored.moderatorNote.endswith("slurs")


def test_a_custom_ban_takes_its_days(client, login, admin, user_factory):
    target = user_factory()
    login(client, admin)

    client.post(
        "/admin/ban",
        json={
            "steam64": str(target.steam64),
            "preset": "custom",
            "days": "3",
            "reason": "Ban evasion",
            "note": "alt of 7656",
        },
    )

    stored = data.UserBan.activeFor(target.steam64)
    assert stored.reason == "Ban evasion"
    assert abs(stored.endTime - (time() + 3 * 86400)) < 10


def test_overturning_keeps_the_row_and_says_who_lifted_it(client, login, admin, user_factory):
    """Deleting it would show an admin a clean record for a repeat offender."""
    target = user_factory()
    banId = ban(target.steam64)
    login(client, admin)

    r = client.post("/admin/unban", json={"id": banId})

    assert r.status_code == 200
    assert data.UserBan.activeFor(target.steam64) is None
    with opendb() as db:
        row = db.query(data.UserBan).where(data.UserBan.id == banId).first()
    assert row is not None
    assert row.endTime == 0
    assert "overturned by" in row.moderatorNote


def test_ban_chips_come_back_for_a_whole_card_at_once(client, login, admin, user_factory):
    banned = user_factory()
    prior = user_factory()
    clean = user_factory()
    ban(banned.steam64)
    ban(prior.steam64, endTime=int(time()) - 60, reason="Toxicity")
    login(client, admin)

    ids = ",".join(str(u.steam64) for u in (banned, prior, clean))
    body = client.get(f"/admin/bans?steam64s={ids}").json()

    assert body[str(banned.steam64)] == {"banned": True, "prior": 0}
    assert body[str(prior.steam64)] == {"banned": False, "prior": 1}
    # A clean player is left out entirely rather than sent as an empty state.
    assert str(clean.steam64) not in body


def test_a_banned_player_sees_the_ban_page_not_an_error_page(client, login, user_factory):
    player = user_factory()
    ban(player.steam64, reason="Cheating")
    login(client, player)

    r = client.get("/fairplay")

    assert r.status_code == 403
    assert "You are banned" in r.text
    assert "Cheating" in r.text
    # The old page called this a "Cooldown", which is not what a permanent ban is.
    assert "Cooldown" not in r.text


def test_the_ban_page_says_when_it_ends(client, login, user_factory):
    player = user_factory()
    ban(player.steam64, endTime=int(time()) + 86400, reason="Toxicity")
    login(client, player)

    assert "Permanent" not in client.get("/fairplay").text


def test_the_ban_is_checked_on_every_request(client, login, user_factory):
    """It used to be checked once a minute, which let a banned player browse normally
    between checks and hit the ban page at apparently random moments."""
    player = user_factory()
    login(client, player)
    assert client.get("/fairplay").status_code == 200

    ban(player.steam64)

    assert client.get("/fairplay").status_code == 403
