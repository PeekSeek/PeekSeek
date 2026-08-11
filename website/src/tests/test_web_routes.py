def test_index_anonymous_renders(client):
    r = client.get("/")
    assert r.status_code == 200


def test_play_redirects_anonymous(client):
    r = client.get("/play")
    assert r.status_code == 302
    assert r.headers["location"] == "/"


def test_index_redirects_when_logged_in(client, login, user_factory):
    login(client, user_factory())
    r = client.get("/")
    assert r.status_code == 302
    assert "/play" in r.headers["location"]


def test_settings_page_requires_login(client):
    assert client.get("/settings").status_code == 302


def test_settings_page_rendered_when_logged_in(client, login, user_factory):
    login(client, user_factory())
    assert client.get("/settings").status_code == 200


def test_admin_requires_admin_permission(client, login, user_factory):
    login(client, user_factory())
    r = client.get("/admin/")
    assert r.status_code == 302


def test_admin_allowed_for_admin_user(client, login, user_factory):
    from sqlmodel import Session
    from src.core import db as core_db
    from src.core import models as data

    user = user_factory()
    with Session(core_db.engine) as s:
        s.query(data.User).filter(data.User.steam64 == user.steam64).update(
            {"permissions": 1}
        )
        s.commit()

    login(client, user)
    assert client.get("/admin/").status_code == 200


def test_logout_clears_session(client, login, user_factory):
    login(client, user_factory())
    r = client.get("/auth/logout")
    assert r.status_code == 302
    assert client.get("/settings").status_code == 302  # anonymous again


def test_404_renders_error_page(client):
    r = client.get("/this-route-does-not-exist", headers={"accept": "text/html"})
    assert r.status_code == 404
    assert "text/html" in r.headers["content-type"]


def test_profile_page_renders_match_history(client, user_factory):
    from src.core.db import open as opendb
    from src.core import models as data

    player = user_factory()
    with opendb() as db:
        match = data.Match(
            gamemode=0, teams=[{"name": "one", "users": [str(player.steam64)]}]
        )
        db.add(match)
        db.flush()
        matchId = match.id

    data.MatchResult.appendKill(
        matchId,
        {
            "round": 1,
            "killerName": "kcvdh",
            "victimName": "Ferret",
            "weapon": 363,
            "limb": "skull",
        },
    )
    data.MatchResult.recordFinalResults(
        matchId,
        {
            str(player.steam64): {
                "Kills": 4,
                "Deaths": 1,
                "Headshots": 2,
                "Won": True,
                "Elo Change": 12,
            }
        },
    )

    r = client.get(f"/u/{player.steam64}")

    assert r.status_code == 200
    assert "statcards" in r.text
    assert "matchlist" in r.text


def test_profile_page_with_no_matches_renders(client, user_factory):
    player = user_factory()
    r = client.get(f"/u/{player.steam64}")
    assert r.status_code == 200
    assert "No matches played yet." in r.text


def test_match_detail_page_renders_the_kill_log(client, user_factory):
    from src.core.db import open as opendb
    from src.core import models as data

    player = user_factory()
    with opendb() as db:
        match = data.Match(
            gamemode=0, teams=[{"name": "one", "users": [str(player.steam64)]}]
        )
        db.add(match)
        db.flush()
        matchId = match.id

    data.MatchResult.appendKill(
        matchId,
        {
            "round": 1,
            "killerName": "kcvdh",
            "victimName": "Ferret",
            "weapon": 363,
            "limb": "skull",
        },
    )
    data.MatchResult.recordFinalResults(
        matchId,
        {str(player.steam64): {"Kills": 1, "Deaths": 0, "Headshots": 1, "Won": True}},
    )

    r = client.get(f"/match/{matchId}")

    assert r.status_code == 200
    assert "Ferret" in r.text


def test_match_detail_page_missing_match_404s(client):
    r = client.get("/match/999999")
    assert r.status_code == 404


def test_match_page_renders_the_commit_list(client, login, user_factory):
    from src.core.db import open as opendb
    from src.core import models as data

    player, opponent = user_factory(), user_factory()
    with opendb() as db:
        db.add(
            data.Match(
                gamemode=0,
                teams=[
                    {"name": "one", "users": [str(player.steam64)]},
                    {"name": "two", "users": [str(opponent.steam64)]},
                ],
                matchSettings={
                    "ballistics": True,
                    "map": "Alpha Valley",
                    "movement": True,
                },
            )
        )

    login(client, player)
    r = client.get("/match")

    assert r.status_code == 200
    assert 'id="commitlist"' in r.text
    assert 'id="commitcount"' in r.text
    assert 'id="connectprogress"' in r.text
    assert 'class="checkmark"' in r.text
