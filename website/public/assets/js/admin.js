/* The admin match list: every live match, and everything that ended in the last day.
   Live matches show their round score; finished ones show each player's final line. */

const REFRESH_MS = 15000;
const CLOCK_MS = 1000;

/* Read by moderation.js to redraw the ban chips. */
let lastPayload = null;

function duration(fromUnix, toUnix) {
    const seconds = Math.max(0, Math.floor((toUnix ?? Date.now() / 1000) - fromUnix));
    const minutes = Math.floor(seconds / 60);
    if (minutes < 60) return `${minutes}m ${String(seconds % 60).padStart(2, "0")}s`;
    return `${Math.floor(minutes / 60)}h ${String(minutes % 60).padStart(2, "0")}m`;
}

/* A scored match is the only ordinary ending. The rest all end with every stat at zero,
   which is why they are named. */
const ENDINGS = {
    scored: ["Scored", "#8885"],
    abandoned: ["Server never reported", "#f04444"],
    notPlayed: ["Nobody turned up", "#ffa34e"],
    noServer: ["No server ever took it", "#ffa34e"],
    unrecorded: ["Ended with no result", "#f04444"],
};

function statusChip(match) {
    if (!match.over) return `<span style="color:#47bbe9;">${esc(match.statusLabel)}</span>`;

    const [label, color] = ENDINGS[match.ending] || ["Over", "#8885"];
    return `<span style="color:${color};">${esc(label)}</span>`;
}

function settingsLine(match) {
    const parts = [match.gamemode, match.map, match.region];
    if (match.ballistics !== null) parts.push(`bullet drop ${match.ballistics ? "on" : "off"}`);
    if (match.movement !== null) parts.push(`${match.movement ? "old" : "new"} movement`);
    return parts.filter(Boolean).map(esc).join(" &middot; ");
}

function serverLine(match) {
    if (!match.serverCode || match.serverCode === "0") {
        return `<span style="color:#8885;">No server holds this match</span>`;
    }
    return `<span style="color:#aaa;">Server ${esc(match.serverCode)}</span>`;
}

function finalLine(player) {
    if (player.kills === null || player.kills === undefined) return "";

    const hs = player.kills ? Math.round((player.headshots / player.kills) * 100) : 0;
    let line = `${esc(player.kills)}K ${esc(player.deaths)}D <span style="color:#aaa;">${hs}% HS</span>`;
    if (player.eloChange !== null && player.eloChange !== undefined) {
        const up = player.eloChange >= 0;
        line +=
            ` <span style="color:${up ? "#6c6" : "#f04444"};">${up ? "+" : ""}${esc(player.eloChange)}</span>` +
            `<span style="color:#8885;"> (${esc(player.elo)})</span>`;
    }
    return line;
}

/* The ban chip sits inside the name cell: a column that only some rows have would push
   the rest of the row onto a second line. */
function playerRow(player) {
    return `<div class="adminPlayer">
        <span class="adminPlayerWho">
          <a href="/u/${esc(player.steam64)}" class="userProfile">
            <img src="${esc(player.avatar)}" alt="">
            <span>${esc(player.name)}</span>
          </a>
          ${banChip(player.steam64)}
        </span>
        <span class="adminPlayerStats">${finalLine(player)}</span>
        <button class="modOpen" title="Ban this player"
                onclick="openDrawer('${esc(player.steam64)}')">&#9873;</button>
      </div>`;
}

function teamBlock(team) {
    const score = team.wins === null || team.wins === undefined ? "" : `<b>${esc(team.wins)}</b>`;
    const eventScore =
        team.eventScore === null || team.eventScore === undefined
            ? ""
            : `<span style="color:#8885;">event score ${esc(team.eventScore)}</span>`;

    return `<div class="adminTeam">
        <div class="adminTeamHead">
          <span>${esc(team.name)}</span>
          <span class="adminTeamScore">${eventScore} ${score}</span>
        </div>
        ${team.players.map(playerRow).join("")}
      </div>`;
}

function card(match) {
    const eventLine = match.event
        ? `<span style="color:#47bbe9;">${esc(match.event.name || `Event ${match.event.id}`)}</span>`
        : "";

    const spectate = match.connectCode
        ? `<button class="settingsButton" style="background-color:#aab;"
             onclick="window.location.href='steam://run/304930//+connect ${esc(match.connectCode)}'">
             <img src="/static/img/spectate.png" height="20" style="filter: invert(1);">Spectate</button>`
        : "";

    const stop = match.over
        ? ""
        : `<button class="settingsButton" style="background-color:#faa;"
          onclick="forceStopMatch(${esc(match.id)})">
          <img src="/static/img/stop.png" height="20" style="filter: invert(1);">Force stop</button>`;

    return `<div class="matchGridElement${match.over ? " over" : ""}">
        <div class="matchGridInner">
          <div class="adminCardHead">
            <span>Match.<b style="color:#20a0f0;">${esc(match.id)}</b></span>
            <span class="adminClock" data-started="${esc(match.startedAt)}"
                  data-ended="${match.endedAt === null ? "" : esc(match.endedAt)}">${duration(match.startedAt, match.endedAt)}</span>
          </div>
          <span class="adminMeta">${statusChip(match)} &middot; First to ${esc(match.roundsToWin)}</span>
          <span class="adminMeta">${settingsLine(match)}</span>
          <span class="adminMeta">${serverLine(match)}</span>
          ${eventLine ? `<span class="adminMeta">${eventLine}</span>` : ""}
          <hr>
          ${match.teams.map(teamBlock).join("")}
        </div>
        <div class="matchGridInner adminActions">${stop}${spectate}</div>
      </div>`;
}

function render(payload) {
    lastPayload = payload;
    const grid = document.getElementById("matches");
    const stamp = document.getElementById("refreshed");

    if (!payload.matches.length) {
        grid.innerHTML = `<span style="color:#8885;">No matches in the last 24 hours.</span>`;
    } else {
        grid.innerHTML = payload.matches.map(card).join("");
    }

    const live = payload.matches.filter(m => !m.over).length;
    stamp.textContent =
        `${live} live, ${payload.matches.length} in the last 24h` +
        ` · updated ${new Date().toLocaleTimeString()}`;
}

function refresh() {
    return api
        .get("/admin/matches")
        .then(r => (r.ok ? r.json() : Promise.reject(r.status)))
        .then(payload => {
            render(payload);
            loadBanState();
        })
        .catch(e => {
            const stamp = document.getElementById("refreshed");
            if (stamp) stamp.textContent = `Could not reach the site (${e}). Retrying.`;
        });
}

function tickClocks() {
    document.querySelectorAll(".adminClock").forEach(node => {
        if (node.dataset.ended) return;
        node.textContent = duration(Number(node.dataset.started), null);
    });
}

function forceStopMatch(id) {
    if (!confirm(`Force stop Match.${id}? Nobody's Elo moves and its players are freed.`)) return;

    api.post("/admin/match/stop", { id })
        .then(r => (r.ok ? refresh() : alert("Failed.")))
        .catch(e => console.error("Error:", e));
}

document.addEventListener("DOMContentLoaded", () => {
    refresh();
    setInterval(refresh, REFRESH_MS);
    setInterval(tickClocks, CLOCK_MS);
});
