"""The `/server/v1/*` contract, driven the way a real game server drives it.

Most of these walk the session loop rather than poking the database, because the loop
is the thing that has to be right: a match that is never handed over, handed over
twice, or left behind when a server disappears is a match a player is stuck in.
"""

from time import time

import pytest

from src.core import models as data
from src.core.db import open as opendb
from src.core.settings import settings

SERVER_CODE = "90071992547409931"
GAMEMODE_BIT = 1  # gamemode 0, Competitive 1v1
DECIDED = {"ballistics": True, "map": "Alpha Valley", "movement": True}


@pytest.fixture
def gameserver(client):
    """A client whose IP is allow-listed as a paris game server."""
    data.AuthorizedServer.bootstrap({"testclient": "paris"})
    return client


def poll(gameserver, matchId=0, serverCode=SERVER_CODE):
    r = gameserver.get(
        "/server/v1/session", params={"serverCode": serverCode, "matchId": matchId}
    )
    assert r.status_code == 200, r.text
    return r.json()


def queue(*users, region="paris"):
    for user in users:
        data.User.addToQueue(user.steam64, GAMEMODE_BIT, region=region)


def makeMatch(first, second, **kwargs):
    with opendb() as db:
        match = data.Match(
            gamemode=0,
            teams=[
                {"name": "one", "users": [str(first.steam64)]},
                {"name": "two", "users": [str(second.steam64)]},
            ],
            **kwargs,
        )
        db.add(match)
        db.flush()
        db.refresh(match)
        db.expunge(match)
    return match


def endSelection(matchId, secondsAgo=1):
    """Put a match where it would be once its players have finished choosing: every
    setting decided, and the kit window closed `secondsAgo` seconds ago."""
    with opendb() as db:
        match = db.query(data.Match).where(data.Match.id == matchId).one()
        db.query(data.Match).where(data.Match.id == matchId).update(
            {
                "time": int(time()) - settings.mmconfig["selectKitsTime"] - secondsAgo,
                "meta": {k: v for k, v in match.meta.items() if k != "pickDeadline"}
                | DECIDED,
            }
        )


def test_unknown_ip_is_rejected(client):
    assert client.get("/server/v1/session").status_code == 401


def test_a_free_server_with_an_empty_queue_is_told_to_idle(gameserver):
    assert poll(gameserver) == {
        "action": "idle",
        "region": "paris",
        "queuedInRegion": 0,
    }


def test_every_answer_says_which_region_the_server_is_in(gameserver, user_factory):
    """A server only gets matches from its own region, and that region is set in the
    database rather than by the server — so a mismatch looks exactly like nobody
    queueing unless the answer says so."""
    queue(user_factory(), region="utah")

    answer = poll(gameserver)

    assert answer["region"] == "paris"
    assert answer["queuedInRegion"] == 0, "the utah player is not in this region"


def test_a_free_server_makes_the_match_that_the_queue_could_not(
    gameserver, user_factory
):
    """The inversion this API exists for: two queued players sit in the queue and
    nothing happens until a server says it is free, and then that server is holding
    the match before anyone else could have taken it."""
    first, second = user_factory(), user_factory()
    queue(first, second)

    with opendb() as db:
        assert db.query(data.Match).count() == 0, "queueing alone must not make a match"

    answer = poll(gameserver)

    assert answer["action"] == "wait"
    with opendb() as db:
        match = db.query(data.Match).one()
        assert match.claimed == SERVER_CODE
        assert match.status == data.Match.PICKING
        assert db.query(data.UserHB).count() == 0, "matched players leave the queue"


def test_a_server_is_told_to_wait_while_its_players_are_still_picking(
    gameserver, user_factory
):
    first, second = user_factory(), user_factory()
    queue(first, second)

    answer = poll(gameserver)

    assert answer["action"] == "wait"
    assert sorted(answer["waitingOn"]) == sorted(
        [str(first.steam64), str(second.steam64)]
    )
    assert answer["selectionEndsAt"] > time()


def test_a_match_is_handed_over_once_selection_is_done(gameserver, user_factory):
    first, second = user_factory(), user_factory()
    queue(first, second)
    matchId = poll(gameserver)["matchId"]
    endSelection(matchId)

    answer = poll(gameserver, matchId)

    assert answer["action"] == "host"
    assert answer["match"]["id"] == matchId
    assert answer["match"]["settings"]["map"] == "Alpha Valley"
    assert {p["steam64"] for p in answer["match"]["players"]} == {
        str(first.steam64),
        str(second.steam64),
    }
    assert answer["match"]["players"][0]["kit"], "a player is never sent an empty kit"


