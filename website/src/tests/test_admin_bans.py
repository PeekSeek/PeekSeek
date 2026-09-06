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


def test_the_drawer_shows_an_active_ban_and_the_history(
    client, login, admin, user_factory
):
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


def test_banning_from_the_dashboard_uses_the_same_path(
    client, login, admin, user_factory
):
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


def test_overturning_keeps_the_row_and_says_who_lifted_it(
    client, login, admin, user_factory
):
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


def test_ban_chips_come_back_for_a_whole_card_at_once(
    client, login, admin, user_factory
):
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


def test_a_banned_player_still_gets_the_site(client, login, user_factory):
    """A ban stops somebody playing, not reading. They still need their profile, the
    rules, and the link to appeal, all of which the old wall blocked."""
    player = user_factory()
    ban(player.steam64, reason="Toxicity", endTime=int(time()) + 6 * 86400)
    login(client, player)

    r = client.get("/fairplay")

    assert r.status_code == 200
    assert "You are currently banned for" in r.text
    assert "Toxicity" in r.text
    assert "another 5 days" in r.text or "another 6 days" in r.text


def test_an_unbanned_player_sees_no_banner(client, login, user_factory):
    player = user_factory()
    login(client, player)

    assert "You are currently banned" not in client.get("/fairplay").text


def test_a_permanent_ban_says_permanently(client, login, user_factory):
    player = user_factory()
    ban(player.steam64, reason="Cheating")
    login(client, player)

    body = client.get("/fairplay").text

    assert "You are currently banned for" in body
    assert "permanently" in body


def test_ban_rows_are_admin_only(client, login, admin, user_factory):
    """Somebody else's ban record is not public: an ordinary visitor sees the profile
    exactly as it was before any of this existed."""
    target = user_factory()
    ban(target.steam64, reason="Cheating")

    login(client, user_factory())
    assert "Cheating" not in client.get(f"/u/{target.steam64}").text

    login(client, admin)
    assert "Cheating" in client.get(f"/u/{target.steam64}").text


def test_only_an_admin_gets_the_ban_button(client, login, admin, user_factory):
    target = user_factory()

    login(client, user_factory())
    assert ">Ban" not in client.get(f"/u/{target.steam64}").text

    login(client, admin)
    assert ">Ban" in client.get(f"/u/{target.steam64}").text


def test_a_logged_out_visitor_gets_no_moderation(client, user_factory):
    target = user_factory()

    body = client.get(f"/u/{target.steam64}").text

    assert ">Ban" not in body
    assert "moderation.js" not in body


def test_a_ban_row_says_whether_it_is_still_in_force(
    client, login, admin, user_factory
):
    target = user_factory()
    ban(target.steam64, endTime=int(time()) - 60, reason="Toxicity")
    ban(target.steam64, reason="Cheating")
    login(client, admin)

    body = client.get(f"/u/{target.steam64}").text

    assert "Active" in body
    assert "Expired" in body


def test_a_profile_with_no_bans_draws_no_ban_list(client, login, admin, user_factory):
    """Nothing is drawn where there is nothing to say."""
    target = user_factory()
    login(client, admin)

    assert "banlist" not in client.get(f"/u/{target.steam64}").text


def event(owner, startsIn: int):
    """An event whose first queue window opens `startsIn` seconds from now."""
    start = int(time()) + startsIn
    with Session(engine) as session:
        row = data.Event(
            ownerSteam64=owner.steam64,
            name="Test Cup",
            timeCreated=int(time()),
            gamemode=0,
            data={
                "meta": {
                    "queueTimes": [[start, start + 3600]],
                    "maxPlayersPerTeam": 2,
                    "maxMatchesPerTeam": 5,
                }
            },
        )
        session.add(row)
        session.commit()
        session.refresh(row)
        return row.id


def test_a_ban_ending_before_the_event_does_not_block_signup(
    client, login, user_factory
):
    """Somebody banned for a week can enter an event three weeks out. Holding the whole
    event against a ban that will have expired punishes them twice."""
    player = user_factory()
    eventId = event(player, startsIn=21 * 86400)
    ban(player.steam64, endTime=int(time()) + 7 * 86400, reason="Toxicity")
    login(client, player)

    r = client.post(f"/esports/signup?eventId={eventId}&team=Late+Bloomers")

    assert r.status_code == 200
    with opendb() as db:
        assert db.query(data.EventSignup).count() == 1


def test_a_ban_running_past_the_start_blocks_signup(client, login, user_factory):
    player = user_factory()
    eventId = event(player, startsIn=2 * 86400)
    ban(player.steam64, endTime=int(time()) + 7 * 86400, reason="Toxicity")
    login(client, player)

    r = client.post(f"/esports/signup?eventId={eventId}&team=Too+Soon")

    assert r.status_code == 403
    assert "after this event starts" in r.text
    with opendb() as db:
        assert db.query(data.EventSignup).count() == 0


def test_a_permanent_ban_blocks_every_event(client, login, user_factory):
    player = user_factory()
    eventId = event(player, startsIn=365 * 86400)
    ban(player.steam64, reason="Cheating")
    login(client, player)

    assert (
        client.post(f"/esports/signup?eventId={eventId}&team=Nope").status_code == 403
    )


def test_an_unbanned_player_signs_up_fine(client, login, user_factory):
    player = user_factory()
    eventId = event(player, startsIn=2 * 86400)
    login(client, player)

    assert (
        client.post(f"/esports/signup?eventId={eventId}&team=Fine").status_code == 200
    )
