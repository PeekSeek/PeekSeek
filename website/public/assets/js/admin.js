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

function esc(value) {
    if (value === null || value === undefined) return "";
    return String(value).replace(/[&<>"']/g, c => ({
        "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;"
    }[c]));
}

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
        <button class="modOpen${always}" title="Moderate this player"
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

/* ---------------------------------------------------------------------------
   Moderation drawer.

   Opened from a player row, from the search box, or from ?ban=<steam64> — which is
   where the "Ban this player" button on an in-game report points, so a report sitting
   in Discord is one click from the form that acts on it. */

let modState = null;

/* Ban state for every player currently drawn, so a row can show a chip without
   costing a request per player per poll. Filled by one call after each refresh. */
let banState = {};

function banChip(steam64) {
    const state = banState[steam64];
    if (!state) return "";
    if (state.banned) return `<span class="banChip active">Banned</span>`;
    if (state.prior) return `<span class="banChip prior">${esc(state.prior)} prior</span>`;
    return "";
}

function openDrawer(steam64) {
    document.getElementById("modOverlay").classList.add("open");
    const drawer = document.getElementById("modDrawer");
    drawer.classList.add("open");
    drawer.innerHTML = `<span style="color:#8885;">Loading…</span>`;

    api.get(`/admin/player/${steam64}`)
        .then(r => r.ok ? r.json() : Promise.reject(r.status))
        .then(data => {
            modState = { player: data, preset: null };
            drawDrawer();
        })
        .catch(e => {
            drawer.innerHTML =
                `<span style="color:#f04444;">Could not load that player (${esc(e)}).</span>`;
        });
}

function closeDrawer() {
    document.getElementById("modOverlay").classList.remove("open");
    document.getElementById("modDrawer").classList.remove("open");
    modState = null;
}

function banRow(ban, active) {
    const when = ban.issued ? new Date(ban.issued).toLocaleDateString() : "";
    const lifted = ban.expires === 0;
    return `<div class="modBan${active ? "" : " expired"}">
        <span class="w">${esc(when)}</span>
        <span>
          <span class="r">${esc(ban.reason)}</span> · ${esc(lifted ? "overturned" : ban.expiryLabel)}
          <div class="n">${esc(ban.note || "")}</div>
        </span>
        ${active ? `<button onclick="overturn(${esc(ban.id)})">Overturn</button>` : ""}
      </div>`;
}

/* The consequence block, and only for the preset that has consequences. A Toxicity ban
   touches nobody else's record, so warning about refunds there would be noise, and
   noise is what teaches people to stop reading warnings. */
function consequence() {
    if (modState.preset !== "cheating") return "";

    const p = modState.player.preview;
    if (!p.matches) {
        return `<div class="modConsequence">
            <div class="h">A cheating ban will also</div>
            They have no scored matches, so nobody else's Elo moves.
          </div>`;
    }

    const since = p.since ? new Date(p.since).toLocaleDateString() : "";
    const sign = p.eloReturned >= 0 ? "+" : "";
    return `<div class="modConsequence">
        <div class="h">A cheating ban will also</div>
        Void <b>${esc(p.matches)}</b> matches back to ${esc(since)}<br>
        Refund <b>${esc(p.players)}</b> players, <b>${sign}${esc(p.eloReturned)}</b> Elo returned<br>
        Delete their stats row<br>
        Post the full refund list to the mod log
      </div>`;
}

function confirmLabel() {
    if (!modState.preset) return "Pick a reason";

    const p = modState.player.preview;
    if (modState.preset === "cheating") {
        return p.players
            ? `Ban permanently and refund ${p.players} players`
            : "Ban permanently";
    }
    if (modState.preset === "custom") return "Ban for the days given";
    return `Ban for ${modState.player.presets[modState.preset].summary.toLowerCase()}`;
}

