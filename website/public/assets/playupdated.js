let selected = parseInt(getCookie("mmoptions")) || 0;
const options = document.getElementsByClassName("qgridoption");

function countOnes(n) {
    let count = 0;
    while (n !== 0) { count += n & 1; n >>>= 1; }
    return count;
}

function bindQgridClicks() {
    for (let i = 0; i < options.length; i++) {
        options.item(i).addEventListener("click", () => {
            if (isQueing) return;

            const eventId = document.getElementById("game").value;
            if (selected & 2 ** i) {
                selected &= ~(2 ** i);
            } else {
                if (eventId !== "matchmaking" && maxTeammates[eventId] - 1 <= countOnes(selected)) {
                    return setError(`You can only have ${maxTeammates[eventId]} players in your team.`);
                }
                selected |= 2 ** i;
            }
            updateOptions();
        });
    }
}

function updateOptions() {
    if (document.getElementById("game").value === "matchmaking") setCookie("mmoptions", selected);
    for (let i = 0; i < options.length; i++) {
        options.item(i).classList.toggle("isSelected", (selected & 2 ** i) === 2 ** i);
    }
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

function setError(text = null, timeout = 3000, stopTheQueue=true) {
    const element = document.getElementById("lastError");
    if (text === null) { element.style.display = "None"; return; }

    if (stopTheQueue) {stopQueue();}
    
    errorDecay = Date.now() + timeout;
    element.style.display = "Flex";
    element.innerText = text;
    setTimeout(() => { if (Date.now() >= errorDecay) setError(); }, timeout);
}

let isQueing = false;
let qFinished = true;
let t = 0;
const region = document.getElementById("region");
const game = document.getElementById("game");

function stopQueue() {
    isQueing = false;
    const el = document.getElementById("q");
    el.classList.remove("isQueing");
    el.innerHTML = "FIND MATCH";
    region.disabled = false;
    game.disabled = false;

    api.get("/queue/quit").catch(() => {
        setError("All attempts to stop queue have failed. You may receive a dodge penalty.",
            3000, false);
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
    const params = eventId !== "matchmaking"
        ? { eventId, team: selected }
        : { gamemode: selected, region: region.value };

    api.get("/queue/heartbeat", params).then(r => {
        qFinished = true;
        if (r.status === 200) {
            r.json().then(d => {
                if (!d.matchid) return;
                isQueing = false;
                window.location.href = "/match/";
            });
        } else {
            r.text().then(msg => setError(msg));
        }
    }).catch(() => setError("Heartbeat failed, stopped queue."));

    renderPing();
}

function queue() {
    const el = document.getElementById("q");
    if (el.classList.contains("isQueing")) {
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
    if (isQueing) document.getElementById("q").innerHTML = new Date(Date.now() - t).toISOString().slice(14, 19);
}, 100);

hb();

window.addEventListener("beforeunload", e => {
    if (!isQueing) return;
    stopQueue();
    e.preventDefault();
});

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

game.addEventListener("change", () => { selected = 0; updateGamemodes(); });
updateGamemodes();
