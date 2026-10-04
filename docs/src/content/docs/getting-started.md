---
title: Getting started
description: Run the website locally against a throwaway database
sidebar:
  order: 1
---
This gets the website running on your machine with a throwaway database. You do not need
a game server, and you do not need Unturned.

## What you need

- Python 3.12
- Git

That is the whole list for a local site. Postgres is what production uses, but you can
skip it for now.

---

## Run it

```sh
git clone https://github.com/PeekSeek/PeekSeek.git
cd PeekSeek/website
pip install -r requirements.txt
cp .env.example .env
```

Open `.env` and set one value:

```sh
DB=sqlite:////absolute/path/to/dev.db
```

Then start it:

```sh
uvicorn src.app:app --reload
```

The site is on [localhost:8000](http://localhost:8000). The interactive API explorer,
generated from the routes themselves, is at
[localhost:8000/api](http://localhost:8000/api) — `/docs` is these pages, which the
app serves from the same port once they are built.

:::note[SQLite is for convenience, not fidelity]
`DB` takes any SQLAlchemy URL, and the schema is built by `create_all` either way.
SQLite will not reproduce Postgres-specific problems, integer width being the
obvious one. Use Postgres before you trust anything you measured.
:::

---

## Make yourself an admin

A fresh database has no admin, and there is no way to promote the first one through the
site. Set this in `.env` and restart once:

```sh
BOOTSTRAP_ADMIN_STEAM64=76561198000000000
```

It creates the account if it has never logged in. The Steam callback matches on
`steam64`, so the row is reused at first login rather than duplicated. Leave it unset in
production.

---

## What works without secrets

Most of it. Every integration no-ops on an empty value rather than crashing, which is
deliberate: it is what lets the dev deployment run without the ability to touch anything
real.

| Unset | What stops |
|---|---|
| `STEAM_API_KEY` | Persona names and avatars. Players still work, they are just nameless. |
| `DISCORD_CLIENT_SECRET` | Discord login. Steam login is unaffected. |
| `BOT_TOKEN` | The Discord player counter. |
| `LOG_WEBHOOK`, `QUEUE_LOG_CHANNEL` | Discord logging. |

Pages backed by data render blank until something seeds them. That is an empty database,
not a broken install.

---

## Run the tests

```sh
pytest -v
```

They use their own database and need no secrets, no network, and no game server. If they
pass on a clean clone, your environment is fine.

---

## Build the plugins

Only if you have a game server to put them on.

```sh
cd plugins/peekseek          # or peekseek-arena, -raid, -lobby, -armor-skin
dotnet build PeekSeek.sln -c Release
dotnet test PeekSeek.sln
```

Builds pull the Rocket, Unturned and Unity assemblies from the
[RestoreMonarchy.RocketRedist](https://www.nuget.org/packages/RestoreMonarchy.RocketRedist)
NuGet package, so there are no hand-copied `lib/` DLLs to find. The compiled DLL lands in
`plugin/bin/Release/net48/` and drops into the server's `Rocket/Plugins/<name>/`.

---

## Next

- [Architecture](/docs/architecture/), if you want to know what you just started
- [Self-hosting](/docs/self-hosting/), when you want it on a real domain
- [Configuration](/docs/configuration/), for every setting in one table
