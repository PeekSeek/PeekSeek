/* The admin match list: every live match, and everything that ended in the last day.

   Polled rather than server-rendered because the interesting matches are the live ones,
   and a page you have to reload to see a score is a page nobody watches. The whole list
   is rebuilt each poll: there is nothing animated here to restart, and one description
   of a card beats a mount/patch pair that can disagree. */

const REFRESH_MS = 15000;

/* Separate from the poll and far quicker, because a running match's clock is the one
   thing on the card that changes every second and none of it needs the network. */
const CLOCK_MS = 1000;

let lastPayload = null;

function duration(fromUnix, toUnix) {
    const seconds = Math.max(0, Math.floor((toUnix ?? Date.now() / 1000) - fromUnix));
    const minutes = Math.floor(seconds / 60);
    if (minutes < 60) return `${minutes}m ${String(seconds % 60).padStart(2, "0")}s`;
    return `${Math.floor(minutes / 60)}h ${String(minutes % 60).padStart(2, "0")}m`;
}

function clock(match) {
    return duration(match.startedAt, match.endedAt);
}

/* One word for why a match stopped, and the colour that goes with it. A scored match is
   the only ordinary ending; the rest are all a server or a player that went missing, and
   they look identical on the numbers alone (everything zero), which is exactly why they
   are labelled rather than left to be inferred. */
const ENDINGS = {
    scored: ["Scored", "#8885"],
    abandoned: ["Server never reported", "#f04444"],
    notPlayed: ["Nobody turned up", "#ffa34e"],
    noServer: ["No server ever took it", "#ffa34e"],
    unrecorded: ["Ended with no result", "#f04444"],
};

/* Where a player stands, said the same way on every card. Offline is called out in red
   because on a live match it is the thing an admin is looking for. */
const STATES = {
    alive: ["Alive", "#fff"],
    dead: ["Dead", "#8885"],
    waiting: ["Waiting", "#8885"],
    offline: ["Not connected", "#f04444"],
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

    if (!match.server) {
        // Steam only lists a server that is up, so silence is itself information while a
        // match is live: whatever was holding it has stopped advertising itself.
        const color = match.over ? "#8885" : "#f04444";
        return `<span style="color:${color};">${esc(match.serverCode)}, not listed on Steam</span>`;
    }

    const players = match.server.players === null || match.server.players === undefined
        ? "" : ` (${match.server.players}/${match.server.maxPlayers})`;
    return `<span style="color:#aaa;">${esc(match.server.name)}${esc(players)}</span>`;
}

function roundLine(match) {
    if (match.round && !match.over) return `Round ${esc(match.round)}, first to ${esc(match.roundsToWin)}`;
    return `First to ${esc(match.roundsToWin)}`;
}

function playerRow(player, over) {
    const [stateLabel, stateColor] = STATES[player.state] || ["", "#8885"];
    const hs = player.kills ? Math.round((player.headshots / player.kills) * 100) : 0;

    const numbers = player.kills === null || player.kills === undefined
        ? `<span style="color:#8885;">no stats</span>`
        : `${esc(player.kills)}K ${esc(player.deaths)}D <span style="color:#aaa;">${hs}% HS</span>`;

    let trailing = "";
    if (over && player.eloChange !== null && player.eloChange !== undefined) {
        const up = player.eloChange >= 0;
        trailing = `<span style="color:${up ? "#6c6" : "#f04444"};">`
            + `${up ? "+" : ""}${esc(player.eloChange)}</span>`
            + `<span style="color:#8885;"> (${esc(player.elo)})</span>`;
    } else if (!over && stateLabel) {
        const health = player.state === "alive" && player.health !== null
            ? ` ${esc(player.health)}hp` : "";
        trailing = `<span style="color:${stateColor};">${esc(stateLabel)}${health}</span>`;
    }

    // The profile link stays what it always was; moderation is a separate control so
    // clicking a name still goes where everyone expects. The button is hidden until the
    // row is hovered unless this player actually has ban state worth surfacing.
    const state = banState[player.steam64];
    const always = state && (state.banned || state.prior) ? " always" : "";

    return `<div class="adminPlayer">
        <a href="/u/${esc(player.steam64)}" class="userProfile">
          <img src="${esc(player.avatar)}" alt="">
          <span>${esc(player.name)}</span>
        </a>
        ${banChip(player.steam64)}
        <span class="adminPlayerStats">${numbers}</span>
        <span class="adminPlayerTrailing">${trailing}</span>
        <button class="modOpen${always}" title="Ban this player"
                onclick="openDrawer('${esc(player.steam64)}')">&#9873;</button>
      </div>`;
}

function teamBlock(team, over) {
    const score = team.wins === null || team.wins === undefined
        ? "" : `<b>${esc(team.wins)}</b>`;
    const eventScore = team.eventScore === null || team.eventScore === undefined
        ? "" : `<span style="color:#8885;">event score ${esc(team.eventScore)}</span>`;

    return `<div class="adminTeam">
        <div class="adminTeamHead">
          <span>${esc(team.name)}</span>
          <span class="adminTeamScore">${eventScore} ${score}</span>
        </div>
        ${team.players.map(p => playerRow(p, over)).join("")}
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

    const stop = match.over ? "" : `<button class="settingsButton" style="background-color:#faa;"
          onclick="forceStopMatch(${esc(match.id)})">
          <img src="/static/img/stop.png" height="20" style="filter: invert(1);">Force stop</button>`;

    return `<div class="matchGridElement${match.over ? " over" : ""}">
        <div class="matchGridInner">
          <div class="adminCardHead">
            <span>Match.<b style="color:#20a0f0;">${esc(match.id)}</b></span>
            <span class="adminClock" data-started="${esc(match.startedAt)}"
                  data-ended="${match.endedAt === null ? "" : esc(match.endedAt)}">${clock(match)}</span>
          </div>
          <span class="adminMeta">${statusChip(match)} &middot; ${roundLine(match)}</span>
          <span class="adminMeta">${settingsLine(match)}</span>
          <span class="adminMeta">${serverLine(match)}</span>
          ${eventLine ? `<span class="adminMeta">${eventLine}</span>` : ""}
          <hr>
          ${match.teams.map(t => teamBlock(t, match.over)).join("")}
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
    stamp.textContent = `${live} live, ${payload.matches.length} in the last 24h`
        + ` · updated ${new Date().toLocaleTimeString()}`;
}

function refresh() {
    return api.get("/admin/matches")
        .then(r => r.ok ? r.json() : Promise.reject(r.status))
        .then(payload => { render(payload); loadBanState(); })
        .catch(e => {
            const stamp = document.getElementById("refreshed");
            if (stamp) stamp.textContent = `Could not reach the site (${e}). Retrying.`;
        });
}

/* Only the clocks, so a card that is otherwise unchanged is not rebuilt 15 times
   between polls just to move a number. */
function tickClocks() {
    document.querySelectorAll(".adminClock").forEach(node => {
        if (node.dataset.ended) return;
        node.textContent = duration(Number(node.dataset.started), null);
    });
}

function forceStopMatch(id) {
    if (!confirm(`Force stop Match.${id}? Nobody's Elo moves and its players are freed.`)) return;

    api.post("/admin/match/stop", { id })
        .then(r => r.ok ? refresh() : alert("Failed."))
        .catch(e => console.error("Error:", e));
}

document.addEventListener("DOMContentLoaded", () => {
    refresh();
    setInterval(refresh, REFRESH_MS);
    setInterval(tickClocks, CLOCK_MS);
});
