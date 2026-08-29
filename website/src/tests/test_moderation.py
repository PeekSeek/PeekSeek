"""Issuing a ban, and undoing a cheat's effect on everyone else's record.

The refund half of this was run by hand against production before it was code: one
account, 20 matches, 11 players refunded. These pin the behaviour that run established,
because it is destructive and there is no second chance to get the arithmetic right.
"""

from datetime import datetime, timezone
from time import time

import pytest
from sqlmodel import Session

from src.core import moderation
from src.core import models as data
from src.core.settings import settings
from src.core.db import engine, open as opendb


@pytest.fixture(autouse=True)
def _noDiscord(monkeypatch):
    """No test posts to Discord. Returns True so the caller's success path still runs."""
    monkeypatch.setattr(
        "src.core.notify.send_discord_channel", lambda *a, **k: True, raising=False
    )


def result(matchId: int, lines: dict, endTime=None):
    """One MatchResults row, in the shape matchResults() writes."""
    with Session(engine) as session:
        session.add(
            data.MatchResult(
                matchId=None,
                region="paris",
                gamemode=0,
                startTime=endTime or datetime.now(timezone.utc),
                endTime=endTime or datetime.now(timezone.utc),
                data={"kills": [], "results": lines},
            )
        )
        session.commit()


def line(elo, kills=0, deaths=0, won=False):
    return {
        "Elo Change": elo,
        "Current Elo": 300,
        "Kills": kills,
        "Deaths": deaths,
        "Headshots": 0,
        "Won": won,
    }


def stats(steam64, **values):
    with Session(engine) as session:
        session.add(data.Stat(steam64=steam64, game="unt", **values))
        session.commit()


def test_a_preset_duration_beats_a_passed_one():
    """Picking Toxicity is choosing not to decide the length; a stray days must not win."""
    week = moderation.endTimeFor("toxicity", days=365)

    assert abs(week - (time() + 7 * moderation.DAY)) < 5


def test_cheating_is_permanent():
    assert moderation.endTimeFor("cheating", None) == data.UserBan.PERMANENT


def test_a_custom_ban_uses_its_days():
    assert abs(moderation.endTimeFor(None, 3) - (time() + 3 * moderation.DAY)) < 5


def test_the_note_always_names_who_issued_it():
    assert (
        moderation.noteWithAuthor("clip in #reports", "Homeless", 76561198000000001)
        == "by Homeless/76561198000000001 clip in #reports"
    )


def test_an_empty_note_is_still_attributed():
    assert moderation.noteWithAuthor("  ", "TSB", 1) == "by TSB/1"


def test_preview_counts_matches_and_refunds(user_factory):
    cheat = user_factory()
    victim = user_factory()
    stats(cheat.steam64, elo=700, kills=10, deaths=2, wins=2, losses=0)
    stats(victim.steam64, elo=276, kills=1, deaths=8, wins=0, losses=2)

    result(1, {str(cheat.steam64): line(20, won=True), str(victim.steam64): line(-24)})
    result(2, {str(cheat.steam64): line(18, won=True), str(victim.steam64): line(-19)})

    preview = moderation.previewBan(cheat.steam64)

    assert preview["matches"] == 2
    assert preview["players"] == 1
    # Both directions: the victim lost 43 across the two, so they are owed 43 back.
    assert preview["eloReturned"] == 43
    assert preview["refunds"] == [{"steam64": str(victim.steam64), "elo": 43}]


def test_preview_writes_nothing(user_factory):
    cheat = user_factory()
    victim = user_factory()
    stats(cheat.steam64, elo=700)
    stats(victim.steam64, elo=276)
    result(1, {str(cheat.steam64): line(20, won=True), str(victim.steam64): line(-24)})

    moderation.previewBan(cheat.steam64)

    with opendb() as db:
        assert db.query(data.Stat).where(data.Stat.steam64 == victim.steam64).first().elo == 276
        assert db.query(data.UserBan).count() == 0


def test_a_cheating_ban_refunds_both_directions(user_factory):
    """Somebody who beat the cheat loses that gain: the match should never have counted."""
    cheat = user_factory()
    loser = user_factory()
    winner = user_factory()
    stats(cheat.steam64, elo=700)
    stats(loser.steam64, elo=276, losses=1, deaths=8)
    stats(winner.steam64, elo=333, wins=1, kills=5)

    result(
        1,
        {
            str(cheat.steam64): line(20, won=True),
            str(loser.steam64): line(-24, deaths=8),
            str(winner.steam64): line(33, kills=5, won=True),
        },
    )

    moderation.issueBan(cheat.steam64, preset="cheating", byName="Homeless", bySteam64=1)

    with opendb() as db:
        assert db.query(data.Stat).where(data.Stat.steam64 == loser.steam64).first().elo == 300
        assert db.query(data.Stat).where(data.Stat.steam64 == winner.steam64).first().elo == 300
        # Counters come back too, not just Elo.
        assert db.query(data.Stat).where(data.Stat.steam64 == loser.steam64).first().deaths == 0
        assert db.query(data.Stat).where(data.Stat.steam64 == winner.steam64).first().wins == 0


