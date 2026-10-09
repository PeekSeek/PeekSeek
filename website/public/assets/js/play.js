let selected = parseInt(getCookie("mmoptions")) || 0;
const options = document.getElementsByClassName("qgridoption");

function countOnes(n) {
    let count = 0;
    while (n !== 0) {
        count += n & 1;
        n >>>= 1;
    }
    return count;
}

/* Matchmaking will not split a party across two teams, so a mode with fewer seats
   per team than the party has members can never take them, it would just queue
   and never match. Same rule /queue/heartbeat enforces, applied before the button
   is pressed. Event tiles carry no seat count and are never ruled out here. */
function fitsParty(option) {
    const perTeam = parseInt(option.dataset.perTeam);
    return !perTeam || perTeam >= (window.partySize || 1);
}

/* A gamemode tile carries its place in the site's whole gamemode list, which is what
   the queue bitmask means; hidden modes leave gaps that the tile order does not.
   Event teammate tiles have no bit and are counted by position. */
function bitOf(option, i) {
    return 2 ** (option.dataset.bit === undefined ? i : parseInt(option.dataset.bit));
}

function bindQgridClicks() {
    for (let i = 0; i < options.length; i++) {
        options.item(i).addEventListener("click", () => {
            if (isQueing) return;
            if (!fitsParty(options.item(i))) return;

            const eventId = document.getElementById("game").value;
            const bit = bitOf(options.item(i), i);
            if (selected & bit) {
                selected &= ~bit;
            } else {
                if (eventId !== "matchmaking" && maxTeammates[eventId] - 1 <= countOnes(selected)) {
                    return setError(`You can only have ${maxTeammates[eventId]} players in your team.`);
                }
                selected |= bit;
            }
            updateOptions();
        });
    }
}

function updateOptions() {
    for (let i = 0; i < options.length; i++) {
        const option = options.item(i);
        const tooBig = !fitsParty(option);
        const bit = bitOf(option, i);

        // Dropped rather than just dimmed: a mode left selected under a party that
        // outgrew it would be silently stripped by the server anyway, and the
        // cookie below would hand it back on the next visit.
        if (tooBig) selected &= ~bit;

        option.classList.toggle("partyTooBig", tooBig);
        option.classList.toggle("isSelected", (selected & bit) === bit);
    }
    if (document.getElementById("game").value === "matchmaking") setCookie("mmoptions", selected);
}
updateOptions();

async function ping(url) {
    const start = performance.now();
    try {
        await api.get("/ping", { method: "HEAD", cache: "no-store" });
        return Math.round(performance.now() - start);
    } catch (e) {
        return null;
    }
}

function renderPing() {
    ping().then(ms => {
        document.getElementById("pingWarning").style.display = ms !== null && ms >= 150 ? "Flex" : "None";
    });
}

setInterval(renderPing, 3000);

let errorDecay = 0;

function setError(text = null, timeout = 3000, stopTheQueue = true) {
    const element = document.getElementById("lastError");
    if (text === null) {
        element.style.display = "None";
        return;
    }

    if (stopTheQueue) {
        stopQueue();
    }

    errorDecay = Date.now() + timeout;
    element.style.display = "Flex";
    element.innerText = text;
    setTimeout(() => {
        if (Date.now() >= errorDecay) setError();
    }, timeout);
}

let isQueing = false;
let qFinished = true;
let t = 0;
const region = document.getElementById("region");
const game = document.getElementById("game");

/* The same sounds the game plays for the queue, from /static/sounds. */
function playSound(name) {
    new Audio(`/static/sounds/${name}.mp3`).play().catch(() => {});
}

function stopQueue() {
    isQueing = false;
    const el = document.getElementById("q");
    el.classList.remove("isQueing");
    el.innerHTML = "FIND MATCH";
    region.disabled = false;
    game.disabled = false;

    api.get("/queue/quit").catch(() => {
        setError("All attempts to stop queue have failed. You may receive a dodge penalty.", 3000, false);
    });
}

