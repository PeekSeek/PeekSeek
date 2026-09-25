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
    arrives, that server saying it is free again is the only thing left to go on.

    Handed over through the loop rather than poked into place, because only a match a
    server was actually told to host can have been abandoned by it. One it was given but
    never heard about is a delivery that failed, and gets handed back instead.
    """
    first, second = user_factory(), user_factory()
    match = makeMatch(first, second)
    match.assignTo(SERVER_CODE)
    endSelection(match.id)
    assert poll(gameserver, match.id)["action"] == "host"

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
    assert (
        r.json()["relayTo"] == SERVER_CODE
    ), "a server holds it, so there is somewhere to go"


def test_a_player_is_relayed_as_soon_as_a_server_holds_their_match(
    gameserver, user_factory
):
    """`relayTo` is the lobby's whole decision: null means keep them, set means send
    them. It is set from the moment a server holds the match, because the picks are
    made on that server: the wait answer carries the roster so the server lets them
    in, and the options so it can put the board in front of them."""
    first, second = user_factory(), user_factory()
    queue(first, second)
    wait = poll(gameserver)
    matchId = wait["matchId"]

    assert wait["action"] == "wait"
    assert {str(first.steam64), str(second.steam64)} == {
        p for team in wait["match"]["teams"] for p in team["players"]
    }
    assert wait["options"]["kits"] and wait["options"]["powerups"]
    assert wait["picked"][str(first.steam64)] == {"kit": None, "powerup": None}
    assert gameserver.get(f"/server/v1/players/{first.steam64}").json()["relayTo"] == (
        SERVER_CODE
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

    body = r.json()
    assert body["steam64"] == str(player.steam64)
    assert body["elo"] == 300
    assert body["rankIcon"]
    assert body["matchId"] is None
    assert body["relayTo"] is None
    assert body["choose"] is None
    assert body["ban"] is None
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
        "relayTo": SERVER_CODE,
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


def test_queueing_a_player_the_site_has_never_seen(gameserver):
    """The common case in a warm-up lobby, not an edge one: most people standing in
    it have never opened the website, so there is no Users row for them. Queue
    heartbeats carry steam64 as a foreign key onto Users, so without creating that
    row first this is a 500 and the player is never queued at all."""
    stranger = 76561198000000077

    r = gameserver.put(f"/server/v1/players/{stranger}/queue", params={"gamemodes": 1})

    assert r.status_code == 200, r.text
    assert r.json()["steam64"] == str(stranger)
    assert r.json()["elo"] == 300, "an unranked stranger starts at the floor"
    with opendb() as db:
        assert db.query(data.UserHB).where(data.UserHB.steam64 == stranger).count() == 1
        assert db.query(data.User).where(data.User.steam64 == stranger).count() == 1


def test_a_stranger_can_be_matched_and_scored_like_anyone_else(
    gameserver, user_factory
):
    """End to end for a player with no account: queued by the lobby, picked up by a
    free server, and scored — the path every new player takes on their first match."""
    known = user_factory()
    stranger = 76561198000000078

    gameserver.put(f"/server/v1/players/{stranger}/queue", params={"gamemodes": 1})
    queue(known)

    answer = poll(gameserver)
    assert answer["action"] in ("wait", "host"), answer
    matchId = answer.get("matchId") or answer["match"]["id"]

    r = gameserver.post(
        f"/server/v1/matches/{matchId}/result",
        json={
            "played": True,
            "winningTeam": None,
            "players": [
                {"steam64": str(stranger), "kills": 3, "deaths": 1, "win": True},
                {"steam64": str(known.steam64), "kills": 1, "deaths": 3, "win": False},
            ],
        },
    )
    assert r.status_code == 200, r.text
    assert r.json()["status"] == "recorded"
    assert data.Stat.getElo(stranger) != 300, "the stranger's Elo moved"


def test_a_lost_answer_hands_the_same_match_back_rather_than_making_another(
    gameserver, user_factory
):
    """The poll that builds a match is also the one that delivers it, so an answer that
    never arrives — a poll that timed out on a slow Steam or Discord call, a server that
    restarted — leaves a server holding a match it has never heard of.

    Asking again as a free server has to finish that delivery. Abandoning the match and
    matchmaking a fresh one is what produced the same pairing over and over, a new
    "Match started" post every few seconds, because the lobby puts those players
    straight back in the queue the moment their match stops counting.
    """
    first, second = user_factory(), user_factory()
    queue(first, second)

    made = poll(gameserver)
    assert made["action"] == "wait", made

    for user in (first, second):
        gameserver.put(
            f"/server/v1/players/{user.steam64}/queue",
            params={"gamemodes": GAMEMODE_BIT},
        )

    again = poll(gameserver)

    assert again["action"] == "wait", again
    assert again["matchId"] == made["matchId"], "a second match was built"
    with opendb() as db:
        assert db.query(data.Match).count() == 1


def test_a_second_server_polling_cannot_form_the_same_pairing_twice(gameserver):
    """Two free servers in one region both poll for work; the players must end up in
    exactly one match, with the second server told to idle.

    This covers the sequential case only. The bug that produced two matches for one
    pairing is a race between the two polls, and it cannot be reproduced here: these
    run one after the other, and the SQLite the tests use serialises writes through a
    single connection anyway. What fixes the race is that taking the players out of
    the queue is now the claim itself: one delete, in one transaction, that either gets
    the whole roster or gets nothing, so the loser of the race is told so before it
    builds anything. See test_matchmaking for what each half of that guarantees.
    """
    first, second = 76561198000000201, 76561198000000202
    for steam64 in (first, second):
        gameserver.put(f"/server/v1/players/{steam64}/queue", params={"gamemodes": 1})

    firstAnswer = poll(gameserver, serverCode="90071992547409931")
    secondAnswer = poll(gameserver, serverCode="90071992547409932")

    assert firstAnswer["action"] in ("wait", "host"), firstAnswer
    assert secondAnswer["action"] == "idle", secondAnswer
    with opendb() as db:
        assert db.query(data.Match).count() == 1
        assert db.query(data.UserHB).count() == 0


def test_a_player_the_site_has_never_seen_gets_a_real_account(gameserver, monkeypatch):
    """Not just a row: the name and avatar too. The Steam lookup has to happen before
    the insert, because it is persisted with an UPDATE — done after, it silently
    stores nothing and the player is "Nameless" everywhere forever."""
    from src.core import steam

    stranger = 76561198000000203
    monkeypatch.setattr(
        steam,
        "get_steam_user_data",
        lambda s: {"personaname": "RandomJoiner", "avatarfull": "https://av/x.jpg"},
    )

    r = gameserver.put(f"/server/v1/players/{stranger}/queue", params={"gamemodes": 1})
    assert r.status_code == 200, r.text

    with opendb() as db:
        user = db.query(data.User).where(data.User.steam64 == stranger).first()
        assert user is not None
        assert user.steam_data["name"] == "RandomJoiner"
    assert data.User.getSteamData(stranger).name == "RandomJoiner"


def test_a_player_read_carries_their_steam_face(gameserver, user_factory, monkeypatch):
    """A server drawing a scoreboard needs the same name and picture the site shows,
    and asking Steam itself would need a key on every game server. So the one read a
    server already polls carries them, from the site's own cache."""
    from src.core import steam

    player = user_factory()
    monkeypatch.setattr(
        steam,
        "get_steam_user_data",
        lambda s: {"personaname": "Ferret", "avatarfull": "https://av/ferret.jpg"},
    )

    r = gameserver.get(f"/server/v1/players/{player.steam64}").json()
    assert r["name"] == "Ferret"
    assert r["avatar"] == "https://av/ferret.jpg"


