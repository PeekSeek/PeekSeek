/* Stream overlay renderer.
 *
 * Polls /match/overlay and draws the live snapshot the game server pushes to
 * /server/v1/matches/{id}/state (see PSUpdateState.cs). The structure is only rebuilt
 * when the roster changes; every other tick patches text and widths in place, because
 * re-creating the DOM twice a second would restart the kill feed and health animations
 * on every poll.
 *
 * Before the first round there is no snapshot to draw, so the pre-match screen takes
 * over: the intermission roster read-out while a match is being picked, and a bare
 * splash when there is not even a roster yet.
 */

const POLL_MS = 250;
const KILL_LIFETIME_MS = 6000;
const ICON_BASE = "/static/unturned/";
const DEFAULT_ROUNDS_TO_WIN = 3;

/* Inverse fillets for the notch. Left is the notch colour minus a quarter
   disc centred at (0, 10); right is the same mirrored, centred at (10, 10).
   Because the centre sits on the top edge the shape is widest at y = 0, which
   is what makes the notch flare outward into the straight line. */
const FLARE_L = `<svg class="flare l" viewBox="0 0 10 10" aria-hidden="true"><path d="M0 0 L10 0 L10 10 A10 10 0 0 0 0 0 Z"/></svg>`;
const FLARE_R = `<svg class="flare r" viewBox="0 0 10 10" aria-hidden="true"><path d="M10 0 L0 0 L0 10 A10 10 0 0 1 10 0 Z"/></svg>`;

/* Says what the number beside it counts, since a bare figure at the end of the loadout
   row could be read as anything the card carries. A cartridge, drawn once here and
   mounted with the card rather than rewritten on every poll. */
const ROUNDS = `<svg class="rd" viewBox="0 0 8 20" aria-label="rounds">
  <path d="M4 0 C6.2 3 7 5.6 7 8.4 L1 8.4 C1 5.6 1.8 3 4 0 Z"/>
  <path d="M1 9.6 L7 9.6 L7 20 L1 20 Z"/>
</svg>`;

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

/* The weapon they brought, which holds the front of the row for the whole round. This
   used to be whatever they had in their hands, so the row reshuffled every time they
   pulled out a medkit or the crossbow and the gun dropped back into alphabetical-looking
   inventory order. A powerup can hand out a second gun, the rocket launcher or the
   pistol, and that is not the weapon they fight with, so it stays with the rest of the
   powerup's items. */
function weapon(user) {
    return user.inventory.find(item =>
        (item.kind === "gun" || item.kind === "melee") && !user.powerupIds.has(item.id)) || null;
}

/* Everything they carry except the weapon already shown in the first slot: their own
   kit first, then whatever the powerup granted. */
