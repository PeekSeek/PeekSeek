let canInterval = true;
let data = null;
let elementsRendered = [];

function update() {
    const params = createdby !== null ? { createdBy: createdby } : { id: matchid };
    api.get("/match/overlay", params).then(r => {
        if (r.status === 200) r.json().then(x => { data = x; });
        else canInterval = false;
    });
    render();
}

function render() {
    if (data === null) return;

    elementsRendered.forEach(el => el.remove());
    elementsRendered = [];

    if (data.claimed === null || data.liveData === null) {
        document.getElementById("notStarted").style.display = "flex";
        document.getElementById("notStartedText").innerHTML = data.teams
            .map(t => `<b>${t.name}</b>`)
            .join(" <i>vs</i> ");
        return;
    }

    document.getElementById("notStarted").style.display = "none";

    data.liveData.Teams.forEach((team, teamId) => {
        const element = document.createElement("div");
        element.innerHTML = `<span class="wins">${team.wins} / 3 wins</span><h1>${team.teamName}</h1>`;

        for (const [s64, userData] of Object.entries(team.users)) {
            element.innerHTML += `<div class="user"><div>${data.users[s64].name}</div> ${userData.kills} / ${userData.deaths}</div>`;
        }

        element.classList.add("team");
        document.getElementById(["left", "right"][teamId % 2]).appendChild(element);
        elementsRendered.push(element);
    });
}

update();
window.setInterval(update, 500);
