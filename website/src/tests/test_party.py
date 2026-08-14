"""Party membership and party-aware matchmaking."""

from time import time

from sqlmodel import Session

from src.core import db as core_db
from src.core import models as data
from src.core.settings import settings

DUO_BIT = 2**3  # gamemode 3, "Casual Duo" — playersPerTeam=2, the one mode two
# people can actually stay together on a single team for.
SOLO_BIT = 2**0  # gamemode 0, "Competitive" — playersPerTeam=1.


def test_invite_creates_a_party_and_adds_the_invite(client, login, user_factory):
    leader, invitee = user_factory(), user_factory()
    login(client, leader)

    r = client.post("/party/invite", params={"target": invitee.steam64})
    assert r.status_code == 200

    with Session(core_db.engine) as s:
        party = s.query(data.Party).first()
        assert party.steam64 == leader.steam64
        assert party.data["members"] == [str(leader.steam64)]
        assert party.data["invites"] == [str(invitee.steam64)]


def test_non_owner_cannot_invite(client, login, user_factory):
    leader, member, outsider = user_factory(), user_factory(), user_factory()
    login(client, leader)
    client.post("/party/invite", params={"target": member.steam64})

    login(client, member)
    partyId = _first_party_id()
    client.post("/party/accept", params={"partyId": partyId})

    r = client.post("/party/invite", params={"target": outsider.steam64})
    assert r.status_code == 401


def test_accept_moves_invitee_into_members(client, login, user_factory):
    leader, invitee = user_factory(), user_factory()
    login(client, leader)
    client.post("/party/invite", params={"target": invitee.steam64})
    partyId = _first_party_id()

    login(client, invitee)
    r = client.post("/party/accept", params={"partyId": partyId})
    assert r.status_code == 200

    with Session(core_db.engine) as s:
        party = s.query(data.Party).where(data.Party.id == partyId).first()
        assert str(invitee.steam64) in party.data["members"]
        assert party.data["invites"] == []


def test_accept_leaves_any_previous_party_first(client, login, user_factory):
    firstLeader, secondLeader, floater = user_factory(), user_factory(), user_factory()
    login(client, firstLeader)
    client.post("/party/invite", params={"target": floater.steam64})
    firstPartyId = _first_party_id()

    login(client, floater)
    client.post("/party/accept", params={"partyId": firstPartyId})

    login(client, secondLeader)
    client.post("/party/invite", params={"target": floater.steam64})
    with Session(core_db.engine) as s:
        secondPartyId = (
            s.query(data.Party).where(data.Party.steam64 == secondLeader.steam64).first().id
        )

    login(client, floater)
    r = client.post("/party/accept", params={"partyId": secondPartyId})
    assert r.status_code == 200

    with Session(core_db.engine) as s:
        first = s.query(data.Party).where(data.Party.id == firstPartyId).first()
        second = s.query(data.Party).where(data.Party.id == secondPartyId).first()
        assert str(floater.steam64) not in first.data["members"]
        assert str(floater.steam64) in second.data["members"]


def test_decline_removes_the_invite_without_joining(client, login, user_factory):
    leader, invitee = user_factory(), user_factory()
    login(client, leader)
    client.post("/party/invite", params={"target": invitee.steam64})
    partyId = _first_party_id()

    login(client, invitee)
    r = client.post("/party/decline", params={"partyId": partyId})
    assert r.status_code == 200

    with Session(core_db.engine) as s:
        party = s.query(data.Party).where(data.Party.id == partyId).first()
        assert str(invitee.steam64) not in party.data["members"]
        assert party.data["invites"] == []


def test_leader_leaving_promotes_the_next_member(client, login, user_factory):
    leader, member = user_factory(), user_factory()
    login(client, leader)
    client.post("/party/invite", params={"target": member.steam64})
    partyId = _first_party_id()

    login(client, member)
    client.post("/party/accept", params={"partyId": partyId})

    login(client, leader)
    r = client.post("/party/leave")
    assert r.status_code == 200

    with Session(core_db.engine) as s:
        party = s.query(data.Party).where(data.Party.id == partyId).first()
        assert party.steam64 == member.steam64
        assert party.data["members"] == [str(member.steam64)]


def test_last_member_leaving_disbands_the_party(client, login, user_factory):
    leader = user_factory()
    login(client, leader)
    client.post("/party/invite", params={"target": user_factory().steam64})
    partyId = _first_party_id()

    r = client.post("/party/leave")
    assert r.status_code == 200

    with Session(core_db.engine) as s:
        assert s.query(data.Party).where(data.Party.id == partyId).first() is None


