"""The full matchmaking loop: queue -> match -> claim -> results -> cancel."""

from time import time

from sqlmodel import Session

from src.core import db as core_db
from src.core import matchmaking
from src.core import models as data
from src.core.settings import settings

GAMEMODE_BIT = 1  # bitmask selecting gamemode 0 (Competitive, 1v1, ranked)
SERVER_CODE = "90071992547409931"  # the free game server matchmaking runs for
RANKED = settings.mmconfig["gamemodes"][0]


def test_two_queued_players_get_matched(user_factory):
    first, second = user_factory(), user_factory()
    data.User.addToQueue(first.steam64, GAMEMODE_BIT, region="paris")
    data.User.addToQueue(second.steam64, GAMEMODE_BIT, region="paris")

    assert matchmaking.matchForServer("paris", SERVER_CODE) is not None

    with Session(core_db.engine) as s:
        match = s.query(data.Match).first()
        players = {u for team in match.teams for u in team["users"]}
        assert players == {str(first.steam64), str(second.steam64)}
        assert len(match.teams) == 2
        assert s.query(data.UserHB).count() == 0, "players must leave the queue"


def test_party_members_land_on_the_same_team(user_factory, party_factory):
    """Casual Duo is 2 teams of 2 — a partied pair queueing under the same partyId
    must end up on one team together, never split across both."""
    DUO_GAMEMODE = 3
    DUO_BIT = 2**DUO_GAMEMODE
    partyA = [user_factory(), user_factory()]
    partyB = [user_factory(), user_factory()]
    idA, idB = party_factory(*partyA), party_factory(*partyB)

    for player in partyA:
        data.User.addToQueue(player.steam64, DUO_BIT, partyId=idA, region="paris")
    for player in partyB:
        data.User.addToQueue(player.steam64, DUO_BIT, partyId=idB, region="paris")

    assert matchmaking.matchForServer("paris", SERVER_CODE) is not None

    with Session(core_db.engine) as s:
        match = s.query(data.Match).first()
        assert match.gamemode == DUO_GAMEMODE
        teamA = next(t for t in match.teams if str(partyA[0].steam64) in t["users"])
        assert str(partyA[1].steam64) in teamA["users"]
        assert str(partyB[0].steam64) not in teamA["users"]


def test_oversized_party_never_matches(user_factory, party_factory):
    """A 3-person party can't fit Casual Duo's 2-per-team cap — it must simply wait
    forever rather than ever being split across teams to make it fit."""
    DUO_BIT = 2**3
    trio = [user_factory(), user_factory(), user_factory()]
    trioId = party_factory(*trio)
    for player in trio:
        data.User.addToQueue(player.steam64, DUO_BIT, partyId=trioId, region="paris")
    # Enough solo players queued alongside to fill both teams if the trio were
    # (wrongly) allowed to split.
    solo = [user_factory() for _ in range(2)]
    for player in solo:
        data.User.addToQueue(player.steam64, DUO_BIT, region="paris")

    assert matchmaking.matchForServer("paris", SERVER_CODE) is None
    with Session(core_db.engine) as s:
        assert s.query(data.Match).count() == 0


def test_lone_player_is_not_matched(user_factory):
    player = user_factory()
    data.User.addToQueue(player.steam64, GAMEMODE_BIT, region="paris")

    assert matchmaking.matchForServer("paris", SERVER_CODE) is None
    with Session(core_db.engine) as s:
        assert s.query(data.Match).count() == 0
        assert s.query(data.UserHB).count() == 1


def test_region_mismatch_does_not_match(user_factory):
    first, second = user_factory(), user_factory()
    data.User.addToQueue(first.steam64, GAMEMODE_BIT, region="paris")
    data.User.addToQueue(second.steam64, GAMEMODE_BIT, region="utah")

    assert matchmaking.matchForServer("paris", SERVER_CODE) is None
    with Session(core_db.engine) as s:
        assert s.query(data.Match).count() == 0


