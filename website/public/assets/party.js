/// Polls party membership + pending invites for the play page, and redirects
/// anyone in a party the instant a match is found — including a non-owner member,
/// whose own client never calls /queue/heartbeat itself, so nothing else would
/// ever send them to /match/.
let partyRedirected = false;

function renderParty(state) {
    window.partyBlocksQueue = !!(state.party && !state.party.isOwner);

    // playupdated.js owns the gamemode tiles and reads this to rule out the modes
    // that can't seat the whole party. Guarded because this file is loaded first.
    window.partySize = state.party ? state.party.members.length : 1;
    if (typeof updateOptions === "function") updateOptions();

    const membersEl = document.getElementById("partyMembers");
    const inviteRow = document.getElementById("partyInviteRow");
    const leaveBtn = document.getElementById("partyLeave");
    const invitesEl = document.getElementById("partyInvitesIncoming");
    const notice = document.getElementById("partyNotice");

    if (state.party) {
        membersEl.innerHTML = state.party.members
            .map(m => `<a class="userProfile"><img src="${m.avatar}"><span>${m.name}</span></a>`)
            .join("");
        leaveBtn.style.display = "flex";
        inviteRow.style.display = state.party.isOwner ? "flex" : "none";
        notice.style.display = state.party.isOwner ? "none" : "flex";
    } else {
        membersEl.innerHTML = "";
        leaveBtn.style.display = "none";
        inviteRow.style.display = "flex";
        notice.style.display = "none";
    }

    invitesEl.innerHTML = state.invites
        .map(i => `<div class="partyInvite">` +
            `<span><b>${i.from.name}</b> invited you to their party</span>` +
            `<span class="partyInviteActions">` +
            `<button type="button" onclick="respondToInvite(${i.id}, true)">Accept</button>` +
            `<button type="button" onclick="respondToInvite(${i.id}, false)">Decline</button>` +
            `</span></div>`)
        .join("");

    if (state.matchId && !partyRedirected) {
        partyRedirected = true;
        window.location.href = "/match/";
    }
}

function pollParty() {
    api.get("/party/").then(r => r.json()).then(renderParty).catch(() => {});
}

function invitePartyMember() {
    const input = document.getElementById("partyInviteInput");
    const target = input.value.trim();
    if (!target) return;
    api.post("/party/invite", { target }).then(r => {
        if (r.ok) {
            input.value = "";
            pollParty();
        } else {
            r.text().then(msg => setError(msg));
        }
    });
}

function respondToInvite(partyId, accept) {
    api.post(accept ? "/party/accept" : "/party/decline", { partyId }).then(pollParty);
}

function leaveParty() {
    api.post("/party/leave").then(pollParty);
}

setInterval(pollParty, 3000);
pollParty();
