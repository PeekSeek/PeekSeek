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
/// "still picking" on that axis; mirrors Match.all_kits_locked() on the backend. Picking
/// doesn't stop anyone from changing their mind afterwards (see setKit/setPowerup); this
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

function nameOf(s64) {
    return data.users && data.users[s64] ? data.users[s64].name : (s64 === steam64 ? "You" : "Someone");
}

/// Your own team by name always; opponents collapse to a picked-count summary until
/// `picksDone`, then expand to the same per-player rows: the same reveal moment the
/// server snapshot uses for the match itself.
function commitListHtml(picksDone, myTeamName) {
    if (!data.config || !("users" in data)) return "";

    const myTeam = data.teams.find(t => t.name === myTeamName);
    const opponents = data.teams.filter(t => t.name !== myTeamName).flatMap(t => t.users);

    const myRows = (myTeam ? myTeam.users : [])
        .map(s64 => commitRow(nameOf(s64), hasPicked(s64)))
        .join("");

    const oppPickedCount = opponents.filter(hasPicked).length;
    const oppRows = opponents
        .map(s64 => commitRow(picksDone ? nameOf(s64) : "—", hasPicked(s64)))
        .join("");

    return `${myRows}
    <div class="opponents ${picksDone ? "expanded" : ""}">
      <div class="head"><span>Opponents</span><b>${picksDone ? `${opponents.length}/${opponents.length} picked` : `hidden, ${oppPickedCount}/${opponents.length} picked`}</b></div>
      ${oppRows}
    </div>`;
}

/// The two coin-flip settings, once decided, so "what did we land on" never means
/// scrolling back.
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

function secondsUntil(unix) {
    return Math.max(0, Math.ceil(unix - Date.now() / 1000));
}

function clock(seconds) {
    return `${Math.floor(seconds / 60)}:${String(seconds % 60).padStart(2, "0")}`;
}

function show(id, on) {
    document.getElementById(id).style.display = on ? "" : "none";
}

function rail(id, text, state) {
    const el = document.getElementById(id);
    el.querySelector("span").textContent = text;
    el.classList.toggle("now", state === "now");
    el.classList.toggle("done", state === "done");
}