def test_stale_heartbeats_are_pruned(user_factory):
    player = user_factory()
    data.User.addToQueue(player.steam64, GAMEMODE_BIT, region="paris")
    with Session(core_db.engine) as s:
        s.query(data.UserHB).update({"time": int(time()) - settings.queue_timeout - 1})
        s.commit()

    assert matchmaking.matchForServer("paris", SERVER_CODE) is None
    with Session(core_db.engine) as s:
        assert s.query(data.UserHB).count() == 0


def test_match_results_update_stats_and_elo(user_factory):
    winner, loser = user_factory(), user_factory()

    results, avgElo = matchmaking.matchResults(
        RANKED,
        {"steam64": str(winner.steam64), "won": True, "kills": 5, "deaths": 2},
        {"steam64": str(loser.steam64), "won": False, "kills": 2, "deaths": 5},
    )

    assert avgElo == 300
    assert results[str(winner.steam64)]["Elo Change"] > 0
    assert results[str(loser.steam64)]["Elo Change"] < 0
    with Session(core_db.engine) as s:
        won = s.query(data.Stat).where(data.Stat.steam64 == winner.steam64).first()
        lost = s.query(data.Stat).where(data.Stat.steam64 == loser.steam64).first()
        assert won.wins == 1 and won.kills == 5 and won.elo > 300
        assert lost.losses == 1 and lost.deaths == 5 and lost.elo < 300


def test_match_results_record_headshots_and_damage(user_factory):
    winner, loser = user_factory(), user_factory()

    matchmaking.matchResults(
        RANKED,
        {
            "steam64": winner.steam64,
            "win": True,
            "kills": 5,
            "deaths": 2,
            "headshots": 3,
            "damageDealt": 620,
            "damageTaken": 140,
        },
        {"steam64": loser.steam64, "win": False, "kills": 2, "deaths": 5},
    )

    with Session(core_db.engine) as s:
        won = s.query(data.Stat).where(data.Stat.steam64 == winner.steam64).first()
        lost = s.query(data.Stat).where(data.Stat.steam64 == loser.steam64).first()
        assert won.headshots == 3
        assert won.damageDealt == 620
        assert won.damageTaken == 140
        # A payload without the newer fields (an older plugin build) must not error
        # and must simply record zero rather than crash on a missing key.
        assert lost.headshots == 0


def test_elo_never_drops_below_floor(user_factory):
    loser = user_factory()

    for _ in range(10):
        matchmaking.matchResults(
            RANKED, {"steam64": loser.steam64, "win": False, "kills": 0, "deaths": 10}
        )

    with Session(core_db.engine) as s:
        stat = s.query(data.Stat).where(data.Stat.steam64 == loser.steam64).first()
        assert stat.elo >= 100


def test_match_without_matchsettings_gets_a_pick_deadline(user_factory):
    """Matchmaking never passes matchSettings, so ballistics/movement start undecided —
    that's exactly when a deadline is needed, or a silent player could stall forever."""
    first, second = user_factory(), user_factory()
    with core_db.open() as db:
        match = data.Match(
            gamemode=0,
            teams=[
                {"name": "one", "users": [str(first.steam64)]},
                {"name": "two", "users": [str(second.steam64)]},
            ],
        )
        db.add(match)

    with Session(core_db.engine) as s:
        loaded = s.query(data.Match).first()
        assert loaded.meta["pickDeadline"] is not None
        assert loaded.meta["pickDeadline"] <= time() + settings.mmconfig["pickTimeout"]


def test_match_with_full_matchsettings_gets_no_pick_deadline(user_factory):
    first, second = user_factory(), user_factory()
    with core_db.open() as db:
        match = data.Match(
            gamemode=0,
            teams=[
                {"name": "one", "users": [str(first.steam64)]},
                {"name": "two", "users": [str(second.steam64)]},
            ],
            matchSettings={"ballistics": True, "map": "Alpha Valley", "movement": True},
        )
        db.add(match)

    with Session(core_db.engine) as s:
        assert s.query(data.Match).first().meta.get("pickDeadline") is None


def test_expired_pick_is_not_resolved_before_its_deadline(user_factory):
    first, second = user_factory(), user_factory()
    with core_db.open() as db:
        match = data.Match(
            gamemode=0,
            teams=[
                {"name": "one", "users": [str(first.steam64)]},
                {"name": "two", "users": [str(second.steam64)]},
            ],
        )
        db.add(match)

    with Session(core_db.engine) as s:
        assert s.query(data.Match).first().resolvedMetaIfExpired() is None


