/* The moderation drawer, shared by the admin match grid and a player's profile.

   One component rather than two: the form that issues a ban has a dry run, a note
   prefix and a history list behind it, and a second copy of that on the profile page
   would be a second thing to keep correct.
*/
/* ---------------------------------------------------------------------------
   Moderation panel.

   Opened from a player row, from the search box, or from ?ban=<steam64> — which is
   where the "Ban this player" button on an in-game report points, so a report sitting
   in Discord is one click from the form that acts on it. */

/* Shared with admin.js, which relies on this file being loaded first. It was left
   behind there when the drawer was split out, which broke the drawer on every page
   that does not also load the match grid. */
function esc(value) {
    if (value === null || value === undefined) return "";
    return String(value).replace(/[&<>"']/g, c => ({
        "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;"
    }[c]));
}

let modState = null;

/* What to do once a ban or an overturn lands. The admin page redraws its match grid
   and its chips; the profile page has neither and simply reloads, which is cheaper to
   reason about than teaching the drawer what page it is on. */
function afterModeration() {
    if (typeof refresh === "function") {
        refresh();
        loadBanState();
        return;
    }
    window.location.reload();
}

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
    const drawer = document.getElementById("modPanel");
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
    document.getElementById("modPanel").classList.remove("open");
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

    document.getElementById("modPanel").innerHTML = `
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
        .then(() => { closeDrawer(); afterModeration(); })
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
        .then(() => { openDrawer(steam64); afterModeration(); })
        .catch(e => alert(`That failed (${e}).`));
}

/* One call for every player on screen rather than one per player: a dozen cards of
   four is fifty requests a poll otherwise, for a chip almost nobody has. */
function loadBanState() {
    // Only the admin page has a match grid to put chips on.
    if (typeof lastPayload === "undefined" || !lastPayload) return;

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

document.addEventListener("DOMContentLoaded", () => {
    const overlay = document.getElementById("modOverlay");
    if (!overlay) return;
    overlay.addEventListener("click", closeDrawer);
    document.addEventListener("keydown", event => {
        if (event.key === "Escape" && modState) closeDrawer();
    });

    // Deep link, so an in-game report's Ban button lands on the right player.
    const wanted = new URLSearchParams(window.location.search).get("ban");
    if (wanted) openDrawer(wanted);
});