def test_party_status_reports_roster_and_invites(client, login, user_factory):
    leader, invitee = user_factory(), user_factory()
    login(client, leader)
    client.post("/party/invite", params={"target": invitee.steam64})

    r = client.get("/party/")
    assert r.status_code == 200
    body = r.json()
    assert body["party"]["isOwner"] is True
    assert [m["steam64"] for m in body["party"]["members"]] == [str(leader.steam64)]

    login(client, invitee)
    body = client.get("/party/").json()
    assert body["party"] is None
    assert [i["id"] for i in body["invites"]]


def test_non_owner_member_cannot_start_the_queue(client, login, user_factory):
    leader, member = user_factory(), user_factory()
    login(client, leader)
    client.post("/party/invite", params={"target": member.steam64})
    partyId = _first_party_id()

    login(client, member)
    client.post("/party/accept", params={"partyId": partyId})

    r = client.get(
        "/queue/heartbeat", params={"gamemode": DUO_BIT, "region": "paris"}
    )
    assert r.status_code == 403


def test_party_queues_together_via_the_leaders_heartbeat(client, login, user_factory):
    """The leader's single heartbeat call queues the whole party, sharing one
    partyId — that's what keeps them together once matchmaking.attemptCreateMatch
    fills teams (covered directly in test_matchmaking.py)."""
    leader, member = user_factory(), user_factory()
    login(client, leader)
    client.post("/party/invite", params={"target": member.steam64})
    partyId = _first_party_id()
    login(client, member)
    client.post("/party/accept", params={"partyId": partyId})

    login(client, leader)
    r = client.get("/queue/heartbeat", params={"gamemode": DUO_BIT, "region": "paris"})
    assert r.status_code == 200
    assert r.json().get("matchid") is None  # not enough players queued yet

    with Session(core_db.engine) as s:
        heartbeats = {hb.steam64: hb.partyId for hb in s.query(data.UserHB).all()}
        assert heartbeats[leader.steam64] == partyId
        assert heartbeats[member.steam64] == partyId


def test_a_party_nobody_has_looked_at_disbands(client, login, user_factory):
    """Everyone closing their tab has to end the party, or a player is still stuck
    in yesterday's group the next time they open /play."""
    leader, member = _duo(client, login, user_factory)
    partyId = _first_party_id()

    with Session(core_db.engine) as s:
        party = s.query(data.Party).where(data.Party.id == partyId).first()
        party.lastSeen = int(time()) - settings.party_timeout - 1
        s.add(party)
        s.commit()

    login(client, member)
    assert client.get("/party/").json()["party"] is None

    with Session(core_db.engine) as s:
        assert s.query(data.Party).count() == 0


def test_looking_at_a_party_keeps_it_alive(client, login, user_factory):
    leader, member = _duo(client, login, user_factory)
    partyId = _first_party_id()

    with Session(core_db.engine) as s:
        stale = int(time()) - settings.party_timeout + 5
        party = s.query(data.Party).where(data.Party.id == partyId).first()
        party.lastSeen = stale
        s.add(party)
        s.commit()

    login(client, leader)
    assert client.get("/party/").json()["party"]["id"] == partyId

    with Session(core_db.engine) as s:
        party = s.query(data.Party).where(data.Party.id == partyId).first()
        assert party.lastSeen > stale


def test_a_party_too_big_for_every_picked_mode_is_refused(client, login, user_factory):
    """Without this the pair queues happily and simply never matches — matchmaking
    skips a party that can't fit on one team, so nothing ever tells them why."""
    leader, member = _duo(client, login, user_factory)

    login(client, leader)
    r = client.get("/queue/heartbeat", params={"gamemode": SOLO_BIT, "region": "paris"})

    assert r.status_code == 400
    assert "too big" in r.text
    with Session(core_db.engine) as s:
        assert s.query(data.UserHB).count() == 0


def test_a_party_keeps_only_the_picked_modes_it_fits(client, login, user_factory):
    leader, member = _duo(client, login, user_factory)

    login(client, leader)
    r = client.get(
        "/queue/heartbeat", params={"gamemode": SOLO_BIT | DUO_BIT, "region": "paris"}
    )

    assert r.status_code == 200
    with Session(core_db.engine) as s:
        assert {hb.gamemode for hb in s.query(data.UserHB).all()} == {DUO_BIT}


def test_a_solo_player_may_still_queue_a_one_per_team_mode(client, login, user_factory):
    login(client, user_factory())
    r = client.get("/queue/heartbeat", params={"gamemode": SOLO_BIT, "region": "paris"})

    assert r.status_code == 200
    with Session(core_db.engine) as s:
        assert s.query(data.UserHB).count() == 1


def _duo(client, login, user_factory):
    """A leader and one accepted member, both logged out again at the end."""
    leader, member = user_factory(), user_factory()
    login(client, leader)
    client.post("/party/invite", params={"target": member.steam64})
    partyId = _first_party_id()
    login(client, member)
    client.post("/party/accept", params={"partyId": partyId})
    return leader, member


def _first_party_id() -> int:
    with Session(core_db.engine) as s:
        return s.query(data.Party).first().id