def test_an_account_is_still_made_when_steam_will_not_answer(gameserver, monkeypatch):
    """Steam being down must not stop somebody playing. The account is created with
    no profile rather than with a placeholder, so the next lookup can still fill it
    in — a stored "Nameless" would never be asked about again."""
    from src.core import steam

    stranger = 76561198000000204
    monkeypatch.setattr(
        steam,
        "get_steam_user_data",
        lambda s: (_ for _ in ()).throw(RuntimeError("down")),
    )

    r = gameserver.put(f"/server/v1/players/{stranger}/queue", params={"gamemodes": 1})
    assert r.status_code == 200, r.text

    with opendb() as db:
        user = db.query(data.User).where(data.User.steam64 == stranger).first()
        assert user is not None
        assert not user.steam_data


def test_picking_in_game_hands_the_match_over_without_waiting_out_selection(
    gameserver, user_factory
):
    """The whole point of the in-game pick. Two players who lock a kit and a powerup
    from the lobby are hosted on the very next poll: no `selectKitsTime` to sit out,
    and no waiting on map/ballistics/movement picks that nobody is at a browser to
    make."""
    first, second = user_factory(), user_factory()
    queue(first, second)
    matchId = poll(gameserver)["matchId"]

    for user in (first, second):
        r = gameserver.post(
            f"/server/v1/players/{user.steam64}/decide",
            params={"kit": "sniper", "powerup": "Boom"},
        )
        assert r.status_code == 200, r.text

    answer = poll(gameserver, matchId)

    assert answer["action"] == "host"
    assert answer["match"]["settings"]["map"] is not None


