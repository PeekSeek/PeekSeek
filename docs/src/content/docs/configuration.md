---
title: Configuration
description: Every knob, and which file or variable it lives in
sidebar:
  order: 5
---
Configuration splits by **kind**, not by environment.

| Where | Holds | In the repository |
|---|---|---|
| `src/config.toml` | Everything non-secret | Yes |
| Environment | Secrets, plus three per-deployment overrides | No |
| `kits.json`, `mmconfig.json` | Machine-shaped datasets too structured for TOML | Yes |

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
Everything else in `config.toml` is the same everywhere.

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

## `[data_files]`

Paths, relative to `config.toml`, of the two JSON datasets.

| File | Holds |
|---|---|
| `mmconfig.json` | Gamemode definitions: names, rounds, settings |
| `kits.json` | The kit catalogue: items, amounts, attachment state |

These are JSON rather than TOML because they are machine-shaped: deep, repetitive, and
edited by tooling more often than by hand. Both are the most game-specific files in the
website, and the first two you replace when [forking](/docs/forking/).
