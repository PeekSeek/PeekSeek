/* Stream overlay renderer.
 *
 * Polls /match/overlay and draws the live snapshot the game server pushes to
 * /unt/update (see PSUpdateState.cs). The structure is only rebuilt when the
 * roster changes; every other tick patches text and widths in place, because
 * re-creating the DOM twice a second would restart the kill feed and health
 * animations on every poll.
 */

const POLL_MS = 500;
const KILL_LIFETIME_MS = 6000;
const ICON_BASE = "/static/unturned/";
const DEFAULT_ROUNDS_TO_WIN = 3;

/* Inverse fillets for the notch. Left is the notch colour minus a quarter
   disc centred at (0, 10); right is the same mirrored, centred at (10, 10).
   Because the centre sits on the top edge the shape is widest at y = 0, which
   is what makes the notch flare outward into the straight line. */
const FLARE_L = `<svg class="flare l" viewBox="0 0 10 10" aria-hidden="true"><path d="M0 0 L10 0 L10 10 A10 10 0 0 0 0 0 Z"/></svg>`;
const FLARE_R = `<svg class="flare r" viewBox="0 0 10 10" aria-hidden="true"><path d="M10 0 L0 0 L0 10 A10 10 0 0 1 10 0 Z"/></svg>`;

/* Headshot marker for the kill feed. */
const CROSSHAIR = `<svg class="hs" viewBox="0 0 24 24" aria-label="headshot">
  <circle cx="12" cy="12" r="7.5" fill="none" stroke="currentColor" stroke-width="1.8"/>
  <circle cx="12" cy="12" r="1.5" fill="currentColor"/>
  <line x1="12" y1="0.5" x2="12" y2="5" stroke="currentColor" stroke-width="1.8"/>
  <line x1="12" y1="19" x2="12" y2="23.5" stroke="currentColor" stroke-width="1.8"/>
  <line x1="0.5" y1="12" x2="5" y2="12" stroke="currentColor" stroke-width="1.8"/>
  <line x1="19" y1="12" x2="23.5" y2="12" stroke="currentColor" stroke-width="1.8"/>
</svg>`;

let canInterval = true;
let data = null;
let view = null;

function escapeHtml(value) {
    return String(value ?? "").replace(/[&<>"']/g, c => ({
        "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;"
    })[c]);
}

function icon(id) { return `${ICON_BASE}${id}.png`; }
function inPlay(user) { return user.state === "alive"; }
function isLow(health) { return health <= 30; }

/* A bare percentage. The label is obvious from its position next to K and D,
   so counting individual headshots buys nothing on a card this small. */
function hsPercent(user) {
    return user.kills > 0 ? Math.round((user.headshots / user.kills) * 100) : 0;
}

/* The deployed 1.0 plugin serialises PascalCase and 2.0 serialises camelCase,
   so read both until every region runs the new build. */
function pick(source, ...names) {
    for (const name of names) {
        if (source && source[name] !== undefined && source[name] !== null) return source[name];
    }
    return undefined;
}

/* Item ids a powerup hands out, so they can be ordered after the player's own
   kit rather than interleaved with it. */
function powerupItemIds(config, name) {
    const entry = (config?.powerups || []).find(p => p.name === name);
    return new Set((entry?.items || []).map(item => item[0]));
}

/* Everything the player carries except the gun already shown in the first
   slot: their own kit first, then whatever the powerup granted. */
function loadout(user, powerupIds) {
    const rest = user.inventory.filter(item => item.id !== user.equipped);
    return [
        ...rest.filter(item => !powerupIds.has(item.id)),
        ...rest.filter(item => powerupIds.has(item.id))
    ];
}

function roundLabel(state) {
    if (state.teams.some(t => t.wins >= state.roundsToWin)) return { text: "FINAL", hot: true };
    if (state.teams.some(t => t.wins === state.roundsToWin - 1)) return { text: "MATCH POINT", hot: true };
    return { text: `ROUND ${state.round}`, hot: false };
}

/* Flattens the payload into exactly what the overlay draws, filling in the
   pieces the game server does not know about: Steam names it never saw and
   the powerup the player chose on the site. */