def test_expired_pick_auto_resolves_to_the_gamemode_default_and_advances_turn(
    user_factory,
):
    """A player who never clicks Yes/No must not be able to hang the whole match —
    once the deadline passes, the pick resolves itself to the gamemode's first
    configured option (the same fallback kits/powerups already use) and turn passes
    on, exactly as if the stalling player had picked."""
    first, second = user_factory(), user_factory()
    with core_db.open() as db:
        match = data.Match(
            gamemode=0,
            teams=[
                {"name": "one", "users": [str(first.steam64)]},
                {"name": "two", "users": [str(second.steam64)]},
            ],
        )
        db.add(match)
        db.flush()
        matchId = match.id
        db.query(data.Match).where(data.Match.id == matchId).update(
            {"meta": match.meta | {"pickDeadline": time() - 1}}
        )

    with Session(core_db.engine) as s:
        loaded = s.query(data.Match).where(data.Match.id == matchId).first()
        resolved = loaded.resolvedMetaIfExpired()
        assert resolved is not None
        assert resolved["ballistics"] == RANKED["ballistics"][0]
        assert resolved["movement"] is None  # only the first outstanding field resolves
        assert resolved["currentPick"] == str(second.steam64)
        # movement is still outstanding, so the next field gets its own full window
        assert resolved["pickDeadline"] > time()


def test_expired_pick_clears_the_deadline_once_everything_is_decided(user_factory):
    first, second = user_factory(), user_factory()
    with core_db.open() as db:
        match = data.Match(
            gamemode=0,
            teams=[
                {"name": "one", "users": [str(first.steam64)]},
                {"name": "two", "users": [str(second.steam64)]},
            ],
        )
        db.add(match)
        db.flush()
        matchId = match.id
        db.query(data.Match).where(data.Match.id == matchId).update(
            {"meta": match.meta | {"pickDeadline": time() - 1, "ballistics": True}}
        )

    with Session(core_db.engine) as s:
        loaded = s.query(data.Match).where(data.Match.id == matchId).first()
        resolved = loaded.resolvedMetaIfExpired()
        assert resolved["movement"] == RANKED["movement"][0]
        assert resolved["pickDeadline"] is None


def test_a_real_pick_resets_the_deadline_for_the_next_field(user_factory):
    first, second = user_factory(), user_factory()
    with core_db.open() as db:
        match = data.Match(
            gamemode=0,
            teams=[
                {"name": "one", "users": [str(first.steam64)]},
                {"name": "two", "users": [str(second.steam64)]},
            ],
        )
        db.add(match)
        db.flush()
        matchId = match.id

    with Session(core_db.engine) as s:
        loaded = s.query(data.Match).where(data.Match.id == matchId).first()
        result = loaded.getReturnedMeta(first.steam64, pick="1")
        assert result["ballistics"] is True
        assert result["movement"] is None
        assert result["pickDeadline"] > time()  # a fresh window for movement


def test_match_is_not_hostable_early_with_picks_outstanding(user_factory):
    first, second = user_factory(), user_factory()
    with core_db.open() as db:
        match = data.Match(
            gamemode=0,
            teams=[
                {"name": "one", "users": [str(first.steam64)]},
                {"name": "two", "users": [str(second.steam64)]},
            ],
            matchSettings={"ballistics": True, "map": "Alpha Valley", "movement": True},
        )
        db.add(match)

    with Session(core_db.engine) as s:
        loaded = s.query(data.Match).first()
        assert loaded.allKitsLocked() is False
        assert loaded.readyToHost() is False


