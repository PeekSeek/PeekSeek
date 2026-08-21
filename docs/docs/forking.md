# Forking for another game

PeekSeek runs Unturned, but very little of it knows that. This page is an honest
inventory of what you would have to replace, ordered by how much work each part actually
is.

---

## The part you get for free

`/server/v1/*` was built to not care what game it is. From
[API.md](https://github.com/PeekSeek/PeekSeek/blob/main/website/API.md):

> It is deliberately not Unturned-specific: a server for a different game implements the
> same calls and speaks the same JSON.

That is not aspiration. The website has no import, no branch and no model that reaches
into Unturned. Matchmaking, Elo, parties, queueing, events, the admin panel, the stream
overlay and the Discord integration are all game-agnostic already.

**Your real job is writing one agent for your game that implements the polling loop.**
Everything below is smaller than that.

---

## What you actually write

A program that runs on your game server and does this:

1. Poll `GET /server/v1/session` with `serverCode` and `matchId`.
2. Switch on `action`: `idle`, `wait`, `host`, `abort`.
3. On `host`, start a match from the payload: teams, per-player loadouts, settings.
4. Stream `POST /matches/{id}/kills` and `POST /matches/{id}/state` as things happen.
5. `POST /matches/{id}/result` at the end, **retrying until acknowledged**.

That is the entire contract. It is HTTP and JSON, so it can be a RocketMod plugin, a
SourceMod extension, a Minecraft plugin, a sidecar process, or a few hundred lines in
whatever language your game's modding API speaks.

Read [the match lifecycle](match-lifecycle.md) before you start. The retry semantics are
the part people get wrong.

The two shipped plugins are the reference implementation:

| Plugin | Implements |
|---|---|
| [`plugins/peekseek`](https://github.com/PeekSeek/PeekSeek/tree/main/plugins/peekseek) | The match loop above |
| [`plugins/peekseek-lobby`](https://github.com/PeekSeek/PeekSeek/tree/main/plugins/peekseek-lobby) | The waiting arena, queueing from in-game, kits, AFK |

---

## Layer 1: the launch link (an afternoon)

Unturned takes a server code on its launch command line, and there is no URL scheme that
joins by code directly, so every join on the site goes through `steam://run/<appid>`.

The app id `304930` is currently hardcoded in four places:

| File | Line |
|---|---|
| `src/routes/pages.py` | `UNTURNED_APP_ID` |
| `src/templates/app/spectate.html` | twice, in the card link and the spectate button |
| `public/assets/js/admin.js` | the spectate button |
| `public/assets/js/match.js` | `connectUrl()` |

If your game joins differently, this is where you change it. Lifting it into
`config.toml` first is a good first commit for a fork, and a welcome pull request back.

---

## Layer 2: game data (a weekend)

Three things describe Unturned specifically. All are data, not code.

| What | Where | Holds |
|---|---|---|
| Kits | `src/kits.json` | Item ids, amounts, attachment state bytes |
| Gamemodes | `src/mmconfig.json` | Modes, rounds to win, maps, per-mode settings |
| Item metadata | `UnturnedItem` in `src/core/models/stat.py`, table `unturned_items` | Item names for display |
| Assets | `public/unturned/`, `public/maps/` | Icons and map images |

The shapes are general. A "kit" is a list of things a player spawns with, and a
"gamemode" is a named set of rules with a win condition. Whether an item id means an
Unturned gun or a Counter-Strike weapon is not something the site inspects.

`UnturnedItem` is the one badly named table, since it is really "a display name for a
game item". Renaming it is a schema change and therefore manual, per
[Architecture](architecture.md#data).

---

## Layer 3: identity (the real work)

This is the one to think about before starting.

PeekSeek identifies players by **steam64**, everywhere. It is the primary key on users,
the id in every API payload, and the thing the login flow returns.

| Coupling | Why it exists |
|---|---|
| steam64 as user id | Unturned is a Steam game, so every player already has one |
| Steam Web API | Persona names and avatars |
| Steam login | One of the two supported sign-ins |
| GSLT server codes | How a server identifies itself and how players connect |

If your game is on Steam, **none of this is a problem**. Change the app id and the game
data, and you are done.

If it is not on Steam, you are replacing the identity layer. The site is helpful here in
one specific way: steam64s are handled as **strings** throughout, precisely because they
are 64-bit and JSON numbers are not. So the field is already an opaque string id, and
substituting a different id scheme is less invasive than it sounds. Discord login already
exists alongside Steam, which proves the user row does not truly require a Steam identity
to be created.

Budget real time for it anyway. It touches the user model, both login flows, the API
payloads and every template that renders a profile.

---

## What stays untouched

Worth stating plainly, because it is most of the value:

- Matchmaking, Elo, and the balancing rules
- Queueing, parties, and the rematch cooldown
- The whole match lifecycle and its retry semantics
- Events, signups, and tournament pairings
- The admin panel
- The stream overlay
- Discord integration
- Sessions, permissions, bans, and the IP allow-list

---

## A realistic order

1. Get the website running locally against SQLite.
   [Getting Started](getting-started.md).
2. Read [the match lifecycle](match-lifecycle.md) until the retry rules make sense.
3. Allow-list a machine you control with `BOOTSTRAP_SERVERS` and poll `/server/v1/session`
   **by hand**, with curl. You will see `idle`.
4. Queue a player through the site and watch your poll turn into `host`.
5. Only then write the game-side agent. By this point you know the contract.
6. Swap the game data and the launch link.
7. Replace identity, if your game is not on Steam.

Steps 3 and 4 are the ones people skip. They are also the ones that make the rest
obvious.

---

## Contributing back

Fork-friendliness is a goal, so changes that reduce coupling are especially welcome:
lifting the app id into config, generalising `UnturnedItem`, or anything that makes the
identity layer pluggable.

See
[CONTRIBUTING.md](https://github.com/PeekSeek/PeekSeek/blob/main/CONTRIBUTING.md)
for the branch model and conventions. The licence is MIT, so you owe nothing. It is just
nicer this way.