function buildState(payload) {
    const live = payload.liveData || {};
    const liveTeams = pick(live, "teams", "Teams") || [];
    if (!liveTeams.length) return null;

    const userData = payload.params?.userData || {};
    const config = payload.config || {};

    return {
        round: pick(live, "round", "Round") ?? 1,
        roundsToWin: pick(live, "roundsToWin", "RoundsToWin") ?? DEFAULT_ROUNDS_TO_WIN,
        event: pick(live, "event", "Event") ?? null,
        feed: pick(live, "feed", "Feed") || [],
        teams: liveTeams.map((team, teamIndex) => {
            const users = pick(team, "users", "Users") || {};
            return {
                teamName: pick(team, "teamName", "TeamName")
                    || payload.teams?.[teamIndex]?.name
                    || `Team ${teamIndex + 1}`,
                wins: pick(team, "wins", "Wins") ?? 0,
                users: Object.entries(users).map(([steam64, stats]) => {
                    const powerup = userData[steam64]?.powerup || null;
                    return {
                        id: steam64,
                        name: pick(stats, "name", "Name")
                            || payload.users?.[steam64]?.name
                            || steam64,
                        powerup,
                        powerupIds: powerupItemIds(config, powerup),
                        state: pick(stats, "state", "State") || "offline",
                        health: pick(stats, "health", "Health") ?? 0,
                        kills: pick(stats, "kills", "Kills") ?? 0,
                        deaths: pick(stats, "deaths", "Deaths") ?? 0,
                        headshots: pick(stats, "headshots", "Headshots") ?? 0,
                        equipped: pick(stats, "equipped", "Equipped") ?? 0,
                        inventory: pick(stats, "inventory", "Inventory") || []
                    };
                })
            };
        })
    };
}

function seatsOf(state) {
    return state.teams.flatMap(team => team.users.map(user => ({ team, user })));
}

/* Remount only when the roster itself changes, so animations survive a poll. */
function signature(state) {
    return state.teams.map(t => `${t.teamName}:${t.users.map(u => u.id).join(",")}`).join("|");
}

function slotHtml(item, extra = "") {
    return `<span class="slot ${extra}">
    <img src="${icon(item.id)}" alt="">
    ${item.amount > 1 ? `<span class="n">${escapeHtml(item.amount)}</span>` : ""}
  </span>`;
}

function columnHtml(state, side) {
    const groups = state.teams
        .map((team, i) => ({ team, i }))
        .filter(({ i }) => (state.teams.length === 2 ? i === side : i % 2 === side));

    let seat = 0;
    const seatOf = new Map();
    state.teams.forEach(team => team.users.forEach(user => seatOf.set(user, seat++)));

    return `<div class="col ${side === 0 ? "l" : "r"}">
    ${groups.map(({ team }) => `
      <div class="group">
        ${team.users.length > 1 ? `<span class="tname">${escapeHtml(team.teamName)}</span>` : ""}
        ${team.users.map(user => `
          <div class="card flat" data-seat="${seatOf.get(user)}">
            <span class="flash"></span>
            <div class="l1">
              <span class="nm"></span>
              <span class="stats"></span>
            </div>
            <div class="l2">
              <span class="kit"></span>
              <span class="pw"></span>
            </div>
            <span class="hp"><i></i></span>
          </div>`).join("")}
      </div>`).join("")}
  </div>`;
}

function mount(state) {
    const hud = document.getElementById("hud");
    const duel = state.teams.length === 2;
    const label = roundLabel(state);

    hud.innerHTML = `
    <div class="notch flat">
      ${FLARE_L}
      ${duel ? `<span class="tn">${escapeHtml(state.teams[0].teamName)}</span><span class="sc" data-sc="0"></span>` : ""}
      <span class="mid ${label.hot ? "hot" : ""}">${label.text}</span>
      ${duel ? `<span class="sc" data-sc="1"></span><span class="tn">${escapeHtml(state.teams[1].teamName)}</span>` : ""}
      ${FLARE_R}
    </div>
    <div class="feed"></div>
    <div class="banner flat"><span class="what"></span><span class="who"></span></div>
    ${columnHtml(state, 0)}
    ${columnHtml(state, 1)}`;

    const cards = new Map();
    seatsOf(state).forEach((_, i) => {
        const el = hud.querySelector(`[data-seat="${i}"]`);
        cards.set(i, {
            root: el,
            nm: el.querySelector(".nm"),
            stats: el.querySelector(".stats"),
            kit: el.querySelector(".kit"),
            pw: el.querySelector(".pw"),
            gauge: el.querySelector(".hp i"),
            health: null
        });
    });

    view = {
        hud,
        cards,
        signature: signature(state),
        scores: [hud.querySelector('[data-sc="0"]'), hud.querySelector('[data-sc="1"]')],
        mid: hud.querySelector(".mid"),
        feed: hud.querySelector(".feed"),
        banner: hud.querySelector(".banner"),
        shownKills: new Set(),
        shownEvent: null,
        bannerTimer: null
    };
}

