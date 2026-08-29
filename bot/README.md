# The moderation bot

Three slash commands for staff: `/ban`, `/stop`, `/stats`.

## Why it lives in this repo

It imports `website/src/core`, so `moderation.issueBan` is literally the same function
the admin dashboard calls. A ban issued from Discord and a ban issued from the site
cannot mean different things, because there is only one of them.

That is also why [Dockerfile](Dockerfile) is built from the **repository root** rather
than from `bot/`: the image has to contain both trees.

```sh
docker build -f bot/Dockerfile -t peekseek-bot .
```

## Commands

| Command | Options | What it does |
|---|---|---|
| `/ban` | `reason`\*, `player`, `steam64`, `days`, `note` | Bans a player. Cheating asks for confirmation first. |
| `/stop` | `matchid`\* | Ends a match. Nobody's Elo moves and its players are freed. |
| `/stats` | `player`, `steam64` | Elo, record, and ban history. Defaults to the caller. |

\* required.

`reason` is a required choice of Cheating, Matchfixing, Toxicity or Custom. Discord
cannot make one option's requirement depend on another's value, so a set of five
optional fields has a lot of ways to be wrong; a required choice removes the most likely
one before it can happen. `days` and `note` are only required under Custom.

You need either `player` or `steam64`. A Discord member is resolved through the account
they linked; when they have not linked one the bot says exactly that and tells you to
pass the steam64, rather than failing with a lookup error.

### The cheating confirmation

A cheating ban voids matches and moves other people's Elo, so it never happens on one
keystroke. The bot runs `moderation.previewBan` first and shows the real blast radius:
how many matches, how many players, and the Elo each of them gets back. The buttons stop
working after 60 seconds, because a live Ban button sitting in old scrollback is its own
hazard.

## Who counts as an admin

Two layers, and only the second is a gate.

`default_permissions(administrator=True)` hides the commands from most members in the
picker. That is a **display** filter: a Discord server admin is not necessarily a
PeekSeek admin.

Every command then resolves the caller's Discord id to their `Users` row and checks the
PeekSeek admin bit, exactly as `_requireAdmin` does on the website. That is the real
check, and an unlinked or non-admin caller gets one ephemeral line saying so.

## Configuration

| Variable | Needed | What |
|---|---|---|
| `BOT_TOKEN` | yes | The Discord bot token. |
| `DB` | yes | The same Postgres the website uses. |
| `DOMAIN`, `PROTOCOL` | no | Override the links the bot puts in embeds. |
| `WEBSITE_SRC` | no | Where the website tree is. The image sets it. |

Everything else comes from `website/src/config.toml`, which the image ships.

## Running it locally

```sh
pip install -r website/requirements.txt -r bot/requirements.txt
DB=... BOT_TOKEN=... WEBSITE_SRC=website python bot/src/bot.py
```

Needs Python 3.12 or newer. On 3.13+ use discord.py 2.6 or later: `audioop` was removed
from the standard library and older versions import it at module load.
