<p align="center">
  <img src="public/img/cmw.png" alt="PeekSeek" width="180">
</p>

<p align="center">
  <i>Unturned matchmaking and esports platform.</i>
</p>

<p align="center">
  <a href="https://github.com/PeekSeek/PeekSeek/actions/workflows/website.yml"><img src="https://github.com/PeekSeek/PeekSeek/actions/workflows/website.yml/badge.svg" alt="website"></a>
  <a href="https://www.python.org/"><img src="https://img.shields.io/badge/python-3.12-blue?logo=python&logoColor=white" alt="Python 3.12"></a>
  <a href="https://github.com/psf/black"><img src="https://img.shields.io/badge/code%20style-black-000000.svg" alt="Code style: black"></a>
  <a href="https://discord.gg/NKgTe6RnvH"><img src="https://img.shields.io/badge/Discord-Join-5865F2?logo=discord&logoColor=white" alt="Discord"></a>
</p>

---

PeekSeek is a single FastAPI app (port **8000**) that serves everything: the
server-rendered pages, the JSON endpoints the site's own scripts call, and the
`/server/v1/*` API the game servers run on, see [API.md](API.md) for that contract.

```
public/               # served at /static
├── assets/css/       # one stylesheet per page, plus default.css for everything shared
├── assets/js/        # one script per page
├── img/ ranks/ sounds/ unturned/
scripts/              # tools you run by hand, never at request time
tooling/              # the JS and CSS linters; kept out of the runtime image
src/
├── app.py            # FastAPI assembly: middleware, routers, error pages
├── dependencies.py   # current user, auth guards, template renderer
├── config.toml       # non-secret settings (TOML: humans tune it)
├── kits.json         # kit catalog (JSON: machine-shaped data)
├── mmconfig.json     # gamemode definitions (JSON: machine-shaped data)
├── core/             # domain logic: settings, db, models, matchmaking, steam
├── routes/           # one APIRouter per area
│   └── contracts.py  # the /server/v1 request and response models
├── templates/        # Jinja2 pages
└── tests/
```

## Installing

```sh
pip install -r requirements-dev.lock   # pinned and hash-checked; runtime plus the tools
cp .env.example .env                   # fill in the values
```

`requirements.txt` is the list a human edits. `requirements.lock` is what anything
installs, every transitive package pinned to a version and a hash, compiled for 3.12,
which is what the image runs. Change a dependency and regenerate both locks with the
commands in the header of `requirements.txt`.

The site is also an installable package (`pyproject.toml`), which is how the Discord bot
depends on `src.core` instead of inserting this directory into `sys.path`.

## Running

```sh
uvicorn src.app:app --reload
```

Interactive API explorer: http://localhost:8000/api, `/docs` is the handbook, which
is why the explorer is not there.

It lists the JSON API and nothing else. Server-rendered pages are declared
`include_in_schema=False`, every router carries a tag, and the `/server/v1/*` routes state
their response shapes from `src/routes/contracts.py`, so the explorer is a reference rather
than a list of every URL the app happens to answer.

## Environment

| Variable | Required | Description |
|---|---|---|
| `DB` | ✅ | PostgreSQL connection string |
| `DISCORD_CLIENT_SECRET` | ✅ | Discord OAuth2 client secret |
| `BOT_TOKEN` | ✅ | Discord bot token |
| `STEAM_API_KEY` | ✅ | Steam Web API key |
| `SECRET_KEY` | | Session-cookie signing key (defaults to the domain) |
| `LOG_WEBHOOK` | | Discord webhook for logging |
| `QUEUE_LOG_CHANNEL` | | Discord channel ID for queue logs |
| `DOMAIN` | | Overrides `[app].domain`; set per deployed environment |
| `PROTOCOL` | | Overrides `[app].protocol` |
| `BUILD` | | Overrides `[app].build` |
| `BOOTSTRAP_ADMIN_STEAM64` | | Grants admin to one account on startup; leave unset in production |
| `BOOTSTRAP_SERVERS` | | Allow-lists game-server IPs on startup, `address=region,address=region`; leave unset in production |

`DB` takes any SQLAlchemy URL. Production uses Postgres; a throwaway environment can
point at a file, `sqlite:////absolute/path/dev.db`, and get the same schema from
`create_all`. Note that SQLite will not reproduce Postgres-specific problems, integer
width being the obvious one.

There is no migration tool. `create_all` builds tables that do not exist yet, and
`add_missing_columns` in [`src/core/db.py`](src/core/db.py) then adds columns the models
have gained since their table was built, backfilling the model's default where there is
one. Each `ALTER` it runs is printed at startup. Dropping, renaming or retyping a column
is not covered, and still has to be done against the database by hand.

`BOOTSTRAP_ADMIN_STEAM64` exists because a brand-new database contains no admin, and
there is no way to promote the first one through the site. It creates the account if it
has never logged in; the Steam callback matches on `steam64`, so the row is reused at
first login rather than duplicated.

`BOOTSTRAP_SERVERS` exists for the same reason on the other side of the contract: every
`/server/v1/*` route 401s an IP that is not in `AuthorizedServers`, and nothing in the
site adds one. Regions come from `[regions.names]` in `config.toml`. Addresses already present are
left alone, so changing a region here does not overwrite the stored one.

Non-secret config lives in [`src/config.toml`](src/config.toml). The three `[app]`
values are env-overridable so one image can serve both dev and production; everything
else in the file is the same everywhere.

## Docker

```
docker build -t peekseek .
docker run --env-file .env -p 8000:8000 peekseek
```

Build the image from the **repository root**, not from here: it also carries the
documentation site built from `docs/`, so every `COPY` is relative to the root.

```sh
docker build -f website/Dockerfile -t peekseek ..
```

The build runs [`minify.py --in-place`](minify.py) over `public/assets` after copying it
in, so the image serves compressed CSS and JS while the repo keeps the readable source. The
compressors are uninstalled in the same layer and never reach the running container. The
`--in-place` is not decoration: run `minify.py` bare and it reports what it would save and
writes nothing, because run in a checkout it would replace every stylesheet and script with
its minified form.

The container runs as a non-root user.

## Testing and linting

```sh
pytest -v

black --check ..                       # from the repo root: this, the bot and the tools
cd tooling && npm install && npm run lint   # prettier over js/, stylelint over css/
```

Black is the only Python formatter here, configured in the root `pyproject.toml` and run
from the root so the bot, the sound generator and the Unity build tools are covered too.
Ruff is deliberately not configured: two formatters with no shared configuration disagree,
and its caches are gitignored.

## Deployment

One container, one Postgres, a reverse proxy, and no CI job, see
[Self-hosting](https://peekseek.net/docs/self-hosting/) for the whole runbook and the
[root README](../README.md) for why a website deploy is never a green build.
