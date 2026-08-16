const teamName = document.getElementById("teamName");
const createTeam = document.getElementById("createTeam");
let errorDecay = 0;

function setError(text = null, timeout = 3000) {
    const element = document.getElementById("lastError");
    if (text === null) { element.style.display = "None"; return; }

    console.log(`Error @ ${Date.now()}: ${text}`);
    errorDecay = Date.now() + timeout;
    element.style.display = "Flex";
    element.innerText = text;
    setTimeout(() => { if (Date.now() >= errorDecay) setError(); }, timeout);
}

function apiCall(params, failedMessage) {
    api.post("/esports/signup", params)
        .then(async r => {
            if (r.status === 200) location.reload();
            else setError(await r.text());
        })
        .catch(() => setError(failedMessage));
}

if (createTeam) {
    createTeam.addEventListener("click", () => {
        if (teamName.value.length <= 2) { alert("Team name must be longer than 2 characters."); return; }
        apiCall({ eventId, team: teamName.value }, "Failed to create team");
    });
}

const joinCode = document.getElementById("joinCode");
const joinTeam = document.getElementById("joinTeam");
if (joinTeam) {
    joinTeam.addEventListener("click", () => {
        apiCall({ eventId, joincode: joinCode.value }, "Failed to join team");
    });
}

const leaveTeam = document.getElementById("leaveTeam");
if (leaveTeam) {
    leaveTeam.addEventListener("click", () => {
        apiCall({ eventId, leave: true }, "Failed to leave team");
    });
}

const resetJoin = document.getElementById("resetJoin");
if (resetJoin) {
    resetJoin.addEventListener("click", () => {
        apiCall({ eventId, reset: true }, "Failed to reset join code");
    });
}