function loadout(user, held) {
    const rest = user.inventory.filter(item => item !== held);
    return [
        ...rest.filter(item => !user.powerupIds.has(item.id)),
        ...rest.filter(item => user.powerupIds.has(item.id))
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
                        inventory: (pick(stats, "inventory", "Inventory") || []).map(item => ({
                            id: pick(item, "id", "Id"),
                            kind: pick(item, "kind", "Kind") || "",
                            amount: pick(item, "amount", "Amount") ?? 1
                        })),
                        totalAmmo: pick(stats, "totalAmmo", "TotalAmmo") ?? 0
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
              <span class="ammo">${ROUNDS}<span></span></span>
              <span class="reveal"></span>
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
            ammo: el.querySelector(".ammo"),
            rounds: el.querySelector(".ammo span"),
            reveal: el.querySelector(".reveal"),
            gauge: el.querySelector(".hp i"),
            health: null,
            loadout: null
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
        bannerTimer: null,
        /* Which round the reveal-cover slide-away has already played for, so a fresh
           poll mid-round does not replay it — see apply(). */
        revealedRound: null
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

    /* Only the moment the round number itself changes counts as "round start" — every
       other poll this tick just re-confirms the cover should stay wherever it already
       animated to. */
    const roundStarted = view.revealedRound !== state.round;
    view.revealedRound = state.round;

    seatsOf(state).forEach(({ user }, i) => {
        const c = view.cards.get(i);
        if (!c) return;

        c.root.classList.toggle("out", !inPlay(user));
        c.nm.textContent = user.name;
        c.stats.innerHTML =
            `<span><b>${user.kills}</b>K</span>` +
            `<span><b>${user.deaths}</b>D</span>` +
            `<span><b>${hsPercent(user)}</b>%</span>`;
        /* Rewritten only when what they carry actually changes. Rebuilding this row every
           poll throws away and re-decodes every icon on the card, and the source icons
           are whole-megapixel PNGs, so it is wasted work that only gets worse the faster
           this polls. Everything else on the card is patched in place, and this was the
           one piece that was not. */
        const held = weapon(user);
        const carried = loadout(user, held);
        const kit = [held ? held.id : 0, ...carried.map(item => `${item.id}x${item.amount}`)].join(" ");
        if (c.loadout !== kit) {
            c.loadout = kit;
            c.kit.innerHTML = (held ? slotHtml(held, "gun") : "") +
                carried.map(item => slotHtml(item)).join("");
        }

        /* No count at all rather than a bare 0 for a melee kit that was never going to
           have rounds in the first place, and the cartridge goes with it. */
        c.ammo.classList.toggle("has", user.totalAmmo > 0);
        c.rounds.textContent = user.totalAmmo > 0 ? user.totalAmmo : "";

        c.reveal.textContent = user.powerup || "";
        c.reveal.classList.toggle("has", !!user.powerup);
        /* The cover is only ever placed or pulled back at the start of a round. Someone
           already out keeps it, since there is nothing under it worth revealing, and
           everyone else gets it slid away. Dying mid-round does not put it back: their
           loadout is exactly what a caster wants to see once they are down. */
        if (roundStarted) {
            c.reveal.classList.remove("leaving");
            if (user.state !== "dead") {
                void c.reveal.offsetWidth;
                c.reveal.classList.add("leaving");
            }
        }

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

/* What fills the gap before the first round. A match that has a roster gets the
   intermission — both teams laid out with what they have picked — and anything else
   falls back to the bare splash, since there is nothing to read out yet. */
function showPreMatch() {
    const screen = document.getElementById("notStarted");
    screen.style.display = "flex";

    const teams = data.teams || [];
    const readable = teams.length === 2 && teams.every(t => (t.users || []).length);
    screen.classList.toggle("intermission", readable);
    screen.classList.toggle("waiting", !readable);

    if (readable) drawIntermission(teams);
    else document.getElementById("notStartedText").innerHTML = teams
        .map(t => `<b>${escapeHtml(t.name)}</b>`)
        .join(" <i>vs</i> ");

    if (view) {
        clearTimeout(view.bannerTimer);
        view.hud.innerHTML = "";
        view = null;
    }
}

/* Redrawn on every poll rather than patched in place, unlike the in-play HUD. Nothing
   here animates — there is no kill feed to restart and no health bar to interrupt — so
   the rebuild costs nothing, and a player locking in a pick shows up immediately. */
function drawIntermission(teams) {
    const [left, right] = teams;

    document.getElementById("imLeftName").textContent = left.name;
    document.getElementById("imRightName").textContent = right.name;
    document.getElementById("imClock").innerHTML = clockHtml();

    const config = data.config || {};
    const params = data.params || {};
    document.getElementById("imMode").textContent =
        [config.name, params.map].filter(Boolean).join(" · ");
    document.getElementById("imRules").textContent = rulesText(params);

    document.getElementById("imLeft").innerHTML = groupHtml(left);
    document.getElementById("imRight").innerHTML = groupHtml(right);
}

/* Counts down to the moment picking closes. Once it has passed the match is only
   waiting on its server, and saying "0:00" forever would read as broken. */
function clockHtml() {
    const remaining = Math.ceil((data.startsAfter || 0) - Date.now() / 1000);
    if (remaining <= 0) return "Starting";

    const seconds = String(remaining % 60).padStart(2, "0");
    return `Starts in <b>${Math.floor(remaining / 60)}:${seconds}</b>`;
}

/* Undecided settings are named as such rather than guessed at — during picking these
   are genuinely still open, and showing a default would be a lie the moment somebody
   picks the other one. */
function rulesText(params) {
    const setting = (label, value, on, off) =>
        value === null || value === undefined ? `${label} undecided` : (value ? on : off);

    return [
        setting("Bullet drop", params.ballistics, "Bullet drop on", "Bullet drop off"),
        setting("Movement", params.movement, "Old movement", "New movement")
    ].join(" · ");
}

function groupHtml(team) {
    const cards = (team.users || []).map(cardHtml).join("");
    return `<div class="group"><span class="tname">${escapeHtml(team.name)}</span>${cards}</div>`;
}

/* A player who has not picked yet is shown pending rather than left out, so the column
   is its final height from the first frame and nothing shifts as picks land. */
function cardHtml(steam64) {
    const user = (data.users || {})[steam64] || {};
    const name = user.name || steam64;
    const picks = (data.params?.userData || {})[steam64] || {};

    /* The gun itself rather than the name of the kit it came out of. "Meta" tells a
       viewer nothing about what the player is holding, and the icon is the same one
       the in-play card shows, so the two screens name the weapon the same way. */
    const gun = user.gun ? slotHtml({ id: user.gun, amount: 1 }, "gun") : "";

    const body = picks.kit
        ? `${gun}<span class="pw">${escapeHtml(picks.powerup || "No powerup")}</span>`
        : `<span class="waiting">picking…</span>`;

    return `<div class="card flat${picks.kit ? "" : " pending"}">
      <span class="nm">${escapeHtml(name)}</span>
      <div class="l2">${body}</div>
    </div>`;
}

function render() {
    if (data === null) return;

    /* livedata starts as {} and is reset to {} when the match ends, so an
       empty snapshot means "not playing" just as much as a missing one. */
    const state = data.status === "live" ? buildState(data) : null;
    if (state === null) {
        showPreMatch();
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