def test_match_is_hostable_early_once_every_kit_and_powerup_is_locked(user_factory):
    """Waiting out the full `selectKitsTime` timer is pointless once nobody has
    anything left to decide — the match should be claimable immediately."""
    first, second = user_factory(), user_factory()
    with core_db.open() as db:
        match = data.Match(
            gamemode=0,
            teams=[
                {"name": "one", "users": [str(first.steam64)]},
                {"name": "two", "users": [str(second.steam64)]},
            ],
            matchSettings={"ballistics": True, "map": "Alpha Valley", "movement": True},
        )
        db.add(match)
        db.flush()
        matchId = match.id
        db.query(data.Match).where(data.Match.id == matchId).update(
            {
                "meta": match.meta
                | {
                    "userData": {
                        str(first.steam64): {"kit": "meta", "powerup": "Ammo"},
                        str(second.steam64): {"kit": "sniper", "powerup": "Doctor"},
                    }
                }
            }
        )

    with Session(core_db.engine) as s:
        loaded = s.query(data.Match).where(data.Match.id == matchId).first()
        assert loaded.allKitsLocked() is True
        assert loaded.readyToHost() is True


def test_append_kill_creates_and_grows_the_match_result(user_factory):
    killer, victim = user_factory(), user_factory()
    with core_db.open() as db:
        match = data.Match(
            gamemode=0,
            teams=[
                {"name": "one", "users": [str(killer.steam64)]},
                {"name": "two", "users": [str(victim.steam64)]},
            ],
            matchSettings={"ballistics": True, "map": "Alpha Valley", "movement": True},
        )
        db.add(match)
        db.flush()
        matchId = match.id

    data.MatchResult.appendKill(
        matchId,
        {"killer": str(killer.steam64), "victim": str(victim.steam64), "limb": "skull"},
    )
    data.MatchResult.appendKill(
        matchId,
        {"killer": str(killer.steam64), "victim": str(victim.steam64), "limb": "spine"},
    )

    with Session(core_db.engine) as s:
        result = (
            s.query(data.MatchResult).where(data.MatchResult.matchId == matchId).first()
        )
        assert result.data["map"] == "Alpha Valley"
        assert len(result.data["kills"]) == 2
        assert result.data["kills"][0]["limb"] == "skull"


def test_record_final_results_merges_into_the_same_row_as_kills(user_factory):
    killer, victim = user_factory(), user_factory()
    with core_db.open() as db:
        match = data.Match(
            gamemode=0,
            teams=[
                {"name": "one", "users": [str(killer.steam64)]},
                {"name": "two", "users": [str(victim.steam64)]},
            ],
            matchSettings={"ballistics": True, "map": "Alpha Valley", "movement": True},
        )
        db.add(match)
        db.flush()
        matchId = match.id

    data.MatchResult.appendKill(
        matchId, {"killer": str(killer.steam64), "victim": str(victim.steam64)}
    )
    data.MatchResult.recordFinalResults(
        matchId, {str(killer.steam64): {"Kills": 1, "Deaths": 0, "Won": True}}
    )

    with Session(core_db.engine) as s:
        result = (
            s.query(data.MatchResult).where(data.MatchResult.matchId == matchId).first()
        )
        assert len(result.data["kills"]) == 1
        assert result.data["results"][str(killer.steam64)]["Won"] is True


def test_latest_for_finds_matches_a_player_has_a_result_in(user_factory):
    player, other = user_factory(), user_factory()
    with core_db.open() as db:
        match = data.Match(
            gamemode=0, teams=[{"name": "one", "users": [str(player.steam64)]}]
        )
        db.add(match)
        db.flush()
        matchId = match.id

    data.MatchResult.recordFinalResults(matchId, {str(player.steam64): {"Kills": 3}})

    found = data.MatchResult.latestFor(player.steam64)
    assert len(found) == 1
    assert found[0].matchId == matchId
    assert data.MatchResult.latestFor(other.steam64) == []


def test_a_serverless_match_is_cancelled_once_its_grace_runs_out(user_factory):
    """No server, no match. Nothing else ever revisits a match nobody is hosting, so
    without this its players stay locked into it and out of matchmaking."""
    first, second = user_factory(), user_factory()
    with core_db.open() as db:
        db.add(
            data.Match(
                gamemode=0,
                teams=[
                    {"name": "one", "users": [str(first.steam64)]},
                    {"name": "two", "users": [str(second.steam64)]},
                ],
            )
        )
    with Session(core_db.engine) as s:
        s.query(data.Match).update(
            {
                "time": int(time())
                - settings.mmconfig["selectKitsTime"]
                - settings.host_grace
                - 1
            }
        )
        s.commit()

    data.Match.cancelStranded()

    with Session(core_db.engine) as s:
        assert s.query(data.Match).first().finished is True
    assert data.User.getCurrentMatchId(first.steam64) is None


