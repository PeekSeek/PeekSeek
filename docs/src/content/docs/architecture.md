---
title: Architecture
description: One FastAPI app, three audiences, and where the data lives
sidebar:
  order: 2
---
One FastAPI app serves three different audiences on one port. That is the whole shape of
the system, and most of what follows is a consequence of it.

| Audience | Gets | Authenticated by |
|---|---|---|
| People | Server-rendered Jinja pages | Steam or Discord login, signed session cookie |
| The site's own scripts | JSON endpoints | The same session cookie |
| Game servers | `/server/v1/*` | The caller's source IP, allow-listed |

---

## Layout

```
website/
├── public/                # served at /static
│   ├── assets/css/        # one stylesheet per page, plus default.css for the shared parts
│   ├── assets/js/         # one script per page
│   └── img/ maps/ unturned/
├── config.yaml         # non-secret settings, repository root
├── gamemodes/          # one file per gamemode
└── src/
    ├── app.py             # assembly: middleware, routers, error pages
    ├── dependencies.py    # current user, auth guards, template renderer
    ├── kits.json          # kit catalog
    ├── core/              # domain logic: settings, db, models, matchmaking, steam
    ├── routes/            # one APIRouter per area
    ├── templates/         # Jinja2 pages
    └── tests/
```

`core/` is where behaviour lives. `routes/` is thin on purpose: a route reads a request,
calls into `core/`, and renders. There is no `utils.py`, and adding one would be a
regression.

---

## The two authentication systems

They have nothing in common and that is intentional.

**People** log in through Steam or Discord and carry a signed session cookie.
Permissions are bits on the user row, named in `permissions` in `config.yaml`, so the
order of that list is a storage format and not a preference.

**Game servers** are recognised by source IP, from the `AuthorizedServers` table. Every
route under `/server/v1` returns `401` to an address that is not in it, and nothing in
the site adds one. You seed the first entries with `BOOTSTRAP_SERVERS`.

:::caution[Reverse proxies and `--forwarded-allow-ips`]
Because server auth trusts `request.client.host`, uvicorn's proxy-header handling
has to be scoped. Setting `--forwarded-allow-ips=*` means any caller can set
`X-Forwarded-For` to an allow-listed address and walk through the IP check. The
shipped [Dockerfile](https://github.com/PeekSeek/PeekSeek/blob/main/website/Dockerfile)
scopes it to private ranges so only the proxy's own hop is trusted. Keep that
property if you change the deployment.
:::

---

## Data

SQLModel over SQLAlchemy, and `DB` takes any SQLAlchemy URL.

**There is no migration tool.** On startup, `create_all` builds tables that do not exist,
and then `add_missing_columns` in `src/core/db.py` adds columns the models have gained
since their table was built, backfilling a model default where there is one. Every
`ALTER` it runs is printed at startup.

What that covers, and what it does not:

| Change | Handled |
|---|---|
| New table | Yes |
| New column | Yes, backfilled from the model default |
| Dropped, renamed or retyped column | **No.** Do it against the database by hand. |

This is a deliberate trade. It means a new SQLModel table reaches production by being
deployed, with no migration step to forget. It also means destructive schema changes are
manual, on purpose.

---

## Configuration splits by kind, not by environment

- **`config.yaml`**, in the repository root, holds everything non-secret: matchmaking
  timings, permission bits, Discord channel ids, the lobby's connect code, and which
  gamemodes the play page shows.
- **`gamemodes/`**, beside it, holds one file per gamemode. Each one carries its own
  queue bit.
- **The environment** holds secrets, plus three overrides (`DOMAIN`, `PROTOCOL`, `BUILD`)
  so one image can serve both dev and production.
- **`kits.json`** holds the kit catalogue, which is machine-shaped data rather than
  settings.

Everything funnels through one `Settings` object in `src/core/settings.py`. Nothing reads
`os.environ` directly, and nothing hardcodes a value that already lives in config.

---

## Third parties never hold a page open

Steam is on the request path of pages that have to render without it, so every call into
it is treated as decoration:

- Lookups are cached, **including failures**, so an outage costs one request per cache
  window rather than one per item per page.
- `Server.describe` does not call Steam at all. It answers from cache and fills the cache
  on a background thread, because the admin list asks about every match on it one after
  another, and a blocking miss multiplies by however many servers are on screen.
- Player names and avatars are persisted on the user row when they are fetched, so an
  ordinary page never has to ask.

If you add a third-party call, match this. A page that renders slowly because a vendor is
slow is a page that goes down when the vendor does.

---

## Front end

Server-rendered Jinja, one stylesheet and one script per page, plus HTMX where a partial
update is worth it. There is no build step for the application code and no framework.

The only build-time transformation is
[`minify.py`](https://github.com/PeekSeek/PeekSeek/blob/main/website/minify.py), which
compresses `public/assets` **in place** during the Docker build. The repository keeps
readable source, the image serves compressed files, and no template has to know which
build it is in. The compressors are uninstalled in the same layer and never reach the
running container.

:::note[One consequence worth knowing]
Because CSS is concatenated per file and served as written, plain cascade rules
apply. Utility classes such as `.hidden` have to come last in a stylesheet to beat
component rules of equal specificity.
:::

---

## Next

- [The match lifecycle](/docs/match-lifecycle/), which is where the design shows
- [The API contract](https://github.com/PeekSeek/PeekSeek/blob/main/website/API.md) in full
- [Forking for another game](/docs/forking/)
