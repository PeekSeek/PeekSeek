"""The admin match list: what /admin/matches puts on a card, and who may ask for it."""

from time import time

import pytest
from sqlmodel import Session

from src.core import db as core_db
from src.core import models as data

GAMEMODE = 0


@pytest.fixture
def admin_client(client, login, user_factory):
    """Logged in as an admin, and allow-listed as a game server, so a test can drive a
    match through the real `/server/v1/*` calls."""
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
            matchSettings={
                "map": "Alpha Valley",
                "ballistics": True,
                "movement": False,
            },
        )
        for field, value in overrides.items():
            setattr(match, field, value)
        db.add(match)
        db.flush()
        return match.id


def test_the_match_list_is_admin_only(client, login, user_factory):
    login(client, user_factory())
    assert client.get("/admin/matches").status_code == 401


def test_a_live_match_lists_its_whole_roster(admin_client, user_factory):
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


def test_a_live_match_shows_its_round_score_and_no_player_stats(
    admin_client, user_factory
):
    first, second = user_factory(), user_factory()
    matchId = _match(first, second)
    r = admin_client.post(
        f"/server/v1/matches/{matchId}/rounds",
        json={"wins": {"team_one": 1, "team_two": 0}},
    )
    assert r.status_code == 200, r.text

    card = admin_client.get("/admin/matches").json()["matches"][0]
    one, two = card["teams"]

    assert (one["wins"], two["wins"]) == (1, 0)
    assert one["players"][0]["kills"] is None


def test_a_scored_match_keeps_its_round_score_and_elo(admin_client, user_factory):
    first, second = user_factory(), user_factory()
    matchId = _match(first, second)
    admin_client.post(
        f"/server/v1/matches/{matchId}/rounds",
        json={"wins": {"team_one": 3, "team_two": 1}},
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
    # MatchResult stores naive UTC; read back as local time it ended before it began.
    assert card["endedAt"] >= card["startedAt"]
    assert (one["wins"], two["wins"]) == (3, 1)
    assert one["players"][0]["kills"] == 5
    assert one["players"][0]["eloChange"] > 0
    assert two["players"][0]["eloChange"] < 0


def test_a_match_nobody_turned_up_to_says_so(admin_client, user_factory):
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
    first, second = user_factory(), user_factory()
    stale = _match(first, second)
    with Session(core_db.engine) as s:
        s.query(data.Match).where(data.Match.id == stale).update(
            {"time": int(time()) - 48 * 60 * 60}
        )
        s.commit()

    cards = admin_client.get("/admin/matches").json()["matches"]
    assert [c["id"] for c in cards] == [stale]
