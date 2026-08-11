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
`/unt/*` endpoints the Unturned game servers hit.

```
public/               # css/js/images, served at /static
src/
├── app.py            # FastAPI assembly: middleware, routers, error pages
├── dependencies.py   # current user, auth guards, template renderer
├── config.toml       # non-secret settings (TOML: humans tune it)
├── kits.json         # kit catalog (JSON: machine-shaped data)
├── mmconfig.json     # gamemode definitions (JSON: machine-shaped data)
├── core/             # domain logic: settings, db, models, matchmaking, steam
├── routes/           # one APIRouter per area
├── templates/        # Jinja2 pages
└── tests/
```

## Installing

```
pip install -r requirements.txt
cp .env.example .env   # fill in the values
```

## Running

```sh
uvicorn src.app:app --reload
```

Interactive API docs: http://localhost:8000/docs

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

`DB` takes any SQLAlchemy URL. Production uses Postgres; a throwaway environment can
point at a file, `sqlite:////absolute/path/dev.db`, and get the same schema from
`create_all`. Note that SQLite will not reproduce Postgres-specific problems — integer
width being the obvious one.

`BOOTSTRAP_ADMIN_STEAM64` exists because a brand-new database contains no admin, and
there is no way to promote the first one through the site. It creates the account if it
has never logged in; the Steam callback matches on `steam64`, so the row is reused at
first login rather than duplicated.

Non-secret config lives in [`src/config.toml`](src/config.toml). The three `[app]`
values are env-overridable so one image can serve both dev and production; everything
else in the file is the same everywhere.

## Docker

```
docker build -t peekseek .
docker run --env-file .env -p 8000:8000 peekseek
```

## Testing

```
pytest -v
```

## Deployments

Pushing to `main` redeploys [dev.peekseek.net](https://dev.peekseek.net) automatically
from this directory. See the deployment table in the [root README](../README.md).
