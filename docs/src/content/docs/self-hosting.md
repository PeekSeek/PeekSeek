---
title: Self-hosting
description: One container, one Postgres, and a reverse proxy
sidebar:
  order: 4
---
A real deployment is one container, one Postgres, and a reverse proxy. Nothing else is
required.

---

## Build and run

```sh
docker build -f website/Dockerfile -t peekseek .
docker run --env-file website/.env -p 8000:8000 peekseek
```

:::caution[The build context is the repository root, not `website/`]
Every `COPY` in the Dockerfile is relative to the repository root, because the image
also carries the documentation site built from `docs/`. Building with `website/` as the
context fails. If your platform asks separately for a Dockerfile path and a context
path, they are `website/Dockerfile` and `.`.

This changed when the docs moved onto the site's own domain. A deployment configured
with the old context has to be pointed at the new one before it will build again.
:::

The image builds `docs/` with Node in a throwaway stage and copies only the result in,
so the running container has no Node in it. The app serves that build at `/docs`, and
mounts nothing if it is absent, which is why a local `uvicorn` with no docs build still
starts.

The build runs `minify.py` over `public/assets` after copying it in, so the image serves
compressed CSS and JS while the repository keeps readable source. The compressors are
uninstalled in the same layer and never reach the running container.

---

## Behind a reverse proxy

The container listens on `8000`. Point your proxy at it and terminate TLS there.

:::danger[Do not set `--forwarded-allow-ips=*`]
Game-server authentication trusts `request.client.host`, and uvicorn's
`ProxyHeadersMiddleware` rewrites that from `X-Forwarded-For` whenever the peer is in
the allowed list. With `*`, anyone hitting the container can set `X-Forwarded-For` to
an allow-listed address and pass the `/server/v1/*` IP check with no other access.

The shipped `CMD` scopes it to Docker's private ranges, so only the proxy's own hop
is trusted:

```
--forwarded-allow-ips=172.16.0.0/12,10.0.0.0/8,192.168.0.0/16
```

If your proxy reaches the container from outside those ranges, narrow the list to
your proxy's address. Do not widen it.
:::

---

## Database

Point `DB` at Postgres. The schema builds itself on startup: `create_all` adds missing
tables, then `add_missing_columns` adds columns the models have gained, printing each
`ALTER` as it runs.

There is no migration tool and no migration step. Dropping, renaming or retyping a column
is not covered and has to be done by hand. See
[Architecture](/docs/architecture/#data) for what that trade buys.

Back the database up yourself. Nothing in the application does it for you.

---

## First run

Two bootstraps exist because a brand-new database cannot be used without them, and
neither should stay set.

```sh
# Grants admin to one account, creating it if it has never logged in.
BOOTSTRAP_ADMIN_STEAM64=76561198000000000

# Allow-lists game servers: address=region,address=region
BOOTSTRAP_SERVERS=203.0.113.10=paris,203.0.113.11=utah
```

Every `/server/v1/*` route returns `401` to an address not in `AuthorizedServers`, and
nothing in the site adds one, so without `BOOTSTRAP_SERVERS` no game server can ever
talk to your instance. Regions come from `[regions.names]` in `config.toml`. Addresses
already present are left alone, so changing a region here does not overwrite the stored
one.

Unset both once they have done their job.

---

## Connecting a game server

1. Put the compiled plugin in the server's `Rocket/Plugins/<name>/`.
2. Point its configuration at your site's base URL.
3. Add the server's public IP to `BOOTSTRAP_SERVERS`, with a region that exists in
   `[regions.names]`.
4. Restart the site once so the allow-list entry is created.

The server then polls `GET /server/v1/session` and the
[match lifecycle](/docs/match-lifecycle/) takes over.

:::note[Plugin configuration is XML on the server, not in this repository]
RocketMod writes each plugin's config file on first load. A new configuration field
is not picked up by editing the repository; it has to be added to the live XML on the
server, or the file deleted so the plugin regenerates it.
:::

---

## Multiple environments

One image serves all of them. Three environment variables override the `[app]` block in
`config.toml`:

| Variable | Overrides |
|---|---|
| `DOMAIN` | `[app].domain`, the public hostname with no scheme |
| `PROTOCOL` | `[app].protocol` |
| `BUILD` | `[app].build`, the label the site shows |

A staging environment should also leave `BOT_TOKEN`, `LOG_WEBHOOK` and
`QUEUE_LOG_CHANNEL` unset. Every one of those callers no-ops on an empty value, so
staging can never DM real players or post into production Discord channels. That is the
mechanism, not a convention, and it is worth preserving.

---

## What PeekSeek itself runs on

[Dokploy](https://dokploy.com), with Traefik in front, on a single host. Nothing in the
application depends on that, and any Docker host will do.

Two lessons from running it that are cheaper to read than to rediscover:

- **Never let two applications claim the same hostname.** Traefik will hold two routers
  with the same rule, pick one deterministically, and a redeploy can flip which one wins.
  A healthy container and a `502` at the same time usually means this.
- **A valid certificate proves nothing about your backend.** Traefik answers the ACME
  challenge itself and never touches the container, so a fresh certificate alongside a
  `502` is exactly the signature of a live router pointed at a dead service.

---

## Next

- [Configuration](/docs/configuration/), every setting in one table
- [Forking for another game](/docs/forking/)
