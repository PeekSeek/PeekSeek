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
    assert 'class="tile statcard s1"' in r.text
    assert "matchlist" in r.text
    # One of each tile. A conflict resolution once left a second copy of everything
    # below the identity tile, which every other assertion here passed straight over.
    assert r.text.count('class="bento') == 1
    assert r.text.count("tile history") == 1
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


def test_match_detail_page_renders_the_result(client, user_factory):
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

    data.MatchResult.recordFinalResults(
        matchId,
        {str(player.steam64): {"Kills": 1, "Deaths": 0, "Headshots": 1, "Won": True}},
    )

    r = client.get(f"/match/{matchId}")

    assert r.status_code == 200
    assert "WIN" in r.text


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
    assert 'id="rail"' in r.text
    assert 'id="turn"' in r.text
    assert 'id="joinline"' in r.text
    assert 'id="matchsettings"' in r.text
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


def test_profile_page_shows_the_rank_the_elo_earns(client, user_factory):
    from src.core import models as data

    player = user_factory()
    data.Stat.append(player.steam64, "unt", kills=1)
    data.Stat.append(player.steam64, "unt", elo=450)  # 300 seeded + 450 = 750, Immune

    r = client.get(f"/u/{player.steam64}")

    assert r.status_code == 200
    assert 'data-rank="immune"' in r.text
    assert "/static/ranks/immune.svg" in r.text
    # The bar runs from this badge to the next one, and states the Elo each starts at
    # rather than the distance between them.
    assert "/static/ranks/immune1.svg" in r.text
    assert '<span class="mark">750</span>' in r.text
    assert '<span class="mark">800</span>' in r.text
    assert "Elo to" not in r.text


def _finishedMatchFor(player, **summary):
    from src.core.db import open as opendb
    from src.core import models as data

    with opendb() as db:
        match = data.Match(
            gamemode=0, teams=[{"name": "one", "users": [str(player.steam64)]}]
        )
        db.add(match)
        db.flush()
        matchId = match.id

    data.MatchResult.recordFinalResults(
        matchId,
        {str(player.steam64): {"Kills": 4, "Deaths": 1, "Won": True, **summary}},
    )
    return matchId


def test_match_detail_shows_the_reader_their_own_elo_on_the_bar(
    client, login, user_factory
):
    """The bar is the one thing on this page that is about the reader rather than the
    match, so it is only drawn for the player who was in it."""
    player = user_factory()
    matchId = _finishedMatchFor(player, **{"Current Elo": 620, "Elo Change": 30})

    login(client, player)
    r = client.get(f"/match/{matchId}")

    assert r.status_code == 200
    assert 'class="rankCard yourRank"' in r.text
    assert 'data-rank="gold1"' in r.text
    # 620 into Gold 1 is 20% of the way through it, and the 30 Elo won is another 30%.
    assert 'style="width:20.0%"' in r.text
    assert 'class="gained" style="left:20.0%;width:30.0%"' in r.text
    assert "+30" in r.text
    assert "50 Elo to Immune" in r.text


def test_match_detail_calls_out_a_promotion(client, login, user_factory):
    player = user_factory()
    matchId = _finishedMatchFor(player, **{"Current Elo": 590, "Elo Change": 20})

    login(client, player)
    r = client.get(f"/match/{matchId}")

    assert 'class="rankMove up">Ranked up' in r.text
    assert 'data-rank="gold1"' in r.text
    # Promoted, so the bar fills the new tier from empty rather than from where the
    # Elo sat in the tier below.
    assert 'class="gained" style="left:0.0%;width:10.0%"' in r.text


def test_match_detail_calls_out_a_demotion(client, login, user_factory):
    player = user_factory()
    matchId = _finishedMatchFor(player, **{"Current Elo": 610, "Elo Change": -20})

    login(client, player)
    r = client.get(f"/match/{matchId}")

    assert 'class="rankMove down">Ranked down' in r.text
    assert 'class="lost" style="left:90.0%;width:10.0%"' in r.text


def test_match_detail_draws_no_bar_for_somebody_who_was_not_in_the_match(
    client, login, user_factory
):
    player = user_factory()
    stranger = user_factory()
    matchId = _finishedMatchFor(player, **{"Current Elo": 620, "Elo Change": 30})

    login(client, stranger)
    r = client.get(f"/match/{matchId}")

    assert r.status_code == 200
    assert "yourRank" not in r.text
    # The results table still names everybody, and still badges them.
    assert "/static/ranks/gold1.svg" in r.text


def test_match_detail_of_an_unranked_match_has_no_rank_to_show(client, user_factory):
    """An unranked gamemode records no Elo Change, and neither did anything played
    before Elo existed, so there is nothing to badge rather than a change of zero."""
    player = user_factory()
    matchId = _finishedMatchFor(player)

    r = client.get(f"/match/{matchId}")

    assert r.status_code == 200
    assert "yourRank" not in r.text
    assert "/static/ranks/" not in r.text


def test_leaderboard_badges_the_players_it_lists(client, login, user_factory):
    from src.core import models as data
    from src.core.models import stat as statModule

    top = user_factory()
    data.Stat.append(top.steam64, "unt", elo=450)  # 750, Immune

    # getTop10 caches for a minute, and an earlier test's ladderPosition call will have
    # filled it with rows that no longer exist by the time this one runs.
    statModule.Stat.forgetTop()

    login(client, top)
    r = client.get("/esports/")

    assert r.status_code == 200
    assert "/static/ranks/immune.svg" in r.text
    assert '<span class="tier">Immune</span>' in r.text
    assert "750ELO" in r.text
    # Your own panel is a reminder of who you are on a page about everybody else, so it
    # carries the number and nothing that explains it.
    assert '<span class="youElo">750</span>' in r.text
    assert "Elo to" not in r.text
    # The badge rides on the avatar, and it is still the badge: mounted in the crest
    # beside the picture rather than cropped and rounded off like one.
    assert 'class="crest"' in r.text
    assert 'class="badge"' in r.text