def test_a_serverless_match_is_left_alone_inside_its_grace(user_factory):
    """A server coming free a moment late still gets to take the match, so the sweep
    must not scrap one that is only just past selection."""
    first, second = user_factory(), user_factory()
    with core_db.open() as db:
        db.add(
            data.Match(
                gamemode=0,
                teams=[
                    {"name": "one", "users": [str(first.steam64)]},
                    {"name": "two", "users": [str(second.steam64)]},
                ],
            )
        )
    with Session(core_db.engine) as s:
        s.query(data.Match).update(
            {"time": int(time()) - settings.mmconfig["selectKitsTime"] - 1}
        )
        s.commit()

    data.Match.cancelStranded()

    with Session(core_db.engine) as s:
        assert s.query(data.Match).first().finished is False


def test_claimed_match_released_when_its_server_polls_idle(user_factory):
    """A server whose lobby timed out with too few players abandons the match and
    goes back to reporting itself free — even if its result report never landed, that
    next free poll must still close the match out, or every player in it stays stuck
    unable to queue again."""
    first, second = user_factory(), user_factory()
    with core_db.open() as db:
        db.add(
            data.Match(
                gamemode=0,
                teams=[
                    {"name": "one", "users": [str(first.steam64)]},
                    {"name": "two", "users": [str(second.steam64)]},
                ],
            )
        )
    with Session(core_db.engine) as s:
        s.query(data.Match).update({"claimed": "90071992547409931"})
        s.commit()

    data.Match.releaseAbandoned("90071992547409931")

    with Session(core_db.engine) as s:
        assert s.query(data.Match).first().finished is True


def test_release_abandoned_ignores_the_shared_legacy_code(user_factory):
    """code="0" is shared by every server too old to send a real one — releasing on
    it could close a match a completely different old server is still playing."""
    first, second = user_factory(), user_factory()
    with core_db.open() as db:
        db.add(
            data.Match(
                gamemode=0,
                teams=[
                    {"name": "one", "users": [str(first.steam64)]},
                    {"name": "two", "users": [str(second.steam64)]},
                ],
            )
        )
    with Session(core_db.engine) as s:
        s.query(data.Match).update({"claimed": "0"})
        s.commit()

    data.Match.releaseAbandoned("0")

    with Session(core_db.engine) as s:
        assert s.query(data.Match).first().finished is False


def elo(user, value):
    """Put a player at a given Elo. Stat.append adds a delta to the 300 everyone
    starts on, so the delta is what gets stored."""
    data.Stat.append(user.steam64, "unt", elo=value - 300)


def test_teams_too_far_apart_in_elo_are_not_matched(user_factory):
    """Past max_team_elo_gap the two keep waiting for someone closer rather than being
    handed a foregone conclusion.

    Read from the setting rather than written down here: this test pinned 200 and went
    red the day the gap was widened to 400, which said nothing about the rule and only
    that the number had moved.
    """
    strong, weak = user_factory(), user_factory()
    elo(strong, 300 + settings.max_team_elo_gap + 100)
    elo(weak, 300)
    data.User.addToQueue(strong.steam64, GAMEMODE_BIT, region="paris")
    data.User.addToQueue(weak.steam64, GAMEMODE_BIT, region="paris")

    assert matchmaking.matchForServer("paris", SERVER_CODE) is None
    with Session(core_db.engine) as s:
        assert s.query(data.UserHB).count() == 2, "both stay in the queue"


def test_players_within_the_elo_gap_are_matched(user_factory):
    strong, weak = user_factory(), user_factory()
    elo(strong, 300 + settings.max_team_elo_gap - 20)
    elo(weak, 300)
    data.User.addToQueue(strong.steam64, GAMEMODE_BIT, region="paris")
    data.User.addToQueue(weak.steam64, GAMEMODE_BIT, region="paris")

    assert matchmaking.matchForServer("paris", SERVER_CODE) is not None


