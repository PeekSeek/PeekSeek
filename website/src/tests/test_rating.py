"""Ranked rating: the hidden skill matchmaking balances on, and the ladder players see."""

import pytest

from src.core import matchmaking
from src.core import models as data
from src.core.settings import settings

RANKED = settings.mmconfig["gamemodes"][0]


def side(*players):
    return [{"steam64": steam64, "won": won} for steam64, won in players]


def test_skill_is_zero_sum_across_a_match(user_factory):
    """Two against two, with players on different deviations: whatever the winners take,
    the losers gave up, so the pool never inflates."""
    a, b, c, d = (user_factory() for _ in range(4))
    for user, elo in ((a, 700), (b, 400), (c, 550), (d, 500)):
        data.Stat.append(user.steam64, "unt", elo=elo - 300)
    before = sum(data.Stat.getSkill(u.steam64)[0] for u in (a, b, c, d))

    matchmaking.matchResults(
        RANKED,
        {"steam64": str(a.steam64), "won": True},
        {"steam64": str(b.steam64), "won": True},
        {"steam64": str(c.steam64), "won": False},
        {"steam64": str(d.steam64), "won": False},
    )

    after = sum(data.Stat.getSkill(u.steam64)[0] for u in (a, b, c, d))
    assert after == pytest.approx(before)
    assert data.Stat.getSkill(a.steam64)[0] > 700
    assert data.Stat.getSkill(c.steam64)[0] < 550


def test_a_draw_between_teams_still_moves_skill_towards_the_underdog():
    skills = {"fav": (900.0, 100.0), "dog": (600.0, 100.0)}
    sides = [side(("fav", False)), side(("dog", False))]

    moved = matchmaking._skillUpdates(sides, skills, draw=True)

    assert moved["fav"][0] < 900
    assert moved["dog"][0] - 600 == pytest.approx(900 - moved["fav"][0])


def test_an_upset_moves_the_favourite_the_most():
    """Losing a match you were expected to win costs more than winning it earns."""
    skills = {"fav": (1000.0, 100.0), "dog": (700.0, 100.0)}

    upset = matchmaking._skillUpdates(
        [side(("fav", False)), side(("dog", True))], skills, draw=False
    )
    expected = matchmaking._skillUpdates(
        [side(("fav", True)), side(("dog", False))], skills, draw=False
    )

    assert 1000 - upset["fav"][0] > 3 * (expected["fav"][0] - 1000)
    assert upset["fav"][0] - 1000 == pytest.approx(700 - upset["dog"][0])


def test_an_unsure_player_moves_further_and_every_match_makes_them_surer():
    skills = {
        "new": (500.0, settings.new_deviation),
        "vet": (500.0, settings.min_deviation),
        "a": (500.0, 100.0),
        "b": (500.0, 100.0),
    }
    moved = matchmaking._skillUpdates(
        [side(("new", True), ("vet", True)), side(("a", False), ("b", False))],
        skills,
        draw=False,
    )

    assert moved["new"][0] - 500 > moved["vet"][0] - 500 > 0
    assert moved["new"][1] < settings.new_deviation
    assert moved["vet"][1] >= settings.min_deviation


def test_kills_do_not_move_either_rating(user_factory):
    quiet, busy, other1, other2 = (user_factory() for _ in range(4))

    first, _ = matchmaking.matchResults(
        RANKED,
        {"steam64": str(quiet.steam64), "won": True, "kills": 0, "deaths": 9},
        {"steam64": str(other1.steam64), "won": False},
    )
    second, _ = matchmaking.matchResults(
        RANKED,
        {"steam64": str(busy.steam64), "won": True, "kills": 30, "deaths": 0},
        {"steam64": str(other2.steam64), "won": False},
    )

    assert (
        first[str(quiet.steam64)]["Elo Change"]
        == second[str(busy.steam64)]["Elo Change"]
    )
    assert data.Stat.getSkill(quiet.steam64) == data.Stat.getSkill(busy.steam64)


def test_a_ladder_far_below_skill_climbs_quickly():
    win = matchmaking._ladderDelta(300, 900, won=True)
    loss = matchmaking._ladderDelta(300, 900, won=False)

    assert win == round(settings.win_points * (1 + settings.max_boost))
    assert -loss == round(settings.loss_points * (1 - settings.max_relief))
    assert win > 2 * settings.win_points


def test_at_equilibrium_winning_half_your_games_goes_nowhere():
    for elo in (300, 700, 1200):
        for skill in (elo, elo - 50, elo - 400):
            win = matchmaking._ladderDelta(elo, skill, won=True)
            loss = matchmaking._ladderDelta(elo, skill, won=False)
            assert win + loss == pytest.approx(0, abs=1)


def test_a_season_cut_is_won_back_and_no_further():
    """Skill survives a season and the ladder does not, so a returning player climbs back
    to where they were quickly. Once the ladder is level with skill the boost is gone and
    a win is worth plain points again."""
    finished = 900
    elo = carried = data.Season.carryOver(finished)
    assert elo < finished

    wins = 0
    while elo < finished:
        elo += matchmaking._ladderDelta(elo, finished, won=True)
        wins += 1

    flatWins = (finished - carried) / settings.win_points
    assert wins <= flatWins * 0.6
    assert elo - finished < settings.win_points * (1 + settings.max_boost)
    assert matchmaking._ladderDelta(elo, finished, won=True) == settings.win_points


def test_there_is_no_cliff_anywhere_in_the_gap():
    """Every Elo of gap changes a result by at most a point, and always the same way."""
    skill = 1000
    wins = [
        matchmaking._ladderDelta(elo, skill, won=True) for elo in range(2000, 100, -1)
    ]
    losses = [
        matchmaking._ladderDelta(elo, skill, won=False) for elo in range(2000, 100, -1)
    ]

    for series in (wins, losses):
        steps = [after - before for before, after in zip(series, series[1:])]
        assert all(0 <= step <= 1 for step in steps)


def test_a_season_leaves_skill_alone(user_factory):
    player, other = user_factory(), user_factory()
    data.Stat.append(player.steam64, "unt", elo=900 - 300)
    data.Stat.append(other.steam64, "unt", elo=900 - 300)
    data.Season.current()
    data.Season.start("26Q3")

    results, _ = matchmaking.matchResults(
        RANKED,
        {"steam64": str(player.steam64), "won": True},
        {"steam64": str(other.steam64), "won": False},
    )

    assert results[str(player.steam64)]["Current Elo"] == data.Season.carryOver(900)
    assert data.Stat.getSkill(player.steam64)[0] > 900
    assert data.Stat.getSkill(other.steam64)[0] < 900


def test_matchmaking_balances_on_skill_not_the_ladder(user_factory):
    player = user_factory()
    data.Stat.append(player.steam64, "unt", elo=0)
    data.Stat.setSkill(player.steam64, 1234.0, 80.0)

    assert matchmaking._teamElo([player.steam64]) == 1234.0
