# PeekSeek Docs

The documentation site for [PeekSeek](https://github.com/PeekSeek/PeekSeek), built with
[Starlight](https://starlight.astro.build). It is served by the website itself at
[peekseek.net/docs](https://peekseek.net/docs), not from a separate host.

It covers running your own instance and forking the platform for another game. The
per-component READMEs stay where they are and these pages link to them rather than
restating them.

## Two generated inputs

Both are produced at build time and are gitignored, because a stale committed copy of
either is worse than none:

| File | Comes from | Edit instead |
|---|---|---|
| `openapi.json` | `website/scripts/openapi.py`, which dumps the app's own schema and keeps only `/server/v1/*` | the routes in `website/src/routes/gameserver.py` |
| `src/content/docs/reference/api.md` | `scripts/import-api-contract.mjs`, which imports the hand-written contract | [`website/API.md`](../website/API.md) |

The route reference pages under `/docs/reference/server-api/` are generated from the
schema by [starlight-openapi](https://starlight-openapi.vercel.app), so route detail
cannot drift from the code. `website/API.md` carries the prose no generated page can:
the conventions, the auth model, and why matchmaking runs on the server's poll.

## Run locally

```sh
npm install
npm run schema   # needs the website's Python dependencies
npm start
```

Then open [localhost:4321/docs](http://localhost:4321/docs). `npm run schema` only has
to be re-run when the game-server routes change.

## Build

```sh
npm run build
```

Output is `dist/`. A broken internal link fails the build, the same as MkDocs `--strict`
did before this site moved to Starlight.

## Deploy

The built site is copied into the website image by
[website/Dockerfile](../website/Dockerfile) and mounted at `/docs` by the app, so docs
ship with the website and not on their own. Pushing to `main` runs
[.github/workflows/docs.yml](../.github/workflows/docs.yml), which builds the site as a
check only. Unlike the old MkDocs setup, this deploy is **not** a CI job: a docs change
goes live with the next deploy of the site.

## Theme

[src/styles/peekseek.css](src/styles/peekseek.css) maps the site's four colours onto
Starlight's own tokens: `#0a0a0a` page, `#121212` raised surfaces, `#ffffff` text,
`#ff0000` accent. Nothing outside those four is introduced, and the light theme is
removed rather than left offering a choice the rest of the project does not have.
