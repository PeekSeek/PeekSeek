"""A page names a player from the stored profile, and only asks Steam once it is old."""

from datetime import datetime, timedelta, timezone

from src.core import steam


def _ask(monkeypatch):
    asked = []
    monkeypatch.setattr(
        steam,
        "get_steam_user_data",
        lambda s: asked.append(s) or {"personaname": "FromSteam", "avatarfull": "a"},
    )
    return asked


def test_a_fresh_stored_profile_is_used_without_asking_steam(user_factory, monkeypatch):
    asked = _ask(monkeypatch)
    player = user_factory(
        steam_data={"name": "Stored", "avatar": "b"},
        steam_fetched_at=datetime.now(timezone.utc),
    )

    assert steam.User(player.steam64).name == "Stored"
    assert asked == []


def test_a_day_old_profile_is_asked_about_again(user_factory, monkeypatch):
    asked = _ask(monkeypatch)
    player = user_factory(
        steam_data={"name": "Stored", "avatar": "b"},
        steam_fetched_at=datetime.now(timezone.utc) - timedelta(days=2),
    )

    assert steam.User(player.steam64).name == "FromSteam"
    assert asked == [player.steam64]
