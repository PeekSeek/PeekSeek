let canDisplayTurn = true;
let canInterval = true;

/// Unturned takes the server code on the launch command line; there is no URL
/// scheme that joins by code directly.
function connectUrl(serverCode) {
    return `steam://run/304930//+connect ${serverCode}`;
}

let launched = false;
function launchOnce(serverCode) {
    if (launched) return;
    launched = true;
    window.location = connectUrl(serverCode);
}

function getTeam(s64) {
    for (const team of data.teams) {
        if (team.users.includes(s64)) return team.name;
    }
}

/// A gamemode with only one kit or one powerup has nothing to pick, so a player is never
/// "still picking" on that axis — mirrors Match.allKitsLocked() on the backend. Picking
/// doesn't stop anyone from changing their mind afterwards (see setKit/setPowerup) — this
/// only tracks whether a real choice has been made yet, not whether it's final.
function hasPicked(s64) {
    const config = data.config;
    const userData = (data.params.userData || {})[s64] || {};
    const needsKit = (config.kitsAllowed || []).length > 1;
    const needsPowerup = (config.powerups || []).length > 1;
    return (!needsKit || userData.kit) && (!needsPowerup || userData.powerup);
}

function commitRow(name, picked) {
    return `<div class="row"><span class="name">${name}</span>` +
        `<span class="status ${picked ? "picked" : "picking"}">${picked ? "picked" : "picking…"}</span></div>`;
}

function allSteam64s() {
    return data.teams.flatMap(t => t.users);
}

/// Your own team by name always; opponents collapse to a picked-count summary until
/// `picksDone`, then expand to the same per-player rows — same reveal moment the
/// server snapshot uses for the match itself, just presented as a quiet list instead
/// of a roster grid (no avatars — the name + status is all this needs to say).
function commitListHtml(picksDone, myTeamName) {
    if (!data.config || !("users" in data)) return "";

    const myTeam = data.teams.find(t => t.name === myTeamName);
    const opponents = data.teams.filter(t => t.name !== myTeamName).flatMap(t => t.users);

    const myRows = (myTeam ? myTeam.users : [])
        .map(s64 => commitRow(data.users[s64] ? data.users[s64].name : s64, hasPicked(s64)))
        .join("");

    const oppPickedCount = opponents.filter(hasPicked).length;
    const oppRows = opponents
        .map(s64 => commitRow(picksDone && data.users[s64] ? data.users[s64].name : "—", hasPicked(s64)))
        .join("");

    return `${myRows}
    <div class="opponents ${picksDone ? "expanded" : ""}">
      <div class="head"><span>Opponents</span><b>${picksDone ? `${opponents.length}/${opponents.length} picked` : `hidden · ${oppPickedCount}/${opponents.length} picked`}</b></div>
      ${oppRows}
    </div>`;
}

/// Persistent readout for the two coin-flip settings — stays visible after the vote
/// UI (#turn/#type/b1/b2) moves on to the next thing to decide or disappears entirely,
/// so "what did we land on" doesn't require scrolling back through the vote prompts.
function matchSettingsHtml() {
    const ballistics = data.params.ballistics;
    const movement = data.params.movement;
    const pill = (label, value, decidedText) =>
        value === null
            ? `<span class="settingpill undecided">${label}<b>Undecided</b></span>`
            : `<span class="settingpill">${label}<b>${decidedText}</b></span>`;

    return pill("Bullet drop", ballistics, ballistics ? "Enabled" : "Disabled") +
        pill("Movement", movement, movement ? "Old" : "New");
}

/// Purely the countdown display — Match.resolvedMetaIfExpired on the backend is what
/// actually enforces the deadline (on the next /match/current poll, from anyone's
/// screen), so this timer reaching 0 a little before the real cutoff is fine.
function pickTimerHtml() {
    const deadline = data.params.pickDeadline;
    if (!deadline) return "";
    const remaining = Math.max(0, Math.ceil(deadline - Date.now() / 1000));
    return `Auto-picks in <b>${remaining}s</b> if nobody answers`;
}