def test_a_top_three_place_outranks_a_permission_ring(client, user_factory):
    """One ring, one meaning: an admin who is also first on the ladder wears gold.

    The two are drawn the same way, so whichever is rarer has to win outright rather
    than both being rendered and one covering the other.
    """
    from src.core import models as data
    from src.core.models import stat as statModule
    from src.core.settings import settings

    admin = user_factory(
        permissions=2 ** settings.permissions.index("admin"), discord=99
    )
    data.Stat.append(admin.steam64, "unt", elo=600)
    statModule.Stat.forgetTop()

    r = client.get(f"/u/{admin.steam64}")

    assert r.status_code == 200
    assert 'data-place="1"' in r.text
    assert "data-ring=" not in r.text
    assert "#1" in r.text


def test_a_linked_discord_rings_a_player_who_is_off_the_podium(client, user_factory):
    from src.core import models as data
    from src.core.models import stat as statModule

    for _ in range(3):
        data.Stat.append(user_factory().steam64, "unt", elo=600)

    player = user_factory(discord=1234)
    data.Stat.append(player.steam64, "unt", elo=50)
    statModule.Stat.forgetTop()

    r = client.get(f"/u/{player.steam64}")

    assert r.status_code == 200
    assert 'data-ring="discord"' in r.text
    # Fourth on the ladder is not a place anybody wears, and only the top three get a
    # number in front of their name.
    assert "data-place=" not in r.text


def test_an_ordinary_page_never_rebuilds_the_top_ten(client, login, user_factory):
    """The sidebar's badge must not be the thing that asks Steam about ten players.

    getTop10 caches for a minute and rebuilds by looking up ten profiles, one blocking
    call each. page_context runs on every render of every page, so a badge there that
    forced the rebuild put that whole cost on whichever page happened to be next.
    """
    from src.core.models import stat as statModule

    me = user_factory()
    login(client, me)
    statModule.Stat.forgetTop()

    assert client.get(f"/u/{me.steam64}").status_code == 200
    # The ids behind a ring are one cheap query; the ten Steam profiles behind the
    # leaderboard are not, and this page did not ask for them.
    assert statModule._top10Cache[0] is None


def test_the_tier_under_a_capped_one_shows_a_position_instead_of_a_bar(
    client, user_factory
):
    """At Immune 1 the bar would fill to the end and the badge still not change, because
    Apollo is five seats rather than a threshold. The place on the ladder is the only
    thing left that says how far there is to go."""
    from src.core import models as data

    player = user_factory()
    data.Stat.append(player.steam64, "unt", elo=550)  # 850, past the 800 ceiling
    # Five seats, all taken, so this player is sixth and wears Immune 1 on the same Elo
    # that would otherwise be Apollo.
    for _ in range(5):
        data.Stat.append(user_factory().steam64, "unt", elo=700)

    r = client.get(f"/u/{player.steam64}")

    assert r.status_code == 200
    assert 'class="rankStanding"' in r.text
    assert "rankProgress" not in r.text
    # Only the numbers are bold; ELO and # are labels on them.
    assert "<b>850</b>ELO #<b>6</b>" in r.text
    assert "worldwide" not in r.text


def test_leaderboard_renders_for_a_signed_out_visitor(client):
    from src.core.models import stat as statModule

    statModule.Stat.forgetTop()
    assert client.get("/esports/").status_code == 200


def test_play_page_carries_the_region_activity_it_has(client, login, user_factory):
    """The play page hands the region line its numbers as data attributes, and the
    script decides which are worth showing. Without this the line is silently empty on
    any environment whose lobbies report somewhere else, which is exactly what dev
    looks like."""
    from datetime import datetime, timedelta
    from time import time

    from src.core.db import open as opendb
    from src.core import models as data

    player = user_factory()
    with opendb() as db:
        db.add(data.AuthorizedServer(address="203.0.113.10", region="paris"))
        db.add(data.LobbyPopulation(address="203.0.113.10", players=6, at=int(time())))
        match = data.Match(
            gamemode=0,
            teams=[{"name": "one", "users": []}, {"name": "two", "users": []}],
            matchSettings={"ballistics": True, "map": "Alpha Valley", "movement": True},
        )
        db.add(match)
        db.flush()
        db.add(
            data.MatchResult(
                matchId=match.id,
                region="paris",
                gamemode=0,
                startTime=datetime.utcnow() - timedelta(minutes=8),
                endTime=datetime.utcnow() - timedelta(minutes=4),
                data={},
            )
        )

    login(client, player)
    r = client.get("/play")

    assert r.status_code == 200
    assert 'id="regionActivity"' in r.text
    assert 'data-paris-players="6"' in r.text
    assert 'data-paris-last="4"' in r.text


def test_play_page_says_nothing_about_a_region_with_no_lobby(
    client, login, user_factory
):
    """A quiet region gets no attributes at all, so the line stays hidden rather than
    advertising an empty arena to the next player who was about to queue."""
    player = user_factory()

    login(client, player)
    r = client.get("/play")

    assert r.status_code == 200
    assert 'id="regionActivity"' in r.text
    assert "data-paris-players" not in r.text