def test_a_cheating_ban_deletes_their_stats(user_factory):
    cheat = user_factory()
    stats(cheat.steam64, elo=700, kills=179)

    moderation.issueBan(cheat.steam64, preset="cheating")

    with opendb() as db:
        assert db.query(data.Stat).where(data.Stat.steam64 == cheat.steam64).count() == 0


def test_a_cheating_ban_voids_the_matches(user_factory):
    """Voided matches leave the key the profile and Elo graph read, keeping the record."""
    cheat = user_factory()
    victim = user_factory()
    stats(cheat.steam64, elo=700)
    stats(victim.steam64, elo=276)
    result(1, {str(cheat.steam64): line(20, won=True), str(victim.steam64): line(-24)})

    moderation.issueBan(cheat.steam64, preset="cheating")

    with opendb() as db:
        row = db.query(data.MatchResult).first()
    assert "results" not in row.data
    assert str(cheat.steam64) in row.data["voidedResults"]
    assert row.data["voided"] == "cheating"
    assert data.MatchResult.latestFor(victim.steam64) == []


def test_a_toxicity_ban_touches_nobody_elses_stats(user_factory):
    cheat = user_factory()
    victim = user_factory()
    stats(cheat.steam64, elo=700)
    stats(victim.steam64, elo=276)
    result(1, {str(cheat.steam64): line(20, won=True), str(victim.steam64): line(-24)})

    moderation.issueBan(cheat.steam64, preset="toxicity")

    with opendb() as db:
        assert db.query(data.Stat).where(data.Stat.steam64 == victim.steam64).first().elo == 276
        # Their own stats survive a ban that is not about cheating.
        assert db.query(data.Stat).where(data.Stat.steam64 == cheat.steam64).count() == 1
        assert db.query(data.MatchResult).first().data.get("results") is not None


def test_a_ban_drops_them_from_the_queue(user_factory):
    player = user_factory()
    data.User.addToQueue(player.steam64, 1)

    moderation.issueBan(player.steam64, preset="toxicity")

    with opendb() as db:
        assert db.query(data.UserHB).count() == 0


def test_a_second_shorter_ban_adds_no_row(user_factory):
    """A player already serving a permanent ban is not banned harder by a week."""
    player = user_factory()
    moderation.issueBan(player.steam64, preset="cheating")

    moderation.issueBan(player.steam64, preset="toxicity")

    with opendb() as db:
        assert db.query(data.UserBan).count() == 1


def test_a_longer_ban_does_add_a_row(user_factory):
    player = user_factory()
    moderation.issueBan(player.steam64, preset="toxicity")

    moderation.issueBan(player.steam64, preset="matchfixing")

    with opendb() as db:
        assert db.query(data.UserBan).count() == 2


def test_a_ban_needs_a_reason(user_factory):
    player = user_factory()

    with pytest.raises(ValueError):
        moderation.issueBan(player.steam64, days=3)


def test_an_unknown_preset_is_refused(user_factory):
    player = user_factory()

    with pytest.raises(ValueError):
        moderation.issueBan(player.steam64, preset="vibes")


def test_an_overturn_is_announced_to_staff(user_factory, monkeypatch):
    """Louder than issuing a ban on purpose: undoing one reverses a colleague's call,
    and staff finding out by accident is how the same player gets banned again."""
    posted = {}

    def capture(channel, content=None, embed=None, allowed_mentions=None):
        posted.update(
            channel=channel, content=content, embed=embed, mentions=allowed_mentions
        )
        return True

    monkeypatch.setattr("src.core.notify.send_discord_channel", capture)

    player = user_factory()
    moderation.issueBan(player.steam64, preset="toxicity", note="said a thing")
    banId = data.UserBan.activeFor(player.steam64).id

    moderation.overturnBan(banId, byName="Homeless", bySteam64=1)

    assert posted["channel"] == settings.overturn_channel
    # The ping has to be in content: a mention inside an embed notifies nobody.
    assert posted["content"] == f"<@&{settings.staff_role}>"
    assert posted["mentions"] == {"roles": [settings.staff_role]}
    assert "Homeless" in posted["embed"]["description"]
    # The note as it read before the overturn marker was appended.
    assert "said a thing" in posted["embed"]["fields"][0]["value"]


def test_the_overturn_log_is_not_the_report_channel(user_factory):
    assert settings.overturn_channel != settings.report_channel
