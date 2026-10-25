let cursor = 0;
let lastPress = 0;

function deleteTeam(id) {
    if (document.getElementsByClassName("customTeam").length <= 1) {
        alert("Can't have less than 1 team.");
        return;
    }
    document.getElementById("cTeam" + id).remove();
}

function createTeam() {
    if (document.getElementsByClassName("customTeam").length >= 4) {
        alert("Can't have more than 4 teams.");
        return;
    }

    const t = document.createElement("div");
    t.id = "cTeam" + cursor;
    t.innerHTML = `<button class="delbuttoncustom" onclick="deleteTeam(${cursor})">x</button>`;
    t.innerHTML += `<input type="text" name="name" placeholder="Team name..." style="width:15%;">`;
    t.innerHTML += `<input type="text" name="ids" placeholder="Steam64s split by space" style="width:80%;">`;
    t.classList.add("customTeam");
    cursor++;
    document.getElementById("customTeamsList").appendChild(t);
}

document.getElementById("customStart").addEventListener("click", () => {
    if (Date.now() < lastPress + 10000) {
        alert(`Wait ${(lastPress + 10000 - Date.now()) / 1000} seconds until creating another match.`);
        return;
    }

    lastPress = Date.now();

    const teams = {};
    for (const i of document.getElementsByClassName("customTeam")) {
        teams[i.children[1].value] = i.children[2].value.trim().split(/\s+/).filter(Boolean);
    }

    const toValue = v => [false, true, null][Number(document.getElementById("customOption" + v).value)];

    const matchData = {
        map: document.getElementById("customOption1").value,
        ballistics: toValue("3"),
        movement: toValue("2"),
        teams,
        region: document.getElementById("region").value,
        gamemode: document.getElementById("gamemode").value,
    };

    // datetime-local is read in the creator's own time zone, which is what they typed.
    const startsAt = document.getElementById("startsAt").value;
    if (startsAt) matchData.startsAt = Math.floor(new Date(startsAt).getTime() / 1000);

    api.post("/match/custom", {}, { body: JSON.stringify(matchData) }).then(async r => {
        if (r.status === 200) alert(startsAt ? "Match scheduled." : "Successfully created match.");
        else alert("Failed to create match. " + ((await r.json().catch(() => ({}))).detail || ""));
    });
});

createTeam();
createTeam();
