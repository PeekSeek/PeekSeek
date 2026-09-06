"""Seasons: what a new one keeps of the last, and when it takes it."""

import pytest
from sqlmodel import Session

from src.core import db as core_db
from src.core import matchmaking
from src.core import models as data
from src.core.settings import settings

RANKED = settings.mmconfig["gamemodes"][0]


def elo(user, value):
    """Put a player at a given Elo. Stat.append adds a delta to the 300 everyone starts
    on, so the delta is what gets stored."""
    data.Stat.append(user.steam64, "unt", elo=value - 300)


def standing(steam64):
    with Session(core_db.engine) as s:
        return s.query(data.Stat).where(data.Stat.steam64 == steam64).first()


@pytest.mark.parametrize(
    "finished, expected",
    [
        # Above the cap: the whole ladder past Gold 1 is re-earned every season, so it
        # does not matter how far above it you finished.
        (2000, 600),
        (900, 600),
        (750, 600),
        # Between the cap and the floor, the drop is what bites and nothing else.
        (700, 550),
        (600, 450),
        (500, 350),
        # Clearance over the seed is worth keeping: the drop would take these under 350
        # and it is not allowed to take the last 50 of what put them over it.
        (450, 350),
        (400, 350),
        (351, 350),
        # At and under that, the seed is the answer -- any guarantee here would be a
        # season handing Elo out rather than taking it.
        (350, 300),
        (300, 300),
        # And a season under the seed is a fresh start, not a hole to still be in.
        (299, 300),
        (100, 300),
        (0, 300),
    ],
)
def test_a_season_keeps_something_but_never_the_top(finished, expected):
    assert data.Season.carryOver(finished) == expected


@pytest.mark.parametrize("finished", [2000, 900, 700, 600, 500, 400, 351])
def test_a_season_always_costs_anybody_who_was_over_the_seed(finished):
    """The drop has to bite everywhere above the floor: a reset that left somebody where
    they were, or better, is not a reset."""
    assert data.Season.carryOver(finished) < finished


def test_the_first_season_adopts_every_standing_that_predates_seasons(user_factory):
    """A season row invented under a live ladder must not read as one everybody owes a
    refresher match to."""
    player = user_factory()
    # Written straight in, the way every row in the table was before this column
    # existed: Stat.append would have opened a season on its way past.
    with Session(core_db.engine) as s:
        s.add(data.Stat(steam64=player.steam64, game="unt", elo=900, season=None))
        s.commit()

    current = data.Season.current()

    assert current.name == "Season 1"
    assert standing(player.steam64).season == current.id


def test_a_new_season_is_named_by_its_manager_and_prefixed_by_the_site():
    data.Season.current()
    assert data.Season.start("26Q3").name == "Season 26Q3"
    # Typed with the prefix already on it, which is the obvious thing for a manager to
    # do, and must not produce "Season Season 26Q4".
    assert data.Season.start("Season 26Q4").name == "Season 26Q4"


def test_a_season_name_cannot_be_used_twice(client, login, user_factory):
    """Typing the same name twice is a likelier mistake than a deliberate one, and it
    used to reach the database as an IntegrityError halfway through archiving."""
    manager = user_factory(
        permissions=2 ** settings.permissions.index("season_manager")
    )
    login(client, manager)
    client.post("/esports/season", data={"name": "26Q3"})

    r = client.post("/esports/season", data={"name": "26Q3"})

    assert r.status_code == 400
    assert "already a Season 26Q3" in r.text
    assert len(data.Season.all()) == 2


def test_a_new_season_files_the_old_standings_and_empties_the_ladder(user_factory):
    player = user_factory()
    elo(player, 950)
    first = data.Season.current()

    data.Season.start("26Q3")
    data.Stat.forgetTop()

    with Session(core_db.engine) as s:
        archived = (
            s.query(data.SeasonStat)
            .where(data.SeasonStat.steam64 == player.steam64)
            .first()
        )
    assert archived.season == first.id
    assert archived.elo == 950

    # Their live row has not moved: they are simply not in the new season until they
    # play in it, which is true.
    assert standing(player.steam64).elo == 950
    assert standing(player.steam64).season == first.id
    assert data.Stat.getTop10() == []


def test_the_first_ranked_match_of_a_season_is_played_on_the_carried_over_elo(
    user_factory,
):
    """The refresher: the carry-over lands before the match is scored, so the result is
    won or lost against where the season put them rather than where the last one left
    them."""
    winner, loser = user_factory(), user_factory()
    elo(winner, 2000)
    elo(loser, 2000)
    data.Season.current()
    data.Season.start("26Q3")
    data.Stat.forgetTop()

    results, avgElo = matchmaking.matchResults(
        RANKED,
        {"steam64": str(winner.steam64), "won": True, "kills": 5, "deaths": 2},
        {"steam64": str(loser.steam64), "won": False, "kills": 2, "deaths": 5},
    )

    # Both were carried to the cap first, so the match was an even one at 600 rather
    # than a 2000-Elo pair.
    assert avgElo == 600
    assert results[str(winner.steam64)]["Current Elo"] == 600

    won = standing(winner.steam64)
    assert won.elo == 600 + results[str(winner.steam64)]["Elo Change"]
    assert won.elo > 600
    # A refresher is the first match of the new season, not the last of the old one.
    assert won.wins == 1 and won.kills == 5
    assert won.season == data.Season.current().id


