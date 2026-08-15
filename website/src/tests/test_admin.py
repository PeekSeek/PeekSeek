"""The admin match list: what /admin/matches puts on a card, and who may ask for it."""

from time import time

import pytest
from sqlmodel import Session

from src.core import db as core_db
from src.core import models as data
from src.core import steam

GAMEMODE = 0


@pytest.fixture(autouse=True)
def _no_steam_server_lookup(monkeypatch):
    """Steam is not reachable from a test, and a card must render without it anyway."""
    monkeypatch.setattr(steam.Server, "describe", staticmethod(lambda code: None))


@pytest.fixture
def admin_client(client, login, user_factory):
    """Logged in as an admin, and allow-listed as a game server.

    Both on the one client because these tests drive a match to its end through the real
    `/server/v1/*` result call rather than writing the result row by hand: what the card
    shows for a finished match depends on what that call leaves behind.
    """
    data.AuthorizedServer.bootstrap({"testclient": "paris"})

    user = user_factory()
    with Session(core_db.engine) as s:
        s.query(data.User).where(data.User.steam64 == user.steam64).update(
            {"permissions": 1}
        )
        s.commit()
    login(client, user)
    return client


def _match(first, second, **overrides) -> int:
    with core_db.open() as db:
        match = data.Match(
            gamemode=GAMEMODE,
            teams=[
                {"name": "team_one", "users": [str(first.steam64)]},
                {"name": "team_two", "users": [str(second.steam64)]},
            ],
            matchSettings={"map": "Alpha Valley", "ballistics": True, "movement": False},
        )
        for field, value in overrides.items():
            setattr(match, field, value)
        db.add(match)
        db.flush()
        return match.id


def test_the_match_list_is_admin_only(client, login, user_factory):
    login(client, user_factory())
    assert client.get("/admin/matches").status_code == 401


def test_a_live_match_reports_its_roster_from_the_match_not_the_snapshot(
    admin_client, user_factory
):
    """A player who never connected is on the match and in no snapshot. Reading the
    roster off the snapshot is what used to leave a no-show off the card entirely, which
    reads as a match that was never made rather than one nobody turned up to."""
    first, second = user_factory(), user_factory()
    _match(first, second)

    card = admin_client.get("/admin/matches").json()["matches"][0]

    assert card["over"] is False
    assert card["ending"] is None
    assert [t["name"] for t in card["teams"]] == ["team_one", "team_two"]
    assert [p["steam64"] for t in card["teams"] for p in t["players"]] == [
        str(first.steam64),
        str(second.steam64),
    ]


def test_live_numbers_come_off_the_snapshot(admin_client, user_factory):
    first, second = user_factory(), user_factory()
    matchId = _match(first, second)
    data.Match.setLiveData(
        matchId,
        {
            "round": 2,
            "teams": [
                {
                    "teamName": "team_one",
                    "wins": 1,
                    "losses": 0,
                    "users": {
                        str(first.steam64): {
                            "name": "Death image",
                            "kills": 3,
                            "deaths": 2,
                            "headshots": 3,
                            "state": "alive",
                            "health": 70,
                        }
                    },
                },
                {
                    "teamName": "team_two",
                    "wins": 0,
                    "losses": 1,
                    "users": {
                        str(second.steam64): {
                            "name": "RioDash",
                            "kills": 2,
                            "deaths": 3,
                            "headshots": 0,
                            "state": "offline",
                            "health": 0,
                        }
                    },
                },
            ],
        },
    )

    card = admin_client.get("/admin/matches").json()["matches"][0]
    one, two = card["teams"]

    assert card["round"] == 2
    assert (one["wins"], two["wins"]) == (1, 0)
    assert one["players"][0] == {
        **one["players"][0],
        "name": "Death image",
        "kills": 3,
        "deaths": 2,
        "headshots": 3,
        "state": "alive",
        "health": 70,
        "connected": True,
    }
    assert two["players"][0]["connected"] is False


def test_a_scored_match_keeps_its_round_score_and_elo(admin_client, user_factory):
    """Scoring a match wipes the live snapshot, so the round tally is copied into the
    result row as it goes. Without that a finished match knows who won and not by how
    much, and 3-0 and 3-2 read identically forever after."""
    first, second = user_factory(), user_factory()
    matchId = _match(first, second)
    data.Match.setLiveData(
        matchId,
        {
            "round": 4,
            "teams": [
                {"teamName": "team_one", "wins": 3, "losses": 1, "users": {}},
                {"teamName": "team_two", "wins": 1, "losses": 3, "users": {}},
            ],
        },
    )

    r = admin_client.post(
        f"/server/v1/matches/{matchId}/result",
        json={
            "played": True,
            "winningTeam": "team_one",
            "players": [
                {"steam64": str(first.steam64), "won": True, "kills": 5, "deaths": 2},
                {"steam64": str(second.steam64), "won": False, "kills": 2, "deaths": 5},
            ],
        },
    )
    assert r.status_code == 200, r.text

    card = admin_client.get("/admin/matches").json()["matches"][0]
    one, two = card["teams"]

    assert card["over"] is True
    assert card["ending"] == "scored"
    # Not merely present: MatchResult stores UTC with no timezone on it, and reading
    # that back as local time put every match's end before its own start.
    assert card["endedAt"] >= card["startedAt"]
    assert (one["wins"], two["wins"]) == (3, 1)
    assert one["players"][0]["kills"] == 5
    assert one["players"][0]["eloChange"] > 0
    assert two["players"][0]["eloChange"] < 0


def test_a_match_nobody_turned_up_to_says_so(admin_client, user_factory):
    """Every stat is zero either way, so the ending is the only thing that tells a
    walkover apart from a real result that happened to be goalless."""
    first, second = user_factory(), user_factory()
    matchId = _match(first, second)

    admin_client.post(f"/server/v1/matches/{matchId}/result", json={"played": False})

    card = admin_client.get("/admin/matches").json()["matches"][0]
    assert card["over"] is True
    assert card["ending"] == "notPlayed"


def test_finished_matches_older_than_a_day_drop_off(admin_client, user_factory):
    first, second = user_factory(), user_factory()
    old = _match(first, second)
    with Session(core_db.engine) as s:
        s.query(data.Match).where(data.Match.id == old).update(
            {"finished": True, "time": int(time()) - 25 * 60 * 60}
        )
        s.commit()

    assert admin_client.get("/admin/matches").json()["matches"] == []


def test_a_live_match_is_never_dropped_however_old(admin_client, user_factory):
    """Age is not a reason to stop showing something that is still going. A match stuck
    open for two days is the single most useful thing this page can put in front of
    somebody."""
    first, second = user_factory(), user_factory()
    stale = _match(first, second)
    with Session(core_db.engine) as s:
        s.query(data.Match).where(data.Match.id == stale).update(
            {"time": int(time()) - 48 * 60 * 60}
        )
        s.commit()

    cards = admin_client.get("/admin/matches").json()["matches"]
    assert [c["id"] for c in cards] == [stale]
