"""The Elo ladder: the configured tiers, the badge an Elo earns, and the bar a
finished match draws on it."""

import pytest

from src.core.models.rank import Rank
from src.core.settings import settings


def test_configured_tiers_ascend_and_have_artwork():
    """Rank.for_elo walks the tiers in order and stops at the first one an Elo does not
    reach, so a list out of order silently hands out the wrong badge.

    Ascending rather than strictly ascending: the capped tier shares the floor of the
    one below it, because what separates those two is a seat and not Elo. progress_at
    divides by the gap between two tiers and returns a full bar rather than dividing by
    that zero.
    """
    elos = [tier["elo"] for tier in settings.ranks]
    assert elos == sorted(elos)

    for tier in settings.ranks:
        assert (settings.public / "ranks" / f"{tier['icon']}.svg").exists()


def test_only_the_top_tier_is_capped():
    """for_elo stops at a capped tier it cannot award rather than stepping over it, so a
    capped tier anywhere but the end would hide every tier above it from everybody."""
    for tier in settings.ranks[:-1]:
        assert "onlyTopPlayers" not in tier


@pytest.mark.parametrize(
    "elo, expected",
    [
        (100, "Bronze"),
        (199, "Bronze"),
        (200, "Bronze 1"),
        (300, "Silver"),
        (499, "Silver 1"),
        (500, "Gold"),
        (600, "Gold 1"),
        (700, "Immune"),
        (899, "Immune 1"),
    ],
)
def test_each_band_earns_its_badge(elo, expected):
    assert Rank.for_elo(elo).name == expected


def test_elo_below_the_ladder_still_gets_the_bottom_badge():
    """_eloDelta will not take anyone under 100, but a row written before that rule, or
    by hand, must not fall off the bottom of the list."""
    assert Rank.for_elo(0).name == "Bronze"


def test_apollo_needs_a_seat_as_well_as_the_elo():
    # Apollo and Immune 1 open together at 800; from there the badge is decided by the
    # seat and not by another hundred Elo.
    assert Rank.for_elo(800, position=1).name == "Apollo"
    assert Rank.for_elo(799, position=1).name == "Immune"
    assert Rank.for_elo(850, position=5).name == "Apollo"
    assert Rank.for_elo(850, position=6).name == "Immune 1"
    assert Rank.for_elo(900, position=1).name == "Apollo"
    assert Rank.for_elo(2000, position=5).name == "Apollo"
    assert Rank.for_elo(2000, position=6).name == "Immune 1"
    # An unknown position is not a good-enough reason to hand out the scarcest badge
    # on the ladder, so it reads as "not in the top five" rather than "probably fine".
    assert Rank.for_elo(2000).name == "Immune 1"


def test_progress_runs_from_the_floor_to_the_next_tier():
    rank = Rank.for_elo(650)
    assert rank.name == "Gold 1"
    assert rank.progress == 50.0
    assert rank.to_next == 50
    assert rank.goal == "50 Elo to Immune"

    assert Rank.for_elo(600).progress == 0.0
    assert Rank.for_elo(699).progress == 99.0


def test_the_top_of_the_ladder_has_nowhere_left_to_point():
    rank = Rank.for_elo(950, position=2)
    assert rank.name == "Apollo"
    assert rank.progress == 100.0
    assert rank.to_next == 0
    assert "Nobody above you" in rank.goal


def test_apollo_elo_without_a_seat_is_told_what_is_actually_missing():
    """Telling a player on 2000 Elo they need more Elo would be a lie: the only thing
    between them and the badge is somebody else's place."""
    rank = Rank.for_elo(2000)
    assert rank.to_next == 0
    assert "Elo to" not in rank.goal
    assert "seats 5" in rank.goal


def test_a_win_inside_one_tier_draws_the_gain_on_the_end_of_the_bar():
    rank = Rank.for_elo(650)
    bar = rank.bar(before=620)
    assert bar["direction"] == "gained"
    assert bar["held"] == 20.0
    assert bar["changed"] == 30.0


def test_a_loss_inside_one_tier_draws_what_was_given_back():
    rank = Rank.for_elo(620)
    bar = rank.bar(before=650)
    assert bar["direction"] == "lost"
    assert bar["held"] == 20.0
    assert bar["changed"] == 30.0


def test_a_promotion_fills_the_new_tier_from_empty():
    """The Elo they came in on belongs to the tier below, so it pins to this bar's left
    edge instead of rendering as a negative width."""
    rank = Rank.for_elo(610)
    bar = rank.bar(before=590)
    assert rank.name == "Gold 1"
    assert bar["held"] == 0.0
    assert bar["changed"] == 10.0
    assert bar["direction"] == "gained"


def test_a_demotion_gives_ground_back_from_a_full_bar():
    rank = Rank.for_elo(590)
    bar = rank.bar(before=610)
    assert rank.name == "Gold"
    assert bar["held"] == 90.0
    assert bar["changed"] == 10.0
    assert bar["direction"] == "lost"


def test_a_bar_never_runs_past_its_own_tier():
    """Someone sitting on Apollo Elo without a seat is drawn on Immune 1, whose ceiling
    their Elo is well past."""
    rank = Rank.for_elo(2000)
    assert rank.progress == 100.0
    assert rank.bar(before=1900)["held"] == 100.0
