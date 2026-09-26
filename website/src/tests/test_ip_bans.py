"""A ban keeps every account seen on the banned player's addresses out of queues and
matches, and nothing else: the site, the banner and the admin drawer stay per account.
"""

import pytest

from src.core import db as core_db
from src.core import models as data


@pytest.fixture
def gameserver(client):
    data.AuthorizedServer.bootstrap({"testclient": "paris"})
    return client


def _ban(steam64, endTime=data.UserBan.PERMANENT):
    with core_db.open() as db:
        db.add(data.UserBan(steam64=steam64, endTime=endTime, reason="Cheating"))


def _seen(steam64, address):
    with core_db.open() as db:
        data.UserIP.remember(db, steam64, address)


def _barred(*steam64s):
    with core_db.open() as db:
        return set(data.UserBan.barring(steam64s, db))


def test_an_account_on_a_banned_address_is_barred(user_factory):
    cheat, alt, stranger = user_factory(), user_factory(), user_factory()
    _seen(cheat.steam64, "1.2.3.4")
    _seen(alt.steam64, "1.2.3.4")
    _seen(stranger.steam64, "5.6.7.8")
    _ban(cheat.steam64)

    assert _barred(cheat.steam64, alt.steam64, stranger.steam64) == {
        cheat.steam64,
        alt.steam64,
    }


def test_an_expired_ban_bars_nobody(user_factory):
    cheat, alt = user_factory(), user_factory()
    _seen(cheat.steam64, "1.2.3.4")
    _seen(alt.steam64, "1.2.3.4")
    _ban(cheat.steam64, endTime=1)

    assert _barred(cheat.steam64, alt.steam64) == set()


def test_the_banner_stays_on_the_banned_account_only(user_factory):
    cheat, alt = user_factory(), user_factory()
    _seen(cheat.steam64, "1.2.3.4")
    _seen(alt.steam64, "1.2.3.4")
    _ban(cheat.steam64)

    assert data.UserBan.activeFor(alt.steam64) is None


def test_the_lobby_logs_the_ip_and_refuses_the_alt(gameserver, user_factory):
    cheat = user_factory()
    _seen(cheat.steam64, "1.2.3.4")
    _ban(cheat.steam64)
    alt = 76561198000009999

    r = gameserver.put(
        f"/server/v1/players/{alt}/queue", params={"gamemodes": 1, "ip": "1.2.3.4"}
    )

    assert r.status_code == 200
    assert r.json()["ban"]["reason"] == "Cheating"
    with core_db.open() as db:
        assert db.query(data.UserHB).where(data.UserHB.steam64 == alt).first() is None
        assert db.query(data.UserIP).where(data.UserIP.steam64 == alt).count() == 1


def test_an_ip_is_logged_once(user_factory):
    player = user_factory()
    _seen(player.steam64, "1.2.3.4")
    _seen(player.steam64, "1.2.3.4")

    with core_db.open() as db:
        assert db.query(data.UserIP).count() == 1


def test_a_custom_match_refuses_a_barred_player(client, login, user_factory):
    owner, cheat, alt = user_factory(), user_factory(), user_factory()
    owner.setPermission("premium", True)
    _seen(cheat.steam64, "1.2.3.4")
    _seen(alt.steam64, "1.2.3.4")
    _ban(cheat.steam64)
    login(client, owner)

    r = client.post(
        "/match/custom",
        json={
            "teams": {"a": [str(owner.steam64)], "b": [str(alt.steam64)]},
            "region": "paris",
            "gamemode": 0,
            "ballistics": True,
            "map": "Alpha Valley",
            "movement": True,
        },
    )

    assert r.status_code == 400
    assert str(alt.steam64) in r.text
    assert data.Match.currentFor(owner.steam64) is None


def test_a_match_can_never_be_built_with_a_barred_player(user_factory):
    cheat = user_factory()
    _ban(cheat.steam64)

    with pytest.raises(AssertionError):
        data.Match(gamemode=0, teams=[{"name": "a", "users": [str(cheat.steam64)]}])