def test_a_second_match_in_the_same_season_is_not_carried_over_again(user_factory):
    player, other = user_factory(), user_factory()
    elo(player, 2000)
    elo(other, 2000)
    data.Season.current()
    data.Season.start("26Q3")

    matchmaking.matchResults(
        RANKED,
        {"steam64": str(player.steam64), "won": True, "kills": 5, "deaths": 2},
        {"steam64": str(other.steam64), "won": False, "kills": 2, "deaths": 5},
    )
    afterFirst = standing(player.steam64).elo

    matchmaking.matchResults(
        RANKED,
        {"steam64": str(player.steam64), "won": True, "kills": 5, "deaths": 2},
        {"steam64": str(other.steam64), "won": False, "kills": 2, "deaths": 5},
    )

    assert standing(player.steam64).elo > afterFirst
    assert standing(player.steam64).wins == 2


def test_an_unranked_match_does_not_start_anybody_season(user_factory):
    """Only an Elo match is a refresher. An unranked one must not spend the carry-over
    on a result that was never going to move Elo anyway."""
    unranked = dict(RANKED) | {"usesElo": False}
    player, other = user_factory(), user_factory()
    elo(player, 2000)
    elo(other, 2000)
    data.Season.current()
    data.Season.start("26Q3")

    matchmaking.matchResults(
        unranked,
        {"steam64": str(player.steam64), "won": True, "kills": 5, "deaths": 2},
        {"steam64": str(other.steam64), "won": False, "kills": 2, "deaths": 5},
    )

    assert standing(player.steam64).elo == 2000


def test_only_a_season_manager_may_open_one(client, login, user_factory):
    nobody = user_factory()
    login(client, nobody)

    r = client.post("/esports/season", data={"name": "26Q3"})

    assert r.status_code == 403
    assert len(data.Season.all()) == 1


def test_a_season_manager_opens_a_season_from_the_leaderboard(
    client, login, user_factory
):
    manager = user_factory(
        permissions=2 ** settings.permissions.index("season_manager")
    )
    login(client, manager)

    r = client.post("/esports/season", data={"name": "26Q3"})

    assert r.status_code == 302
    assert [s.name for s in data.Season.all()][0] == "Season 26Q3"


def test_the_leaderboard_can_be_read_one_season_at_a_time(client, user_factory):
    player = user_factory()
    elo(player, 900)
    first = data.Season.current()
    data.Season.start("26Q3")
    data.Stat.forgetTop()

    fresh = client.get("/esports/")
    archived = client.get(f"/esports/?season={first.id}")

    assert fresh.status_code == 200
    assert archived.status_code == 200
    # The finished season still has its top ten; the new one has nobody in it yet.
    assert "Season 26Q3" in fresh.text and "Season 1" in fresh.text
    assert "/static/ranks/" in archived.text


def test_the_ladder_hands_out_one_role_per_badge_and_none_for_a_tier_step():
    """Immune 1 is a badge on the site and the Immune role on Discord: the ladder has
    nine tiers and the server has five roles, because a tier step is not a promotion
    anybody else can see."""
    from src.core.notify import rank_role_ids

    roles = rank_role_ids()
    assert len(roles) == 5
    assert all(tier.get("role") for tier in settings.ranks)

    def role(name):
        return next(t["role"] for t in settings.ranks if t["name"] == name)

    assert role("Immune") == role("Immune 1")
    assert role("Bronze") == role("Bronze 1")
    assert role("Apollo") not in {role("Immune"), role("Gold")}


def test_an_account_with_no_discord_is_never_asked_about(user_factory):
    """Most players have not linked Discord, and the ladder must not go looking for them
    on it. The check comes before any request, which is also what keeps this suite from
    talking to Discord: conftest sets a bot token so the "no token" branch is not the
    one under test.
    """
    from src.core.notify import sync_rank_role

    player, other = user_factory(), user_factory()

    results, _ = matchmaking.matchResults(
        RANKED,
        {"steam64": str(player.steam64), "won": True, "kills": 5, "deaths": 2},
        {"steam64": str(other.steam64), "won": False, "kills": 2, "deaths": 5},
    )

    assert results[str(player.steam64)]["Elo Change"] > 0
    assert sync_rank_role(player.steam64) is False
    assert sync_rank_role(other.steam64) is False
