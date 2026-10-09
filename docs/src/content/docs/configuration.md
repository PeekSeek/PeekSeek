---
title: Configuration
description: Every knob, and which file or variable it lives in
sidebar:
  order: 5
---
Configuration splits by **kind**, not by environment.

| Where | Holds | In the repository |
|---|---|---|
| `config.yaml` | Everything non-secret | Yes |
| `gamemodes/*.yaml` | One file per gamemode | Yes |
| Environment | Secrets, plus three per-deployment overrides | No |
| `kits.json` | The kit catalogue | Yes |

Both `config.yaml` and `gamemodes/` sit in the repository root. The site, the bot and
the images all read them from there. Set `CONFIG` to point at the file when the loader
cannot find it, which is what the bot does.

Everything funnels through one `Settings` object in `src/core/settings.py`. Nothing reads
`os.environ` directly, and nothing hardcodes a value that already lives in config. If you
add a setting, add it there.

---

## Environment

Only **`DB`** is enforced at startup. Everything else is optional in code, and the
features that depend on it no-op when it is missing. That is what lets a staging
deployment run without the ability to touch anything real.

| Variable | Missing means |
|---|---|
| `DB` | **The app will not start.** Any SQLAlchemy URL. |
| `STEAM_API_KEY` | No persona names or avatars. Players still work, nameless. |
| `DISCORD_CLIENT_SECRET` | No Discord login. Steam login is unaffected. |
| `BOT_TOKEN` | No Discord player counter, and no channel renames. |
| `LOG_WEBHOOK` | No Discord logging. |
| `QUEUE_LOG_CHANNEL` | No queue logging. |
| `SECRET_KEY` | Session cookies are signed with the domain instead. **Set this in production.** |
| `DOMAIN` | Falls back to `[app].domain`. |
| `PROTOCOL` | Falls back to `[app].protocol`. |
| `BUILD` | Falls back to `[app].build`. |
| `BOOTSTRAP_ADMIN_STEAM64` | No admin is created. A fresh database then has none. |
| `BOOTSTRAP_SERVERS` | No game server can authenticate. |

Both bootstraps should be unset once they have done their job. See
[Self-hosting](/docs/self-hosting/#first-run).

---

## `[app]`

```toml
[app]
domain = "127.0.0.1"   # public hostname, no scheme
protocol = "http"
build = "develop"
```

All three are overridable from the environment so one image can serve every deployment.
Everything else in `config.yaml` is the same everywhere.

---

## `[matchmaking]`

Every number that decides whether a roster becomes a match.

| Key | Default | What it does |
|---|---|---|
| `queue_timeout` | 60 | Seconds without a heartbeat before a player drops from the queue |
| `host_grace` | 20 | Seconds past selection a serverless match waits before it is cancelled |
| `party_timeout` | 600 | Seconds with nobody looking at a party before it disbands |
| `max_team_elo_gap` | 400 | Widest average-skill gap allowed between the two teams |
| `rematch_cooldown` | 300 | Seconds before the same two players can be put against each other again |
| `match_cooldown` | 1 | Seconds after one match is made before the next can be, site-wide |

See [the match lifecycle](/docs/match-lifecycle/#what-decides-a-roster) for how the gap and
the rematch cooldown interact. One repeated pairing rejects a whole roster.

---

## `[lobby]`

```toml
[lobby]
connect_code = "85568392936415589"
```

The server code `/lobby` hands to the game. This is the steamID64 the server's login
token owns, so it survives restarts.

:::note[Store the code, never the address]
A server running with FakeIP protection is given a fresh `169.254.x.x:port` by
Steam's relay every session, so the address is worth nothing written down. The code
beside it in the server console changes only when the login token does.

Read a new one off the console after a token change. It is the line beginning
`Server Code:`.
:::

---

## `[permissions]`

```toml
[permissions]
list = ["admin", "canWriteBlog", "premium"]
```

:::danger[Order is a storage format]
A permission's index in this list is its **bit** in `Users.permissions`. Reordering
the list silently reassigns everyone's permissions. Append only.
:::

---

## `[discord]`

| Key | What it does |
|---|---|
| `client_id` | OAuth client id. Not a secret; the secret is `DISCORD_CLIENT_SECRET`. |
| `invite_url` | Where `/discord` redirects. |
| `counter_channel` | Voice channel renamed to the live player count. A channel id is not a secret. |
| `counter_interval` | Seconds between renames. Discord allows two per ten minutes and answers the third with a `429`, so this cannot usefully go below ~300. |
| `wake_ping_after` | Seconds an arena must have been empty before the first player to walk in is worth announcing. |

Nothing renames the channel unless `BOT_TOKEN` is set, which is what keeps a development
instance from renaming the production one.

---

## `[regions.names]`

```toml
[regions.names]
paris = "EU-West"
utah = "US-West"
singapore = "AS-South"
brazil = "SA-East"
```

Region id on the left, display name on the right. The ids are what `BOOTSTRAP_SERVERS`
assigns to each game server, and what matchmaking groups players by. Add a region here
before allow-listing a server into it.

---

## Gamemodes

Every file in `gamemodes/` is a gamemode. The file name is the gamemode, in lower case,
with a hyphen for each space.

Each file carries its own `queueBit`. That number is the mode's bit in a queue mask, so
it must be unique, and the set of bits must have no gaps. The loader refuses to start
otherwise. Only ever give a new mode the next free bit; moving an existing one changes
what every mask already sent means.

The `gamemodes` list in `config.yaml` says which modes the play page shows, and in what
order. A mode left out of that list keeps its bit but no player can select it.

A gamemode's `name` is sent to the game server, which uses it to pick the plugin that
runs the match. The match plugin matches by prefix, so `RAID 2v2` needs a plugin named
`RAID`. Renaming a mode therefore needs a plugin release.

## `kitsFile`

The path of the kit catalogue, relative to `config.yaml`. It holds items, amounts and
attachment state. It and `gamemodes/` are the most game-specific parts of the website,
and the first you replace when [forking](/docs/forking/).
