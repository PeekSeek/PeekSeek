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


def test_match_overlay_finds_a_premium_users_custom_match_by_created_by(
    client, login, user_factory
):
    """The premium /match/custom flow, end to end: create the match through the real
    route (not by constructing Match directly), then confirm ?createdBy=<creator> on
    the public overlay endpoint actually finds it."""
    owner = user_factory()
    owner.setPermission("premium", True)
    login(client, owner)

    created = client.post(
        "/match/custom",
        json={
            "teams": {"one": [str(owner.steam64)], "two": ["76561197960287930"]},
            "region": "paris",
            "gamemode": 0,
            "ballistics": True,
            "map": "Alpha Valley",
            "movement": True,
        },
    )
    assert created.status_code == 200
    matchId = int(created.text)

    r = client.get("/match/overlay", params={"createdBy": owner.steam64})
    assert r.status_code == 200
    assert r.json()["id"] == matchId


def test_match_overlay_created_by_also_finds_a_premium_users_regular_match(
    client, user_factory
):
    """The personal overlay link is meant to follow whatever match a premium streamer
    is actually playing — regular matchmaking is how most of their matches happen, not
    just the ones they spin up through /match/custom."""
    from src.core.db import open as opendb
    from src.core import models as data

    player = user_factory()
    player.setPermission("premium", True)
    with opendb() as db:
        match = data.Match(
            gamemode=0, teams=[{"name": "one", "users": [str(player.steam64)]}]
        )
        db.add(match)
        db.flush()
        matchId = match.id

    r = client.get("/match/overlay", params={"createdBy": player.steam64})
    assert r.status_code == 200
    assert r.json()["id"] == matchId


def test_match_overlay_created_by_requires_premium(client, user_factory):
    """The personal overlay link is a premium perk — a non-premium participant must
    not get a working ?createdBy= link just by having played a match."""
    from src.core.db import open as opendb
    from src.core import models as data

    player = user_factory()
    with opendb() as db:
        match = data.Match(
            gamemode=0, teams=[{"name": "one", "users": [str(player.steam64)]}]
        )
        db.add(match)
        db.flush()

    r = client.get("/match/overlay", params={"createdBy": player.steam64})
    assert r.status_code == 404


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
    # recordFinalResults writes the match's own record; the running totals behind the
    # stat strip are a separate row the game server appends to. Without this the page
    # renders "User has no statistics" instead, which is what the statcards assertion
    # below was silently passing on back when profile.html carried its CSS inline and
    # the class name appeared in the page whether or not anything used it.
    data.Stat.append(player.steam64, "unt", kills=4, deaths=1, wins=1)

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
        {
            str(player.steam64): {
                "Kills": 4,
                "Deaths": 1,
                "Won": True,
                "Current Elo": 300,
                "Elo Change": 12,
            }
        },
    )
    data.MatchResult.recordFinalResults(
        matchIds[1],
        {
            str(player.steam64): {
                "Kills": 2,
                "Deaths": 5,
                "Won": False,
                "Current Elo": 312,
                "Elo Change": -5,
            }
        },
    )

    r = client.get(f"/u/{player.steam64}")

    assert r.status_code == 200
    assert "Not enough ranked matches yet" not in r.text
    assert "chart.js" in r.text
    assert 'id="eloChart"' in r.text
    assert '"elo": 312' in r.text  # 300 + 12, chronologically first
    assert '"elo": 307' in r.text  # 312 - 5, second
    assert 'class="matchrow win"' in r.text
    assert 'class="matchrow loss"' in r.text


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


def test_match_page_never_shows_a_kit_or_powerup_as_pre_selected(
    client, login, user_factory
):
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


def test_layout_scripts_are_actually_served(client):
    """Every script the layout pulls in has to exist on disk.

    htmx.min.js went missing from public/assets while layout.html kept asking
    for it, and since the loadout editors are the only htmx on the site, the kit
    and outfit pages quietly stopped saving anything at all.
    """
    import re

    from src.core.settings import settings

    layout = (settings.root / "templates" / "layout.html").read_text(encoding="utf-8")
    sources = re.findall(r'<script src="(/static/[^"]+)"', layout)

    assert sources
    for source in sources:
        assert client.get(source).status_code == 200, source


def test_kit_editor_leaves_out_the_barrel_row_for_a_sniper(client, login, user_factory):
    """A sniper's barrel list holds one item, which is not a choice, so the row is
    left out rather than rendered as a lone tile that can never change anything."""
    login(client, user_factory())
    r = client.get("/me/kit/sniper")

    assert r.status_code == 200
    assert 'name="sight"' in r.text
    assert 'name="barrel"' not in r.text


def test_melee_kit_editor_offers_no_attachments(client, login, user_factory):
    login(client, user_factory())
    r = client.get("/me/kit/melee")

    assert r.status_code == 200
    for slot in ("sight", "tactical", "grip", "barrel"):
        assert f'name="{slot}"' not in r.text
    assert "takes no attachments" in r.text


def test_kit_editor_saves_a_pick_and_returns_it_selected(client, login, user_factory):
    import re

    from src.core import models as data
    from src.core.settings import settings

    player = user_factory()
    login(client, player)

    guns = settings.kits["kits"]["meta"]
    gunId = list(guns)[1]
    sight = guns[gunId]["sight"][2]

    r = client.post("/me/kit/meta", data={"gun": gunId, "sight": sight})

    assert r.status_code == 200
    assert data.Kit.publicData(player.steam64)["meta"]["items"][0][0] == int(gunId)
    # The swapped-in response is the only feedback a player gets that their click
    # landed, so the tile it comes back with has to be the one they picked.
    assert re.search(rf'id="sight-{sight}"[^>]*\bchecked\b', r.text, re.S)


def test_lobby_hands_steam_the_launch_command(client):
    """Anonymous on purpose: this link gets posted, and making people log in first
    would defeat the point of posting it."""
    from src.core.settings import settings

    r = client.get("/lobby")
    assert r.status_code == 200

    url = f"steam://run/304930//+connect {settings.lobby_connect_code}"
    assert url in r.text
    # The bare code too, for Connect Directly and for anyone without the handler.
    assert settings.lobby_connect_code in r.text