/// The whole page from `data`, every poll. The rail says where the match is in four
/// steps; the turn row says the one thing that is yours right now; everything else
/// keeps its place and only changes its numbers.
function update() {
    if (!canInterval) return;

    if (data.status === "finished") {
        showMatchOver();
        return;
    }

    const params = data.params;
    const banning = params.ballistics === null || params.movement === null;
    const myTurn = steam64 === data.currentPick;
    const everyone = data.config ? allSteam64s() : [];
    const pickedCount = everyone.filter(hasPicked).length;
    const allPicked = everyone.length > 0 && pickedCount === everyone.length;
    const iPicked = hasPicked(steam64);
    const live = data.status === "live";
    const serverCode = data.serverCode;
    const joinTimeout = data.joinTimeoutSeconds || 120;

    // ---- the rail
    const banText = banning
        ? (myTurn ? "your turn" : `${nameOf(data.currentPick)} choosing`)
        : `drop ${params.ballistics ? "on" : "off"}, ${params.movement ? "old" : "new"} movement`;
    rail("railBans", banText, banning ? "now" : "done");
    rail("railPicks", `${pickedCount} of ${everyone.length} picked`, allPicked ? "done" : (banning ? "" : "now"));
    rail("railServer", serverCode ? "up, join now" : (allPicked ? "loading" : "waiting on picks"),
        serverCode ? "done" : (allPicked ? "now" : ""));
    let readyText = "after the server";
    if (live) readyText = `join within ${clock(secondsUntil((params.hostedAt || 0) + joinTimeout))}`;
    else if (serverCode) readyText = "picks finish in game";
    rail("railReady", readyText, live ? "now" : "");

    // ---- the turn row: the one thing that is yours
    const avatar = document.getElementById("turnAvatar");
    const title = document.getElementById("turnTitle");
    const line = document.getElementById("turnLine");
    show("turnButtons", banning && myTurn);
    show("turnAvatar", banning && !myTurn);
    if (banning) {
        const field = params.ballistics === null ? "ballistics" : "movement";
        const label = field === "ballistics" ? "Bullet drop" : "Movement";
        document.getElementById("b1").innerText = field === "ballistics" ? "Disabled" : "New";
        document.getElementById("b2").innerText = field === "ballistics" ? "Enabled" : "Old";
        if (myTurn) {
            title.textContent = `Your turn: ${label.toLowerCase()}`;
            line.textContent = params.pickDeadline ? `Pick one. Auto-picks in ${secondsUntil(params.pickDeadline)}s.` : "Pick one.";
        } else {
            const who = data.users && data.users[data.currentPick];
            avatar.innerHTML = who ? `<img src="${who.avatar}" alt=""><span>${who.name}</span>` : "";
            title.textContent = `${nameOf(data.currentPick)} is choosing`;
            line.textContent = label;
        }
    } else if (!iPicked) {
        title.textContent = "Your pick";
        line.textContent = `The match starts the moment everyone has. Auto-picks in ${secondsUntil(data.startsAfter)}s.`;
    } else if (!allPicked) {
        title.textContent = "Waiting for the others";
        line.textContent = `${pickedCount} of ${everyone.length} picked. You can still change yours.`;
    } else if (!serverCode) {
        title.textContent = "Server loading";
        line.textContent = `A server is taking the match. ${params.map || ""}`.trim();
    } else {
        title.textContent = "Your server is up";
        line.textContent = "Unturned should open on its own. If it did not, connect below.";
    }

    // ---- the rest keeps its place
    show("kits", !live);
    document.getElementById("commitcount").innerHTML =
        everyone.length ? `<b>${pickedCount}</b>/${everyone.length} picked` : "";
    document.getElementById("commitlist").innerHTML = commitListHtml(live, getTeam(steam64));
    document.getElementById("matchsettings").innerHTML = matchSettingsHtml();

    show("joinline", !!serverCode);
    if (serverCode) {
        document.getElementById("joinText").innerHTML = live
            ? `Join within <b>${clock(secondsUntil((params.hostedAt || 0) + joinTimeout))}</b>. If only one team is there when it runs out, that team takes the match.`
            : `Your server is up and the picks carry on in game. Server code <b>${serverCode}</b>.`;
        document.getElementById("joinConnect").href = connectUrl(serverCode);
        document.getElementById("joinCopy").onclick = () => navigator.clipboard.writeText(serverCode);
        launchOnce(serverCode);
    }

    api.get("/match/current").then(r => {
        if (r.status === 200) {
            r.json().then(x => { data = x; });
        } else {
            canInterval = false;
            document.getElementById("message").innerHTML = `<h1>Lost the match</h1><span>The site did not answer. The match may still be on.</span><button class="mbutton" onclick="canInterval=true">Try again</button><button class="mbutton" style="margin-left:5px;" onclick="window.location.href='/play'">Back to matchmaking</button>`;
            document.getElementById("message").style.display = "flex";
        }
    });
}

/// A match can end while this page is open: someone's server went away, nobody
/// turned up, or it was simply played and scored. Whatever the reason, the page has to
/// say so and let the player leave, rather than sitting on a countdown forever.
function showMatchOver() {
    canInterval = false;
    for (const id of ["rail", "turn", "kits", "commitcount", "commitlist", "joinline", "matchsettings"]) {
        document.getElementById(id).style.display = "none";
    }
    document.getElementById("message").innerHTML =
        `<h1>This match is over</h1><span>Nothing left to pick. Head back to matchmaking for the next one.</span>` +
        `<a href="/play"><button class="mbutton">Back to matchmaking</button></a>`;
    document.getElementById("message").style.display = "flex";
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

window.setInterval(update, 1000);
