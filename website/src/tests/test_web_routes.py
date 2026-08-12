def test_index_anonymous_renders(client):
    r = client.get("/")
    assert r.status_code == 200


def test_play_redirects_anonymous(client):
    r = client.get("/play")
    assert r.status_code == 302
    assert r.headers["location"] == "/"


def test_play_page_renders_the_party_panel(client, login, user_factory):
    login(client, user_factory())
    r = client.get("/play")
    assert r.status_code == 200
    assert 'id="partyPanel"' in r.text
    assert 'id="partyInviteInput"' in r.text


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


def test_overlay_requires_id_or_created_by(client):
    r = client.get("/overlay")
    assert r.status_code == 302
    assert r.headers["location"] == "/"


def test_overlay_quotes_the_match_id_in_the_script(client):
    r = client.get("/overlay", params={"id": "5"})
    assert r.status_code == 200
    assert 'let matchid = "5"' in r.text
    assert "let createdby = null" in r.text


def test_overlay_quotes_a_steam64_created_by_without_losing_precision(client):
    """A steam64 is ~17 digits, past Number.MAX_SAFE_INTEGER — interpolated unquoted
    into the page it becomes a JS number literal that silently rounds, and createdBy
    never matches the match again. Must stay a string end to end."""
    steam64 = "76561198012345678"
    r = client.get("/overlay", params={"createdBy": steam64})
    assert r.status_code == 200
    assert f'let createdby = "{steam64}"' in r.text


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
    # Only one match, and it has no "Current Elo" paired with the change — not
    # enough to plot a trend.
    assert "Not enough ranked matches yet" in r.text
    assert "chart.js" not in r.text


def test_profile_page_renders_the_elo_trend_with_two_ranked_matches(
    client, user_factory
):
    from src.core.db import open as opendb
    from src.core import models as data

    player = user_factory()
    matchIds = []
    for _ in range(2):
        with opendb() as db:
            match = data.Match(
                gamemode=0, teams=[{"name": "one", "users": [str(player.steam64)]}]
            )
            db.add(match)
            db.flush()
            matchIds.append(match.id)

    data.MatchResult.recordFinalResults(
        matchIds[0],
        {str(player.steam64): {"Kills": 4, "Deaths": 1, "Won": True, "Current Elo": 300, "Elo Change": 12}},
    )
    data.MatchResult.recordFinalResults(
        matchIds[1],
        {str(player.steam64): {"Kills": 2, "Deaths": 5, "Won": False, "Current Elo": 312, "Elo Change": -5}},
    )

    r = client.get(f"/u/{player.steam64}")

    assert r.status_code == 200
    assert "Not enough ranked matches yet" not in r.text
    assert "chart.js" in r.text
    assert 'id="eloChart"' in r.text
    assert '"elo": 312' in r.text  # 300 + 12, chronologically first
    assert '"elo": 307' in r.text  # 312 - 5, second
    assert 'class="formpill win"' in r.text
    assert 'class="formpill loss"' in r.text


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
    assert 'id="matchsettings"' in r.text
    assert 'id="picktimer"' in r.text
    # No checkmark badge, and kit/powerup tiles are real buttons (keyboard-reachable),
    # not divs with an onclick.
    assert "checkmark" not in r.text
    assert '<button type="button" class="kitbutton' in r.text
    assert '<button type="button" class="powerupbutton' in r.text


def test_match_page_never_shows_a_kit_or_powerup_as_pre_selected(client, login, user_factory):
    """A tile carrying .selectedChoice before the player has ever clicked would claim a
    choice was made when the saved pick is still None — the server falls back to the
    first kit/powerup at match time regardless, but the tile UI must not imply that's
    already "your" pick."""
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
    # These seed the client-side `currentkit`/`currentPowerup` vars that
    # updateKitClasses()/updatePowerupClasses() use to add .selectedChoice — null
    # means no tile renders as selected until the player actually clicks one.
    assert '"currentkit": null' in r.text
    assert '"currentpowerup": null' in r.text