def test_one_player_locking_in_does_not_start_the_match_for_everyone(
    gameserver, user_factory
):
    """Collapsing the pick phase is only safe once nobody is left who might use it."""
    first, second = user_factory(), user_factory()
    queue(first, second)
    matchId = poll(gameserver)["matchId"]

    gameserver.post(
        f"/server/v1/players/{first.steam64}/decide",
        params={"kit": "meta", "powerup": "Boom"},
    )

    assert poll(gameserver, matchId)["action"] == "wait"


def test_an_unknown_pick_is_refused_rather_than_reaching_the_match(
    gameserver, user_factory
):
    """An unknown kit category reaches Kit.getItems as a missing key, and an unknown
    powerup equips nothing — so both are refused here instead."""
    first, second = user_factory(), user_factory()
    queue(first, second)
    poll(gameserver)

    kit = gameserver.post(
        f"/server/v1/players/{first.steam64}/decide", params={"kit": "rocket"}
    )
    powerup = gameserver.post(
        f"/server/v1/players/{first.steam64}/decide", params={"powerup": "Nothing"}
    )

    assert kit.status_code == 400
    assert powerup.status_code == 400


def test_a_matched_player_is_told_what_is_left_to_choose(gameserver, user_factory):
    """`choose` is what lets the lobby put a menu in front of somebody without knowing
    anything about the gamemode."""
    first, second = user_factory(), user_factory()
    queue(first, second)
    poll(gameserver)

    choose = gameserver.get(f"/server/v1/players/{first.steam64}").json()["choose"]
    assert choose["kits"] == ["meta", "sniper"]
    assert "Boom" in choose["powerups"]
    assert choose["kit"] is None

    gameserver.post(
        f"/server/v1/players/{first.steam64}/decide", params={"kit": "meta"}
    )

    choose = gameserver.get(f"/server/v1/players/{first.steam64}").json()["choose"]
    assert choose["kit"] == "meta"
    assert choose["powerup"] is None


def test_deciding_without_a_match_is_a_404(gameserver, user_factory):
    player = user_factory()

    r = gameserver.post(
        f"/server/v1/players/{player.steam64}/decide", params={"kit": "meta"}
    )

    assert r.status_code == 404


def test_a_draw_moves_elo_on_performance_alone():
    """A gamemode that caps its overtime can end level. A draw is neither the win's
    gain nor the loss's cost: the same kills and deaths move Elo the same whether the
    player was on the side that would have won or not."""
    from src.core import matchmaking

    drawn = matchmaking._eloDelta(1000, 1000, 2, 2, won=True, draw=True)
    assert drawn == matchmaking._eloDelta(1000, 1000, 2, 2, won=False, draw=True)
    assert drawn == int(3.5 * 2) - int(2.5 * 2)
    assert (
        matchmaking._eloDelta(1000, 1000, 2, 2, won=True)
        > drawn
        > matchmaking._eloDelta(1000, 1000, 2, 2, won=False)
    )
