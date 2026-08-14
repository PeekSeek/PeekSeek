"""Party membership: invite, accept/decline, leave. Queueing the whole party together
happens in /queue/heartbeat (index.py), which looks the caller's party up itself —
this router only manages who's in one.
"""

from fastapi import APIRouter, HTTPException
from fastapi.responses import PlainTextResponse

from src.core.db import open as opendb
from src.core import models as data
from src.dependencies import ApiSteam64

router = APIRouter(prefix="/party")


@router.get("/")
def party_status(steam64: ApiSteam64):
    """Polled by the play page: your party's roster, your pending invites, and
    (so a non-owner member's tab knows to redirect once the leader queues) whether
    you're already in a match."""
    with opendb() as db:
        party = data.Party.find(db, steam64)
        invites = data.Party.pendingInvites(db, steam64)
        return {
            "matchId": data.User.getCurrentMatchId(steam64),
            "party": (
                None
                if not party
                else {
                    "id": party.id,
                    "isOwner": str(party.steam64) == str(steam64),
                    "members": [
                        data.User.getSteamData(int(s)).toDict() | {"steam64": s}
                        for s in party.data.get("members", [])
                    ],
                }
            ),
            "invites": [
                {"id": p.id, "from": data.User.getSteamData(p.steam64).toDict()}
                for p in invites
            ],
        }


@router.post("/invite")
def party_invite(steam64: ApiSteam64, target: int):
    if target == steam64:
        raise HTTPException(status_code=400, detail="You can't invite yourself.")

    with opendb() as db:
        party = data.Party.find(db, steam64)
        if party and str(party.steam64) != str(steam64):
            raise HTTPException(
                status_code=401, detail="Only the party leader can invite."
            )

        if not party:
            party = data.Party(
                steam64=steam64, data={"members": [str(steam64)], "invites": []}
            )
            db.add(party)
            db.flush()

        members = party.data.get("members", [])
        invites = party.data.get("invites", [])
        if str(target) in members:
            raise HTTPException(status_code=400, detail="Already in your party.")
        if str(target) not in invites:
            invites = [*invites, str(target)]
            db.query(data.Party).where(data.Party.id == party.id).update(
                {"data": party.data | {"members": members, "invites": invites}}
            )

        targetUser = db.query(data.User).where(data.User.steam64 == target).first()
        if targetUser:
            targetUser.notify(
                "Party invite",
                f"{data.User.getSteamData(steam64).name} invited you to their party.",
            )
    return PlainTextResponse("success")


@router.post("/accept")
def party_accept(steam64: ApiSteam64, partyId: int):
    with opendb() as db:
        target = db.query(data.Party).where(data.Party.id == partyId).first()
        if not target or str(steam64) not in target.data.get("invites", []):
            raise HTTPException(status_code=404, detail="No such invite.")

        _leaveCurrentParty(db, steam64)

        members = [*target.data.get("members", []), str(steam64)]
        invites = [s for s in target.data.get("invites", []) if s != str(steam64)]
        db.query(data.Party).where(data.Party.id == partyId).update(
            {"data": target.data | {"members": members, "invites": invites}}
        )
    return PlainTextResponse("success")


@router.post("/decline")
def party_decline(steam64: ApiSteam64, partyId: int):
    with opendb() as db:
        target = db.query(data.Party).where(data.Party.id == partyId).first()
        if not target:
            raise HTTPException(status_code=404, detail="No such invite.")
        invites = [s for s in target.data.get("invites", []) if s != str(steam64)]
        db.query(data.Party).where(data.Party.id == partyId).update(
            {"data": target.data | {"invites": invites}}
        )
    return PlainTextResponse("success")


@router.post("/leave")
def party_leave(steam64: ApiSteam64):
    with opendb() as db:
        _leaveCurrentParty(db, steam64)
    data.User.removeFromQueue(steam64)
    return PlainTextResponse("success")


def _leaveCurrentParty(db, steam64: int) -> None:
    """Drop the caller from their current party, promoting the next member to
    leader (or disbanding it if they were the last one). Called both from /leave and
    before /accept, since a player can only ever be in one party at a time."""
    party = data.Party.find(db, steam64)
    if not party:
        return

    members = [s for s in party.data.get("members", []) if s != str(steam64)]
    if not members:
        db.query(data.Party).where(data.Party.id == party.id).delete()
        return

    newOwner = int(members[0]) if str(party.steam64) == str(steam64) else party.steam64
    db.query(data.Party).where(data.Party.id == party.id).update(
        {"steam64": newOwner, "data": party.data | {"members": members}}
    )