def test_a_match_is_only_ever_handed_over_once(gameserver, user_factory):
    """The guard against a slow poll restarting a round that is already being played."""
    first, second = user_factory(), user_factory()
    queue(first, second)
    matchId = poll(gameserver)["matchId"]
    endSelection(matchId)

    assert poll(gameserver, matchId)["action"] == "host"
    assert poll(gameserver, matchId)["action"] == "idle"
    assert poll(gameserver, matchId)["action"] == "idle"


def test_a_busy_server_is_never_given_a_second_match(gameserver, user_factory):
    first, second = user_factory(), user_factory()
    queue(first, second)
    matchId = poll(gameserver)["matchId"]

    third, fourth = user_factory(), user_factory()
    queue(third, fourth)
    poll(gameserver, matchId)

    with opendb() as db:
        assert db.query(data.Match).count() == 1
        assert db.query(data.UserHB).count() == 2, "the second pair stays queued"


def test_a_finished_match_is_aborted(gameserver, user_factory):
    match = makeMatch(user_factory(), user_factory(), matchSettings=DECIDED)
    with opendb() as db:
        db.query(data.Match).where(data.Match.id == match.id).update({"finished": True})

    answer = poll(gameserver, match.id)

    assert answer["action"] == "abort"
    assert answer["matchId"] == match.id
    assert answer["reason"] == "matchOver"


def test_an_unknown_match_is_aborted(gameserver):
    assert poll(gameserver, 4242)["reason"] == "unknownMatch"


def test_a_match_taken_over_by_another_server_is_aborted(gameserver, user_factory):
    match = makeMatch(user_factory(), user_factory(), matchSettings=DECIDED)
    match.assignTo("11111111111111111")

    assert poll(gameserver, match.id)["reason"] == "hostedElsewhere"


def test_a_server_that_reports_itself_free_releases_the_match_it_abandoned(
    gameserver, user_factory
):
    """A server's result report is a match's only "I'm done" signal. When one never
    arrives, that server saying it is free again is the only thing left to go on."""
    first, second = user_factory(), user_factory()
    match = makeMatch(first, second)
    match.assignTo(SERVER_CODE)

    poll(gameserver)

    with opendb() as db:
        assert db.query(data.Match).where(data.Match.id == match.id).one().finished
        result = (
            db.query(data.MatchResult).where(data.MatchResult.matchId == match.id).one()
        )
        assert result.data["unscored"] == "abandoned"
    assert data.User.getCurrentMatchId(first.steam64) is None


def test_a_match_nobody_could_host_is_cancelled_once_its_grace_runs_out(
    gameserver, user_factory
):
    """No server, no match. A custom or event match that no server picked up is never
    going to be played, and leaving it open is what used to lock those players out of
    matchmaking entirely.

    Only reachable through a *busy* server's poll, because a free one would have taken
    the match rather than sweeping it.
    """
    first, second = user_factory(), user_factory()
    match = makeMatch(first, second, matchSettings=DECIDED)
    endSelection(match.id, secondsAgo=settings.host_grace + 1)
    held = makeMatch(user_factory(), user_factory(), matchSettings=DECIDED)
    held.assignTo(SERVER_CODE)

    poll(gameserver, held.id)

    with opendb() as db:
        stored = db.query(data.Match).where(data.Match.id == match.id).one()
    assert stored.finished is True
    assert data.User.getCurrentMatchId(first.steam64) is None


def test_a_stranded_match_is_taken_by_a_server_rather_than_scrapped(
    gameserver, user_factory
):
    """Assignment beats the sweep. A match that is over its grace and a server that has
    just come free arrive in the same request, and the players get their match."""
    first, second = user_factory(), user_factory()
    match = makeMatch(first, second, matchSettings=DECIDED)
    endSelection(match.id, secondsAgo=settings.host_grace + 1)

    answer = poll(gameserver)

    assert answer["action"] == "host"
    assert answer["match"]["id"] == match.id


def test_a_pending_match_is_given_to_the_next_free_server(gameserver, user_factory):
    """A custom match has no server of its own — the next one to report free takes it,
    ahead of making anything new out of the queue."""
    first, second = user_factory(), user_factory()
    match = makeMatch(first, second, matchSettings=DECIDED)
    endSelection(match.id)

    answer = poll(gameserver)

    assert answer["action"] == "host"
    assert answer["match"]["id"] == match.id