function update() {
    if (!canInterval) {
        document.getElementById("b1").style.display = "none";
        document.getElementById("b2").style.display = "none";
        return;
    }

    document.getElementById("message").style.display = "none";

    // Kits stop changing the instant the match is claimed (that's the snapshot the
    // server pulls the loadout from), so that's also the moment there's nothing left
    // to hide: reveal every roster together rather than opponents trickling in.
    const picksDone = data.claimed !== null;
    const myTeam = getTeam(steam64);
    document.getElementById("commitlist").innerHTML = commitListHtml(picksDone, myTeam);
    document.getElementById("matchsettings").innerHTML = matchSettingsHtml();

    const everyone = data.config ? allSteam64s() : [];
    const pickedCount = everyone.filter(hasPicked).length;
    document.getElementById("commitcount").innerHTML =
        everyone.length ? `<b>${pickedCount}</b>/${everyone.length} picked` : "";

    // Picking is immediate client-side feedback; the server only claims the match on
    // its own poll cycle, so there's a real gap between "everyone's picked" and a code
    // showing up — that gap is what the progress line is for.
    const allPicked = everyone.length > 0 && pickedCount === everyone.length;
    const connectProgress = document.getElementById("connectprogress");
    if (allPicked && data.claimed === null) {
        connectProgress.style.display = "flex";
        document.getElementById("connectcaption").innerHTML =
            `<b>Connecting…</b> ${data.params.map || ""}`;
    } else {
        connectProgress.style.display = "none";
    }

    api.get("/match/current").then(r => {
        if (r.status === 200) {
            r.json().then(x => { data = x; });
        } else {
            canInterval = false;
            document.getElementById("message").style.display = "block";
            document.getElementById("turn").style.display = "none";
            document.getElementById("type").style.display = "none";
            document.getElementById("message").innerHTML = `<h1>Failed to get match</h1><br><span>Likely due to timeout</span><br><br><button class="mbutton" onclick="canInterval=true">Try again</button><button class="mbutton" style="margin-left:5px;" onclick="window.location.href='/play'">Back to Match Making</button>`;
        }
    });

    if (canDisplayTurn) {
        document.getElementById("turn").style.display = "flex";
        document.getElementById("type").style.display = "flex";
        let c;
        try {
            c = data.users[data.currentPick];
        } catch {
            c = steam64 === data.currentPick ? { name: "You", avatar: "" } : { name: "Someone else", avatar: "" };
        }

        document.getElementById("message").style.display = "flex";
        document.getElementById("turn").innerHTML = `<img src="${c.avatar}"><span>${c.name}</span>`;

        if (steam64 === data.currentPick) {
            document.getElementById("message").innerHTML = "<span>Waiting for <b>YOU</b>...</span>";
            document.getElementById("b1").style.display = "flex";
            document.getElementById("b2").style.display = "flex";
        } else {
            document.getElementById("message").innerHTML = "Waiting for...";
            document.getElementById("b1").style.display = "none";
            document.getElementById("b2").style.display = "none";
        }
    } else {
        document.getElementById("turn").style.display = "none";
        document.getElementById("type").style.display = "none";
    }

    if (data.params.ballistics === null) {
        document.getElementById("type").innerText = "Picking: Bullet drop";
        document.getElementById("b1").innerText = "Disabled";
        document.getElementById("b2").innerText = "Enabled";
        document.getElementById("kits").style.display = "block";
        document.getElementById("commitcount").style.display = "flex";
        document.getElementById("commitlist").style.display = "flex";
        document.getElementById("picktimer").style.display = "flex";
        document.getElementById("picktimer").innerHTML = pickTimerHtml();
    } else if (data.params.movement === null) {
        document.getElementById("type").innerText = "Picking: Movement";
        document.getElementById("b1").innerText = "New";
        document.getElementById("b2").innerText = "Old";
        document.getElementById("kits").style.display = "block";
        document.getElementById("commitcount").style.display = "flex";
        document.getElementById("commitlist").style.display = "flex";
        document.getElementById("picktimer").style.display = "flex";
        document.getElementById("picktimer").innerHTML = pickTimerHtml();
    } else if (Date.now() / 1000 < data.startsAfter) {
        document.getElementById("turn").style.display = "none";
        document.getElementById("type").style.display = "none";
        document.getElementById("b1").style.display = "none";
        document.getElementById("b2").style.display = "none";
        document.getElementById("kits").style.display = "block";
        document.getElementById("commitcount").style.display = "flex";
        document.getElementById("commitlist").style.display = "flex";
        document.getElementById("picktimer").style.display = "none";
        document.getElementById("message").innerHTML = `<h1>Almost there</h1>There's ${Math.round(data.startsAfter - Date.now() / 1000)} seconds left to choose your kit.`;
        document.getElementById("message").style.display = "block";
    } else {
        canDisplayTurn = false;
        document.getElementById("turn").style.display = "none";
        document.getElementById("type").style.display = "none";
        document.getElementById("b1").style.display = "none";
        document.getElementById("b2").style.display = "none";
        document.getElementById("kits").style.display = "none";
        document.getElementById("commitcount").style.display = "none";
        document.getElementById("commitlist").style.display = "none";
        document.getElementById("connectprogress").style.display = "none";
        document.getElementById("picktimer").style.display = "none";
        document.getElementById("message").innerHTML = "<h1>Waiting for server...</h1>Your match will start shortly.";
        document.getElementById("message").style.display = "flex";

        // "0" means the server that claimed the match is too old to report its
        // server code, so there is nothing for a player to connect with.
        if (data.claimed !== null && data.claimed !== "0") {
            if ("users" in data) canInterval = false;
            document.getElementById("message").innerHTML =
                `<h1>Server found</h1><span>Join the server within 2 minutes.</span>` +
                `<a href="${connectUrl(data.claimed)}"><button class="mbutton">Connect to server</button></a>` +
                `<button class="mbutton" onclick="navigator.clipboard.writeText('${data.claimed}')">Copy server code</button>`;
            document.getElementById("message").style.display = "flex";
            document.getElementById("type").innerHTML = "";
            launchOnce(data.claimed);
        }
    }
}