def test_a_closer_opponent_further_down_the_queue_is_preferred(user_factory):
    """Waiting time orders the queue, but it does not override fairness: the player at
    the front is skipped for the one who actually makes a legal match."""
    waiting, mismatch, fair = user_factory(), user_factory(), user_factory()
    elo(waiting, 300)
    elo(mismatch, 900)
    elo(fair, 350)
    for player in (waiting, mismatch, fair):
        data.User.addToQueue(player.steam64, GAMEMODE_BIT, region="paris")

    match = matchmaking.matchForServer("paris", SERVER_CODE)

    assert match is not None
    players = {u for team in match.teams for u in team["users"]}
    assert players == {str(waiting.steam64), str(fair.steam64)}


def test_the_same_two_players_are_not_rematched_straight_away(user_factory):
    """Diversity over convenience: having just played each other, these two wait for
    somebody else rather than running the same fixture back."""
    first, second = user_factory(), user_factory()
    data.User.addToQueue(first.steam64, GAMEMODE_BIT, region="paris")
    data.User.addToQueue(second.steam64, GAMEMODE_BIT, region="paris")
    assert matchmaking.matchForServer("paris", SERVER_CODE) is not None

    with Session(core_db.engine) as s:
        s.query(data.Match).update({"finished": True})
        s.commit()
    data.User.addToQueue(first.steam64, GAMEMODE_BIT, region="paris")
    data.User.addToQueue(second.steam64, GAMEMODE_BIT, region="paris")

    assert matchmaking.matchForServer("paris", SERVER_CODE) is None


def test_a_rematch_is_allowed_once_the_cooldown_has_passed(user_factory):
    first, second = user_factory(), user_factory()
    with core_db.open() as db:
        db.add(
            data.Match(
                gamemode=0,
                teams=[
                    {"name": "one", "users": [str(first.steam64)]},
                    {"name": "two", "users": [str(second.steam64)]},
                ],
            )
        )
    with Session(core_db.engine) as s:
        s.query(data.Match).update(
            {"finished": True, "time": int(time()) - settings.rematch_cooldown - 1}
        )
        s.commit()

    data.User.addToQueue(first.steam64, GAMEMODE_BIT, region="paris")
    data.User.addToQueue(second.steam64, GAMEMODE_BIT, region="paris")

    assert matchmaking.matchForServer("paris", SERVER_CODE) is not None


def test_changing_one_player_is_not_enough_to_beat_the_cooldown(user_factory):
    """(a+b vs c+d) then (a+b vs c+e): swapping d for e still leaves a and b facing c
    for a second time, so the fixture counts as a repeat."""
    DUO_BIT = 2**3  # Casual Duo: 2 teams of 2
    a, b, c, d, e = [user_factory() for _ in range(5)]
    with core_db.open() as db:
        db.add(
            data.Match(
                gamemode=3,
                teams=[
                    {"name": "ab", "users": [str(a.steam64), str(b.steam64)]},
                    {"name": "cd", "users": [str(c.steam64), str(d.steam64)]},
                ],
            )
        )
    with Session(core_db.engine) as s:
        s.query(data.Match).update({"finished": True})
        s.commit()

    for player in (a, b, c, e):
        data.User.addToQueue(player.steam64, DUO_BIT, partyId=None, region="paris")

    assert matchmaking.matchForServer("paris", SERVER_CODE) is None


def test_two_unresolvable_players_do_not_get_the_same_team_name(user_factory):
    """The bug this exists for: a match between two players whose Steam lookup fails
    comes back as "Nameless" twice, both teams get built as "team_Nameless", the game
    server groups them as one team, and nobody can shoot anybody."""
    first, second = user_factory(), user_factory()
    data.User.addToQueue(first.steam64, GAMEMODE_BIT, region="paris")
    data.User.addToQueue(second.steam64, GAMEMODE_BIT, region="paris")

    assert matchmaking.matchForServer("paris", SERVER_CODE) is not None

    with Session(core_db.engine) as s:
        match = s.query(data.Match).first()
        names = [team["name"] for team in match.teams]
        assert len(set(names)) == len(names), names