def test_the_session_poll_resolves_a_stalled_pick(gameserver, user_factory):
    """Nobody's browser has to be open. A match whose players all walked away still
    moves on, because the server holding it keeps asking about it."""
    first, second = user_factory(), user_factory()
    queue(first, second)
    matchId = poll(gameserver)["matchId"]
    with opendb() as db:
        match = db.query(data.Match).where(data.Match.id == matchId).one()
        db.query(data.Match).where(data.Match.id == matchId).update(
            {"meta": match.meta | {"pickDeadline": time() - 1}}
        )

    poll(gameserver, matchId)

    with opendb() as db:
        stored = db.query(data.Match).where(data.Match.id == matchId).one()
    assert stored.meta["ballistics"] is not None


def test_a_result_scores_the_match_and_finishes_it(gameserver, user_factory):
    first, second = user_factory(), user_factory()
    queue(first, second)
    matchId = poll(gameserver)["matchId"]
    endSelection(matchId)
    poll(gameserver, matchId)

    r = gameserver.post(
        f"/server/v1/matches/{matchId}/result",
        json={
            "played": True,
            "winningTeam": "one",
            "players": [
                {"steam64": str(first.steam64), "won": True, "kills": 5, "deaths": 2},
                {"steam64": str(second.steam64), "won": False, "kills": 2, "deaths": 5},
            ],
        },
    )

    assert r.json() == {"status": "recorded", "matchId": matchId}
    with opendb() as db:
        assert db.query(data.Match).where(data.Match.id == matchId).one().finished
    assert data.Stat.getElo(first.steam64) > data.Stat.getElo(second.steam64)


def test_a_repeated_result_does_not_move_elo_twice(gameserver, user_factory):
    """A server retries this report until it is acknowledged, so the retry that
    follows a lost reply must be free."""
    first, second = user_factory(), user_factory()
    match = makeMatch(first, second, matchSettings=DECIDED)
    body = {
        "played": True,
        "winningTeam": "one",
        "players": [
            {"steam64": str(first.steam64), "won": True, "kills": 5, "deaths": 2},
            {"steam64": str(second.steam64), "won": False, "kills": 2, "deaths": 5},
        ],
    }

    gameserver.post(f"/server/v1/matches/{match.id}/result", json=body)
    elo = data.Stat.getElo(first.steam64)
    again = gameserver.post(f"/server/v1/matches/{match.id}/result", json=body)

    assert again.json()["status"] == "alreadyRecorded"
    assert data.Stat.getElo(first.steam64) == elo


def test_a_match_nobody_turned_up_for_scores_nothing(gameserver, user_factory):
    first, second = user_factory(), user_factory()
    match = makeMatch(first, second, matchSettings=DECIDED)

    r = gameserver.post(
        f"/server/v1/matches/{match.id}/result", json={"played": False, "players": []}
    )

    assert r.json()["status"] == "recorded"
    assert data.Stat.getElo(first.steam64) == data.Stat.getElo(second.steam64)
    with opendb() as db:
        stored = (
            db.query(data.MatchResult).where(data.MatchResult.matchId == match.id).one()
        )
    assert stored.data["unscored"] == "notPlayed"


def test_a_player_who_left_still_takes_the_loss(gameserver, user_factory):
    """The server reports its whole roster, including anyone who walked out, so
    leaving stopped being a way to keep your Elo."""
    first, second = user_factory(), user_factory()
    match = makeMatch(first, second, matchSettings=DECIDED)

    gameserver.post(
        f"/server/v1/matches/{match.id}/result",
        json={
            "played": True,
            "winningTeam": "one",
            "players": [
                {"steam64": str(first.steam64), "won": True, "kills": 3, "deaths": 0},
                {"steam64": str(second.steam64), "won": False, "kills": 0, "deaths": 3},
            ],
        },
    )

    assert data.Stat.getElo(second.steam64) < 300


def test_a_malformed_result_is_rejected_rather_than_silently_dropped(
    gameserver, user_factory
):
    match = makeMatch(user_factory(), user_factory(), matchSettings=DECIDED)

    r = gameserver.post(
        f"/server/v1/matches/{match.id}/result",
        json={"played": True, "players": [{"kills": 1}]},
    )

    assert r.status_code == 400
    with opendb() as db:
        assert not db.query(data.Match).where(data.Match.id == match.id).one().finished


def test_kills_append_to_the_match_result(gameserver, user_factory):
    killer, victim = user_factory(), user_factory()
    match = makeMatch(killer, victim, matchSettings=DECIDED)

    r = gameserver.post(
        f"/server/v1/matches/{match.id}/kills",
        json={"killer": str(killer.steam64), "victim": str(victim.steam64)},
    )

    assert r.status_code == 200
    with opendb() as db:
        stored = (
            db.query(data.MatchResult).where(data.MatchResult.matchId == match.id).one()
        )
    assert len(stored.data["kills"]) == 1