function setScore(el, value) {
    if (!el || el.textContent === String(value)) return;
    el.textContent = value;
    el.classList.add("bump");
    setTimeout(() => el.classList.remove("bump"), 400);
}

function apply(state) {
    if (view.scores[0]) {
        setScore(view.scores[0], state.teams[0].wins);
        setScore(view.scores[1], state.teams[1].wins);
    }

    const label = roundLabel(state);
    view.mid.textContent = label.text;
    view.mid.classList.toggle("hot", label.hot);

    seatsOf(state).forEach(({ user }, i) => {
        const c = view.cards.get(i);
        if (!c) return;

        c.root.classList.toggle("out", !inPlay(user));
        c.nm.textContent = user.name;
        c.stats.innerHTML =
            `<span><b>${user.kills}</b>K</span>` +
            `<span><b>${user.deaths}</b>D</span>` +
            `<span><b>${hsPercent(user)}</b>%</span>`;
        c.kit.innerHTML = slotHtml({ id: user.equipped, amount: 1 }, "gun") +
            loadout(user, user.powerupIds).map(item => slotHtml(item)).join("");
        c.pw.textContent = user.powerup || "";

        /* Flash the card when they lost health since the last poll. */
        if (c.health !== null && user.health < c.health) {
            c.root.classList.remove("hit");
            void c.root.offsetWidth;
            c.root.classList.add("hit");
        }
        c.health = user.health;

        c.gauge.style.width = `${user.health}%`;
        c.gauge.classList.toggle("low", isLow(user.health));
    });

    syncFeed(state.feed);
    if (state.event) showBanner(state.event);
}

function syncFeed(feed) {
    feed.filter(kill => !view.shownKills.has(kill.id)).forEach(kill => {
        view.shownKills.add(kill.id);

        /* Weapon is 0 when nothing killed them — bleeding out, falling, the
           arena border — so there is no icon to show and no killer either. */
        const row = document.createElement("div");
        row.className = "kill flat";
        row.innerHTML =
            (kill.killer ? `<span class="name">${escapeHtml(kill.killer)}</span>` : "") +
            (kill.weapon ? `<img src="${icon(kill.weapon)}" alt="">` : "") +
            (kill.limb === "skull" ? CROSSHAIR : "") +
            `<span class="name">${escapeHtml(kill.victim)}</span>`;
        view.feed.appendChild(row);

        setTimeout(() => {
            row.classList.add("leaving");
            setTimeout(() => row.remove(), 320);
        }, KILL_LIFETIME_MS);
    });
}

function showBanner(event) {
    if (event.id === view.shownEvent) return;
    view.shownEvent = event.id;

    view.banner.querySelector(".what").textContent =
        event.kind === "matchWon" ? "Wins the match" : `Takes round ${event.round}`;
    view.banner.querySelector(".who").textContent = event.team;
    view.banner.classList.toggle("final", event.kind === "matchWon");

    clearTimeout(view.bannerTimer);
    view.banner.classList.remove("hide");
    view.banner.classList.add("show");

    view.bannerTimer = setTimeout(() => {
        view.banner.classList.remove("show");
        view.banner.classList.add("hide");
    }, 3200);
}

function showSplash() {
    document.getElementById("notStarted").style.display = "flex";
    document.getElementById("notStartedText").innerHTML = (data.teams || [])
        .map(t => `<b>${escapeHtml(t.name)}</b>`)
        .join(" <i>vs</i> ");

    if (view) {
        clearTimeout(view.bannerTimer);
        view.hud.innerHTML = "";
        view = null;
    }
}

function render() {
    if (data === null) return;

    /* livedata starts as {} and is reset to {} when the match ends, so an
       empty snapshot means "not playing" just as much as a missing one. */
    const state = data.claimed === null ? null : buildState(data);
    if (state === null) {
        showSplash();
        return;
    }

    document.getElementById("notStarted").style.display = "none";

    if (view === null || view.signature !== signature(state)) mount(state);
    apply(state);
}

function update() {
    const params = createdby !== null ? { createdBy: createdby } : { id: matchid };
    api.get("/match/overlay", params).then(r => {
        if (r.status === 200) r.json().then(x => { data = x; render(); });
        else canInterval = false;
    });
}

update();
window.setInterval(() => { if (canInterval) update(); }, POLL_MS);