def test_team_names_are_made_distinct_however_they_arrive():
    """Enforced on Match itself rather than at one call site, so no creation path —
    matchmaking, a custom match, an event pairing — can get around it."""
    match = data.Match(
        gamemode=0,
        teams=[
            {"name": "team_Nameless", "users": ["1"]},
            {"name": "team_Nameless", "users": ["2"]},
            {"name": "team_Nameless", "users": ["3"]},
        ],
    )
    names = [team["name"] for team in match.teams]

    assert names[0] == "team_Nameless", "the first one keeps the readable name"
    assert len(set(names)) == 3, names
    assert all(len(n) <= data.Match.NAME_LIMIT for n in names), names


def test_long_names_that_truncate_alike_still_end_up_distinct():
    """Cutting to length is itself a way to collide, so the cut has to happen before
    the comparison, not after it."""
    match = data.Match(
        gamemode=0,
        teams=[
            {"name": "team_Christopher_Alpha", "users": ["1"]},
            {"name": "team_Christopher_Beta", "users": ["2"]},
        ],
    )
    names = [team["name"] for team in match.teams]

    assert len(set(names)) == 2, names
    assert all(len(n) <= data.Match.NAME_LIMIT for n in names), names


def test_a_team_with_no_name_still_gets_one():
    match = data.Match(
        gamemode=0,
        teams=[{"name": "", "users": ["1"]}, {"users": ["2"]}],
    )
    names = [team["name"] for team in match.teams]

    assert all(names), names
    assert len(set(names)) == 2, names


def test_a_queued_player_already_in_a_match_is_never_matched_again(user_factory):
    """The queue is not a list of who is free. Lobby servers re-queue everyone standing
    in them on a timer, so a player who was matched a second ago is still standing in
    the lobby and still sending heartbeats. Pairing them again is how one set of players
    ended up in two matches at once."""
    first, second, third = user_factory(), user_factory(), user_factory()
    for player in (first, second, third):
        data.User.addToQueue(player.steam64, GAMEMODE_BIT, region="paris")

    assert matchmaking.matchForServer("paris", SERVER_CODE) is not None

    # Both of the matched players heartbeat again from the lobby they are still in.
    matched = [int(u) for team in _onlyMatch().teams for u in team["users"]]
    for steam64 in matched:
        data.User.addToQueue(steam64, GAMEMODE_BIT, region="paris")

    assert matchmaking.matchForServer("paris", SERVER_CODE) is None
    with Session(core_db.engine) as s:
        assert s.query(data.Match).count() == 1


def test_a_roster_the_queue_will_not_give_up_creates_nothing(user_factory):
    """What the second of two servers polling at the same instant runs into: it scanned
    the queue, liked a roster, and by the time it went to take those players another
    server already had them. It must create nothing at all: not a match, and not the
    "Match started" post that goes out with one."""
    first, second = user_factory(), user_factory()
    data.User.addToQueue(first.steam64, GAMEMODE_BIT, region="paris")
    data.User.addToQueue(second.steam64, GAMEMODE_BIT, region="paris")

    players = [first.steam64, second.steam64]
    assert data.UserHB.claim(players) is True

    assert matchmaking._createMatch(0, players, "paris", SERVER_CODE) is None
    with Session(core_db.engine) as s:
        assert s.query(data.Match).count() == 0


def test_half_a_roster_left_in_the_queue_is_put_back(user_factory):
    """A claim is all of them or none. Taking only the players still queued would strand
    them: their roster went to another server, and they would be waiting in a queue they
    are no longer in."""
    first, second = user_factory(), user_factory()
    data.User.addToQueue(first.steam64, GAMEMODE_BIT, region="paris")
    data.User.addToQueue(second.steam64, GAMEMODE_BIT, region="paris")
    data.User.removeFromQueue(first.steam64)

    assert data.UserHB.claim([first.steam64, second.steam64]) is False
    with Session(core_db.engine) as s:
        assert s.query(data.UserHB).count() == 1, "the one still queued stayed queued"


def _onlyMatch() -> data.Match:
    with Session(core_db.engine) as s:
        match = s.query(data.Match).first()
        s.expunge(match)
        return match