function heartbeat() {
    const el = document.getElementById("q");
    isQueing = true;
    qFinished = false;
    el.classList.add("isQueing");
    region.disabled = true;
    game.disabled = true;

    const eventId = game.value;
    const params =
        eventId !== "matchmaking"
            ? { eventId, team: selected }
            : { gamemode: selected, region: region.value };

    api.get("/queue/heartbeat", params)
        .then(r => {
            qFinished = true;
            if (r.status === 200) {
                r.json().then(d => {
                    if (!d.matchid) return;
                    isQueing = false;
                    playSound("match_found");
                    // Long enough for the sound to be heard before the page goes.
                    setTimeout(() => (window.location.href = "/match/"), 900);
                });
            } else {
                r.text().then(msg => setError(msg));
            }
        })
        .catch(() => setError("Heartbeat failed, stopped queue."));

    renderPing();
}

function queue() {
    const el = document.getElementById("q");
    if (el.classList.contains("isQueing")) {
        playSound("queue_leave");
        stopQueue();
    } else {
        if (window.partyBlocksQueue) {
            setError("Only your party leader can start the queue.");
            return;
        }
        t = Date.now();
        if (selected === 0 && game.value === "matchmaking") {
            setError("Please select gamemode(s) before queue'ing.");
            return;
        }
        playSound("queue_join");
        heartbeat();
    }
}

function hb() {
    if (isQueing) {
        if (!qFinished) console.log("Last queue event hasn't responded yet. Re-trying.");
        else heartbeat();
    }
    setTimeout(hb, 2500);
}

setInterval(() => {
    if (isQueing)
        document.getElementById("q").innerHTML = new Date(Date.now() - t).toISOString().slice(14, 19);
}, 100);

hb();

window.addEventListener("beforeunload", e => {
    if (!isQueing) return;
    stopQueue();
    e.preventDefault();
});

/* The chosen region's activity, and only the part of it worth saying: two or more in
   the lobby, a match inside the last half hour. Anything less is left blank rather than
   written down, since a number that reads as "nobody is here" turns away exactly the
   player who would have made it two. */
function showActivity() {
    const box = document.getElementById("regionActivity");
    if (!box) return;
    const region = document.getElementById("region").value;
    const players = parseInt(box.dataset[region + "Players"] || "0", 10);
    const last = box.dataset[region + "Last"];
    const parts = [];
    if (players >= 2) parts.push(`<b>${players}</b> in the lobby`);
    if (last !== undefined && last !== "" && parseInt(last, 10) <= 30) {
        const minutes = parseInt(last, 10);
        parts.push(minutes < 1 ? "a match just finished" : `last match <b>${minutes} min</b> ago`);
    }
    box.innerHTML = parts.join('<span class="sep"></span>');
    box.style.display = parts.length ? "flex" : "none";
}
document.addEventListener("DOMContentLoaded", showActivity);

let originalQgridHTML = null;

function updateGamemodes() {
    const eventId = game.value;
    const qgrid = document.getElementsByClassName("qgrid")[0];
    const controls = document.getElementsByClassName("controls")[0];

    if (originalQgridHTML === null) originalQgridHTML = qgrid.innerHTML;

    if (eventId !== "matchmaking") {
        const teammates = eventPlayers[parseInt(eventId)];

        if (!teammates || teammates.length === 0) {
            qgrid.style.display = "none";
            controls.style.borderTopLeftRadius = controls.style.borderTopRightRadius = "10px";
            return;
        }

        qgrid.style.display = "";
        controls.style.borderTopLeftRadius = controls.style.borderTopRightRadius = "0px";
        qgrid.innerHTML = "";

        teammates.forEach(teammate => {
            const optionDiv = document.createElement("div");
            optionDiv.classList.add("qgridoption");
            optionDiv.style.cursor = "pointer";
            optionDiv.innerHTML = `<img src="${teammate.avatar}" alt="avatar" style="width:50px;height:50px;border-radius:50%;margin:5px;"><span>${teammate.name}</span>`;
            optionDiv.addEventListener("click", () => optionDiv.classList.toggle("selected"));
            qgrid.appendChild(optionDiv);
        });
    } else {
        qgrid.innerHTML = originalQgridHTML;
        qgrid.style.display = "";
        controls.style.borderTopLeftRadius = controls.style.borderTopRightRadius = "0px";
    }

    bindQgridClicks();
    updateOptions();
}

game.addEventListener("change", () => {
    selected = 0;
    updateGamemodes();
});
updateGamemodes();
