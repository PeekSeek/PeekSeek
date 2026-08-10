<p align="center">
  <img src="website/public/img/cmw.png" alt="PeekSeek" width="180">
</p>

<p align="center">
  <i>Unturned matchmaking and esports platform: the site, and the plugins the game servers run.</i>
</p>

<p align="center">
  <a href="https://github.com/PeekSeek/PeekSeek/actions/workflows/website.yml"><img src="https://github.com/PeekSeek/PeekSeek/actions/workflows/website.yml/badge.svg" alt="website"></a>
  <a href="https://github.com/PeekSeek/PeekSeek/actions/workflows/peekseek-plugin.yml"><img src="https://github.com/PeekSeek/PeekSeek/actions/workflows/peekseek-plugin.yml/badge.svg" alt="peekseek-plugin"></a>
  <a href="https://github.com/PeekSeek/PeekSeek/actions/workflows/utelemetry.yml"><img src="https://github.com/PeekSeek/PeekSeek/actions/workflows/utelemetry.yml/badge.svg" alt="utelemetry"></a>
  <a href="https://discord.gg/NKgTe6RnvH"><img src="https://img.shields.io/badge/Discord-Join-5865F2?logo=discord&logoColor=white" alt="Discord"></a>
</p>

---

Three deployables live here. The website is the backend the plugins talk to, which is
why they share a repo: a change to the `/unt/*` endpoints and the plugin call that hits
them lands in one commit.

| Component | What it is | Language |
| --- | --- | --- |
| [website/](website/) | The whole site and API: pages, JSON endpoints, and the `/unt/*` endpoints game servers call | FastAPI, Python 3.12 |
| [plugins/peekseek/](plugins/peekseek/) | RocketMod plugin that runs matches: lobby lifecycle, match state, ping-based damage scaling | C#, net48 |
| [plugins/utelemetry/](plugins/utelemetry/) | RocketMod plugin for PvP telemetry beyond kill counts | C#, net48 |

Each component keeps its own README with the detail; start there.

## Working on it

```sh
# website
cd website
pip install -r requirements.txt
cp .env.example .env          # fill in the values
uvicorn src.app:app --reload  # :8000, docs at /docs
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

## Deployment

The website deploys from this repo on [Dokploy](https://hosting.kcvdh.com) (project
`PeekSeek.net`), built from [website/Dockerfile](website/Dockerfile) and served on port
8000 behind Traefik.

| Environment | Domain | Source | Database |
| --- | --- | --- | --- |
| production | peekseek.net (+ esports, api, admin) | `PeekSeek/website@main` | production Postgres |
| develop | [dev.peekseek.net](https://dev.peekseek.net) | this repo, `main`, Dockerfile `website/Dockerfile` | its own Postgres, empty |

Both auto-deploy on push. Production still builds from the old single-purpose
`PeekSeek/website` repo until it is cut over to this one.

The dev database is empty, with the schema SQLModel creates on startup; pages backed by
data render blank until it is seeded. Dev also runs with `BOT_TOKEN`, `LOG_WEBHOOK` and
`QUEUE_LOG_CHANNEL` deliberately unset, so it can never DM real players or post into
production Discord channels — every caller no-ops on an empty value.
