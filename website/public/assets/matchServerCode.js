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

function update() {
    if (!canInterval) {
        document.getElementById("b1").style.display = "none";
        document.getElementById("b2").style.display = "none";
        return;
    }

    document.getElementById("message").style.display = "none";

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
    } else if (data.params.movement === null) {
        document.getElementById("type").innerText = "Picking: Movement";
        document.getElementById("b1").innerText = "New";
        document.getElementById("b2").innerText = "Old";
        document.getElementById("kits").style.display = "block";
    } else if (Date.now() / 1000 < data.startsAfter) {
        document.getElementById("turn").style.display = "none";
        document.getElementById("type").style.display = "none";
        document.getElementById("b1").style.display = "none";
        document.getElementById("b2").style.display = "none";
        document.getElementById("kits").style.display = "block";
        document.getElementById("message").innerHTML = `<h1>Almost there</h1>There's ${Math.round(data.startsAfter - Date.now() / 1000)} seconds left to choose your kit.`;
        document.getElementById("message").style.display = "block";
    } else {
        canDisplayTurn = false;
        document.getElementById("turn").style.display = "none";
        document.getElementById("type").style.display = "none";
        document.getElementById("b1").style.display = "none";
        document.getElementById("b2").style.display = "none";
        document.getElementById("kits").style.display = "none";
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

    const ballisticsText = data.params.ballistics === true ? "Enabled" : data.params.ballistics === false ? "Disabled" : "Undecided";
    const movementText = data.params.movement === true ? "Old" : data.params.movement === false ? "New" : "Undecided";
    let currentMatch = `<h1>Match data</h1><div><span>Bulletdrop: ${ballisticsText}</span><br><span>Movement: ${movementText}</span></div><div class="teams">`;

    for (const t of data.teams) {
        currentMatch += `<div><span class="name">${t.name}</span>`;
        if ("users" in data) {
            for (const s64 of t.users) {
                const u = data.users[s64];
                currentMatch += `<a class="userProfile"><img src="${u.avatar}"><span>${u.name}</span></a>`;
            }
        }
        currentMatch += "</div>";
    }
    currentMatch += "</div>";
    document.getElementById("match").innerHTML = currentMatch;
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