function drawDrawer() {
    const p = modState.player;
    const presets = {
        ...p.presets,
        custom: { label: "Custom", summary: "Set days + reason" },
    };

    const tiles = Object.entries(presets).map(([key, preset]) => `
        <div class="modTile${modState.preset === key ? " sel" : ""}" tabindex="0"
             onclick="pickPreset('${esc(key)}')"
             onkeydown="if(event.key==='Enter'||event.key===' '){event.preventDefault();pickPreset('${esc(key)}')}">
          <div class="n">${esc(preset.label)}</div>
          <div class="d">${esc(preset.summary)}</div>
        </div>`).join("");

    const custom = modState.preset === "custom" ? `
        <div class="modField">
          <label for="modDays">Days</label>
          <input id="modDays" type="number" min="1" value="7">
        </div>
        <div class="modField">
          <label for="modReason">Reason</label>
          <input id="modReason" type="text" placeholder="What it is for">
        </div>` : "";

    const stats = p.stats
        ? `${p.stats.elo} Elo · ${p.stats.wins}W ${p.stats.losses}L · ${p.stats.kills}K ${p.stats.deaths}D`
        : "Never played a ranked match";

    const past = p.history.filter(b => !p.ban || b.id !== p.ban.id);
    const history = p.history.length
        ? (p.ban ? banRow(p.ban, true) : "") + past.map(b => banRow(b, false)).join("")
        : `<span style="color:#8885;font-size:13px;">Never banned.</span>`;

    document.getElementById("modDrawer").innerHTML = `
      <div class="modHead">
        <img src="${esc(p.avatar)}" alt="">
        <div>
          <div class="modName">${esc(p.name)}</div>
          <div class="modId">${esc(p.steam64)}</div>
        </div>
        <button class="modClose" onclick="closeDrawer()" aria-label="Close">&times;</button>
      </div>
      <div class="modId" style="margin-top:8px;">${esc(stats)}</div>

      ${p.ban ? `<div class="modBanner">
          <div class="t">${p.ban.permanent ? "Permanently banned" : "Banned"}</div>
          <div class="s">${esc(p.ban.reason)} · ${esc(p.ban.expiryLabel)}<br>${esc(p.ban.note || "")}</div>
        </div>` : ""}

      <div class="modSection">
        <h4>Ban history</h4>
        ${history}
      </div>

      <div class="modSection">
        <h4>Issue a ban</h4>
        <div class="modTiles">${tiles}</div>
        ${custom}
        <div class="modField">
          <label for="modNote">Moderator note — evidence, links, context</label>
          <div class="modPrefix">by ${esc(adminName)}/${esc(adminSteam64)}</div>
          <textarea id="modNote" placeholder="Clip, match id, what you saw"></textarea>
        </div>
        ${consequence()}
        <button class="modConfirm" onclick="submitBan()" ${modState.preset ? "" : "disabled"}>
          ${esc(confirmLabel())}
        </button>
      </div>`;
}

function pickPreset(key) {
    modState.preset = key;
    drawDrawer();
}

function submitBan() {
    const p = modState.player;
    const note = document.getElementById("modNote").value;
    const days = document.getElementById("modDays");
    const reason = document.getElementById("modReason");

    if (modState.preset === "custom" && (!days.value || !reason.value)) {
        alert("A custom ban needs both a length and a reason.");
        return;
    }

    const wording = modState.preset === "cheating" && p.preview.players
        ? `Ban ${p.name} permanently?\n\nThis voids ${p.preview.matches} matches and refunds `
          + `${p.preview.players} players. It cannot be undone from here.`
        : `Ban ${p.name}?`;
    if (!confirm(wording)) return;

    const button = document.querySelector(".modConfirm");
    button.disabled = true;
    button.textContent = "Working…";

    // A real body rather than query params: a note carries evidence links, and
    // api.post puts params in the query string, where they would be logged and capped.
    api.post("/admin/ban", {}, {
        body: JSON.stringify({
            steam64: p.steam64,
            preset: modState.preset,
            days: days ? days.value : null,
            reason: reason ? reason.value : null,
            note: note
        })
    })
        .then(r => r.ok ? r.json() : Promise.reject(r.status))
        .then(() => { closeDrawer(); refresh(); loadBanState(); })
        .catch(e => {
            alert(`That failed (${e}).`);
            button.disabled = false;
            button.textContent = confirmLabel();
        });
}

function overturn(banId) {
    if (!confirm("Overturn this ban? The record is kept and marked as overturned.")) return;

    const steam64 = modState.player.steam64;
    api.post("/admin/unban", {}, { body: JSON.stringify({ id: banId }) })
        .then(r => r.ok ? r.json() : Promise.reject(r.status))
        .then(() => { openDrawer(steam64); refresh(); loadBanState(); })
        .catch(e => alert(`That failed (${e}).`));
}

/* One call for every player on screen rather than one per player: a dozen cards of
   four is fifty requests a poll otherwise, for a chip almost nobody has. */
function loadBanState() {
    if (!lastPayload) return;

    const ids = [...new Set(lastPayload.matches.flatMap(
        m => m.teams.flatMap(t => t.players.map(p => p.steam64))))];
    if (!ids.length) return;

    api.get(`/admin/bans?steam64s=${ids.join(",")}`)
        .then(r => r.ok ? r.json() : Promise.reject(r.status))
        .then(state => {
            banState = state;
            if (lastPayload) render(lastPayload);
        })
        .catch(() => { /* Chips are an enhancement. The page is fine without them. */ });
}

function searchPlayer(event) {
    if (event.key !== "Enter") return;

    // Accepts a bare steam64 or a profile URL pasted straight out of the browser.
    const found = event.target.value.trim().match(/(\d{17,})/);
    if (!found) {
        alert("Paste a steam64 or a peekseek.net/u/ link.");
        return;
    }
    event.target.value = "";
    openDrawer(found[1]);
}

document.addEventListener("DOMContentLoaded", () => {
    document.getElementById("modOverlay").addEventListener("click", closeDrawer);
    document.addEventListener("keydown", event => {
        if (event.key === "Escape" && modState) closeDrawer();
    });

    // Deep link, so an in-game report's Ban button lands on the right player.
    const wanted = new URLSearchParams(window.location.search).get("ban");
    if (wanted) openDrawer(wanted);
});