function coreDecide(params) {
    api.post("/match/decide", params).then(() => update());
}

let canDecide = true;
function decide(x) {
    if (!canDecide) return;
    canDecide = false;
    coreDecide({ pick: x });
    setTimeout(() => { canDecide = true; }, 1000);
}

function altDecide(category, value, publish = true) {
    if (publish) coreDecide({ [category]: value });
    for (const el of document.getElementsByClassName(category + "button")) {
        el.style.opacity = el.innerText.trim().toLowerCase() === value.toLowerCase() ? "1" : "0.7";
    }
}

let currentkit = data.currentkit;
let currentPowerup = data.currentpowerup;

function updateKitClasses() {
    for (const el of document.querySelectorAll(".kitbutton")) {
        el.classList.toggle("selectedChoice", el.dataset.kit === currentkit);
    }
}

function updatePowerupClasses() {
    for (const el of document.querySelectorAll(".powerupbutton")) {
        el.classList.toggle("selectedChoice", el.dataset.powerup === currentPowerup);
    }
}

function setKit(value) {
    coreDecide({ kit: value });
    currentkit = value;
    updateKitClasses();
}

function setPowerup(value) {
    coreDecide({ powerup: value });
    currentPowerup = value;
    updatePowerupClasses();
}

document.addEventListener("DOMContentLoaded", () => {
    updateKitClasses();
    updatePowerupClasses();
});

window.setInterval(update, 500);