def test_a_malformed_body_400s_instead_of_crashing(gameserver):
    r = gameserver.post(
        "/server/v1/matches/1/kills",
        content=b"not json",
        headers={"Content-Type": "application/json"},
    )
    assert r.status_code == 400


def test_queueing_a_player_never_makes_a_match(gameserver, user_factory):
    """The lobby's heartbeat. Two players standing in it are queued, and nothing more
    happens until a game server asks for work."""
    first, second = user_factory(), user_factory()

    gameserver.put(f"/server/v1/players/{first.steam64}/queue")
    r = gameserver.put(f"/server/v1/players/{second.steam64}/queue")

    assert r.json()["matchId"] is None
    with opendb() as db:
        assert db.query(data.Match).count() == 0
        assert db.query(data.UserHB).count() == 2


def test_queueing_reports_the_match_a_player_has_been_given(gameserver, user_factory):
    first, second = user_factory(), user_factory()
    queue(first, second)
    matchId = poll(gameserver)["matchId"]

    r = gameserver.put(f"/server/v1/players/{first.steam64}/queue")

    assert r.json()["matchId"] == matchId
    assert r.json()["relayTo"] is None, "not until the match is actually being hosted"


def test_a_player_is_only_relayed_once_their_match_is_live(gameserver, user_factory):
    """`relayTo` is the lobby's whole decision: null means keep them, set means send
    them. It must not be set while the server is still waiting on picks, or players
    arrive at a server that will refuse them."""
    first, second = user_factory(), user_factory()
    queue(first, second)
    matchId = poll(gameserver)["matchId"]

    assert (
        gameserver.get(f"/server/v1/players/{first.steam64}").json()["relayTo"] is None
    )

    endSelection(matchId)
    poll(gameserver, matchId)

    assert gameserver.get(f"/server/v1/players/{first.steam64}").json()["relayTo"] == (
        SERVER_CODE
    )


def test_asking_about_a_player_does_not_queue_them(gameserver, user_factory):
    """What the lobby polls with for players who used /noqueue — asking must not
    quietly put them back in."""
    player = user_factory()

    r = gameserver.get(f"/server/v1/players/{player.steam64}")

    assert r.json() == {
        "steam64": str(player.steam64),
        "elo": 300,
        "matchId": None,
        "relayTo": None,
    }
    with opendb() as db:
        assert db.query(data.UserHB).count() == 0


def test_leaving_the_queue_removes_the_heartbeat(gameserver, user_factory):
    player = user_factory()
    data.User.addToQueue(player.steam64, GAMEMODE_BIT, region="paris")

    r = gameserver.delete(f"/server/v1/players/{player.steam64}/queue")

    assert r.status_code == 200
    with opendb() as db:
        assert db.query(data.UserHB).count() == 0


def test_player_kits_come_back_ready_to_equip(gameserver, user_factory):
    player = user_factory()

    r = gameserver.get(f"/server/v1/players/{player.steam64}/kits")

    body = r.json()
    assert set(body) == set(settings.kits["kits"])
    gun = body["meta"]["items"][0]
    assert set(gun) == {"itemId", "amount", "state"}
    assert isinstance(gun["itemId"], int)
    assert all(isinstance(byte, int) for byte in gun["state"])


def test_a_ban_needs_no_body(gameserver):
    r = gameserver.post("/server/v1/players/76561197960287930/bans")

    assert r.status_code == 200
    with opendb() as db:
        assert db.query(data.UserBan).one().reason == "Cheating"


def test_a_ban_records_the_reason_it_was_given(gameserver):
    gameserver.post(
        "/server/v1/players/76561197960287930/bans", json={"reason": "Aimbot"}
    )

    with opendb() as db:
        assert db.query(data.UserBan).one().reason == "Aimbot"


def test_a_non_numeric_steam64_is_rejected(gameserver):
    assert gameserver.post("/server/v1/players/not-a-number/bans").status_code == 422


def test_match_status_reports_where_to_send_players(gameserver, user_factory):
    first, second = user_factory(), user_factory()
    queue(first, second)
    matchId = poll(gameserver)["matchId"]

    picking = gameserver.get(f"/server/v1/matches/{matchId}").json()
    assert picking == {
        "id": matchId,
        "region": "paris",
        "status": "picking",
        "relayTo": None,
    }
    assert gameserver.get(f"/server/v1/players/{first.steam64}").json()["matchId"] == (
        matchId
    )

    endSelection(matchId)
    poll(gameserver, matchId)

    live = gameserver.get(f"/server/v1/matches/{matchId}").json()
    assert live["status"] == "live"
    assert live["relayTo"] == SERVER_CODE


def test_an_unknown_match_404s(gameserver):
    assert gameserver.get("/server/v1/matches/4242").status_code == 404
