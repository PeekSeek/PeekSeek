<p align="center">
  <img src="website/public/img/cmw.png" alt="PeekSeek" width="180">
</p>

<p align="center">
  <i>Unturned matchmaking and esports platform: the site, and the plugins the game servers run.</i>
</p>

<p align="center">
  <a href="https://github.com/PeekSeek/PeekSeek/actions/workflows/website.yml"><img src="https://github.com/PeekSeek/PeekSeek/actions/workflows/website.yml/badge.svg" alt="website"></a>
  <a href="https://github.com/PeekSeek/PeekSeek/actions/workflows/bot.yml"><img src="https://github.com/PeekSeek/PeekSeek/actions/workflows/bot.yml/badge.svg" alt="bot"></a>
  <a href="https://github.com/PeekSeek/PeekSeek/actions/workflows/peekseek-plugin.yml"><img src="https://github.com/PeekSeek/PeekSeek/actions/workflows/peekseek-plugin.yml/badge.svg" alt="peekseek-plugin"></a>
  <a href="https://github.com/PeekSeek/PeekSeek/actions/workflows/peekseek-arena.yml"><img src="https://github.com/PeekSeek/PeekSeek/actions/workflows/peekseek-arena.yml/badge.svg" alt="peekseek-arena"></a>
  <a href="https://github.com/PeekSeek/PeekSeek/actions/workflows/peekseek-raid.yml"><img src="https://github.com/PeekSeek/PeekSeek/actions/workflows/peekseek-raid.yml/badge.svg" alt="peekseek-raid"></a>
  <a href="https://github.com/PeekSeek/PeekSeek/actions/workflows/peekseek-lobby.yml"><img src="https://github.com/PeekSeek/PeekSeek/actions/workflows/peekseek-lobby.yml/badge.svg" alt="peekseek-lobby"></a>
  <a href="https://github.com/PeekSeek/PeekSeek/actions/workflows/peekseek-armor-skin.yml"><img src="https://github.com/PeekSeek/PeekSeek/actions/workflows/peekseek-armor-skin.yml/badge.svg" alt="peekseek-armor-skin"></a>
  <a href="LICENSE"><img src="https://img.shields.io/badge/licence-MIT-blue" alt="MIT"></a>
  <a href="https://discord.gg/NKgTe6RnvH"><img src="https://img.shields.io/badge/Discord-Join-5865F2?logo=discord&logoColor=white" alt="Discord"></a>
</p>

---

Seven deployables live here. The website is the backend the plugins talk to, which is
why they share a repo: a change to the `/server/v1/*` API and the plugin call that hits
it lands in one commit. That contract is written down in
[website/API.md](website/API.md).

| Component | What it is | Language | Status |
| --- | --- | --- | --- |
| [website/](website/) | The whole site and API: pages, JSON endpoints, and the `/server/v1/*` API game servers run on | FastAPI, Python 3.12 | maintained |
| [plugins/peekseek/](plugins/peekseek/) | PeekSeek Match: the RocketMod plugin that runs a match server, the session, the roster, the score, the result and every in-game screen; gamemodes plug into it | C#, net48 | maintained |
| [plugins/peekseek-arena/](plugins/peekseek-arena/) | PeekSeek ARENA: the arena round, last team standing, as a gamemode for Match | C#, net48 | maintained |
| [plugins/peekseek-raid/](plugins/peekseek-raid/) | PeekSeek RAID: build a base, then take turns cracking each other's, as a gamemode for Match | C#, net48 | maintained |
| [plugins/peekseek-lobby/](plugins/peekseek-lobby/) | RocketMod plugin that runs the waiting arena: queueing from in-game, kits, AFK handling | C#, net48 | maintained |
| [plugins/peekseek-armor-skin/](plugins/peekseek-armor-skin/) | RocketMod plugin that gives every player one of four skin colours and a full military set's damage resistance, whatever they wear | C#, net48 | maintained |
| [bot/](bot/) | Discord bot for staff: `/ban`, `/stop`, `/stats`. Imports the website's own moderation code | Python 3.12 | maintained |

Every one of them is in service and every one of them is MIT. If that ever stops being
true of one, the Status column says so here rather than leaving it to look like a peer of
the rest.

Each component keeps its own README with the detail; start there.
[CONTRIBUTING.md](CONTRIBUTING.md) has the branch model and the conventions.

[docs/](docs/) is the published documentation site: running your own instance, and
forking the platform for another game.

## Working on it

```sh
# website
cd website
pip install -r requirements-dev.lock   # pinned, hashed; requirements.txt is the input
cp .env.example .env
uvicorn src.app:app --reload
pytest -v

# the Discord bot shares the site's code, so it installs it
cd bot
pip install -e ../website -r requirements.txt
python src/bot.py

# any plugin
cd plugins/peekseek           # or peekseek-arena, -raid, -lobby, -armor-skin
dotnet build PeekSeek.sln -c Release
dotnet test PeekSeek.sln
```

Plugin builds pull Rocket/Unturned/Unity assemblies from the
[RestoreMonarchy.RocketRedist](https://www.nuget.org/packages/RestoreMonarchy.RocketRedist)
NuGet package, so no hand-copied `lib/` DLLs are needed. The one assembly no package
carries, `SDG.NetPak.Runtime`, is declared as source under [refs/](refs/) rather than
committed as a binary we have no right to redistribute. The compiled DLL lands in
`plugin/bin/Release/net48/` and drops into the server's `Rocket/Plugins/<name>/`.

## CI

One workflow per component in [.github/workflows/](.github/workflows/), each filtered on
its own path, so touching the website does not run the C# jobs and vice versa. Tags for
plugin releases are prefixed (`peekseek-raid-v0.4.3`) because one repo ships several.

Two workflows are not per-component: `audit.yml` runs `pip-audit` against the lockfiles
on a schedule, and `docs.yml` builds the documentation site with its link checker. No
workflow deploys the website — see below.

## Deployment

The site runs as one container, one Postgres and a reverse proxy, and nothing in it
depends on a particular host. **[Self-hosting](https://peekseek.net/docs/self-hosting/)**
has the whole runbook: the build context, the proxy settings that matter, the two
bootstrap variables, and how to connect a game server.

The one thing worth knowing from here: no website deploy is a CI job. The workflows in
this repo lint, test and release plugins, and the platform watches the branch. So a
website change is live because the platform rebuilt it, not because a merge went green.
The docs site is the exception and does build from CI.
