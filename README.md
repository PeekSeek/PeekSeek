<p align="center">
  <img src="website/public/img/cmw.png" alt="PeekSeek" width="180">
</p>

<p align="center">
  <i>Unturned matchmaking and esports platform: the site, and the plugins the game servers run.</i>
</p>

<p align="center">
  <a href="https://github.com/PeekSeek/PeekSeek/actions/workflows/website.yml"><img src="https://github.com/PeekSeek/PeekSeek/actions/workflows/website.yml/badge.svg" alt="website"></a>
  <a href="https://github.com/PeekSeek/PeekSeek/actions/workflows/peekseek-plugin.yml"><img src="https://github.com/PeekSeek/PeekSeek/actions/workflows/peekseek-plugin.yml/badge.svg" alt="peekseek-plugin"></a>
  <a href="https://github.com/PeekSeek/PeekSeek/actions/workflows/peekseek-lobby.yml"><img src="https://github.com/PeekSeek/PeekSeek/actions/workflows/peekseek-lobby.yml/badge.svg" alt="peekseek-lobby"></a>
  <a href="https://github.com/PeekSeek/PeekSeek/actions/workflows/utelemetry.yml"><img src="https://github.com/PeekSeek/PeekSeek/actions/workflows/utelemetry.yml/badge.svg" alt="utelemetry"></a>
  <a href="https://github.com/PeekSeek/PeekSeek/actions/workflows/peekseek-armor-skin.yml"><img src="https://github.com/PeekSeek/PeekSeek/actions/workflows/peekseek-armor-skin.yml/badge.svg" alt="peekseek-armor-skin"></a>
  <a href="https://discord.gg/NKgTe6RnvH"><img src="https://img.shields.io/badge/Discord-Join-5865F2?logo=discord&logoColor=white" alt="Discord"></a>
</p>

---

Six deployables live here. The website is the backend the plugins talk to, which is
why they share a repo: a change to the `/server/v1/*` API and the plugin call that hits
it lands in one commit. That contract is written down in
[website/API.md](website/API.md).

| Component | What it is | Language |
| --- | --- | --- |
| [website/](website/) | The whole site and API: pages, JSON endpoints, and the `/server/v1/*` API game servers run on | FastAPI, Python 3.12 |
| [plugins/peekseek/](plugins/peekseek/) | RocketMod plugin that runs matches: lobby lifecycle, match state, ping-based damage scaling | C#, net48 |
| [plugins/peekseek-lobby/](plugins/peekseek-lobby/) | RocketMod plugin that runs the waiting arena: queueing from in-game, kits, AFK handling | C#, net48 |
| [plugins/utelemetry/](plugins/utelemetry/) | RocketMod plugin for PvP telemetry beyond kill counts | C#, net48 |
| [plugins/peekseek-armor-skin/](plugins/peekseek-armor-skin/) | RocketMod plugin that gives every player one of four skin colours and a full military set's damage resistance, whatever they wear | C#, net48 |
| [bot/](bot/) | Discord bot for staff: `/ban`, `/stop`, `/stats`. Imports the website's own moderation code | Python 3.12 |

Each component keeps its own README with the detail; start there.
[CONTRIBUTING.md](CONTRIBUTING.md) has the branch model and the conventions.

[docs/](docs/) is the published documentation site: running your own instance, and
forking the platform for another game.

## Working on it

```sh
# website
cd website
pip install -r requirements.txt
cp .env.example .env
uvicorn src.app:app --reload
pytest -v

# either plugin
cd plugins/peekseek           # or plugins/utelemetry
dotnet build PeekSeek.sln -c Release
dotnet test PeekSeek.sln
```

Plugin builds pull Rocket/Unturned/Unity assemblies from the
[RestoreMonarchy.RocketRedist](https://www.nuget.org/packages/RestoreMonarchy.RocketRedist)
NuGet package, so no hand-copied `lib/` DLLs are needed. The compiled DLL lands in
`plugin/bin/Release/net48/` and drops into the server's `Rocket/Plugins/<name>/`.

## CI

One workflow per component in [.github/workflows/](.github/workflows/), each filtered on
its own path, so touching the website does not run the C# jobs and vice versa. Tags for
plugin releases are prefixed (`utelemetry-v1.2.3`) because one repo now ships several.

`docs.yml` is the one workflow that deploys anything: a change under `docs/` on `main`
builds the site with `--strict` and publishes it to GitHub Pages. Everything else lints,
tests, or releases a plugin.

## Deployment

The website deploys from this repo on [Dokploy](https://hosting.kcvdh.com) (project
`PeekSeek.net`), built from [website/Dockerfile](website/Dockerfile) and served on port
8000 behind Traefik.

| Environment | Domain | Source | Database |
| --- | --- | --- | --- |
| production | [peekseek.net](https://peekseek.net) (+ `www`) | this repo, `main` | production Postgres |
| develop | [dev.peekseek.net](https://dev.peekseek.net) | this repo, `main`, Dockerfile `website/Dockerfile` | its own Postgres, empty |

Both auto-deploy on push, and neither website deploy is a CI job: the workflows in this
repo lint, test and release plugins, and Dokploy is what watches the branch. So a website
change is live because Dokploy rebuilt it, not because a merge went green. The docs site
is the exception and does deploy from CI.

Production and develop are separate Dokploy applications. Only one of them may hold a
given hostname: two applications claiming the same domain leaves Traefik with two routers
for one rule, and a redeploy can flip which one wins, which looks like a healthy
container serving `502`.

The production Postgres in that table is also the database
[data.peekseek.net](https://data.peekseek.net) reads. Anything the site creates on
startup — a new SQLModel table, a column `addMissingColumns` fills in — shows up there
one deploy later, which is the only way a new table ever reaches the analytics.

The dev database is empty, with the schema SQLModel creates on startup; pages backed by
data render blank until it is seeded. Dev also runs with `BOT_TOKEN`, `LOG_WEBHOOK` and
`QUEUE_LOG_CHANNEL` deliberately unset, so it can never DM real players or post into
production Discord channels — every caller no-ops on an empty value.
