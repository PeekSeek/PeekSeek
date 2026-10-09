# Instructions for agents

PeekSeek is matchmaking and esports for Unturned. This repository holds the website,
the Discord bot, and the five RocketMod plugins that the game servers run.

Read this file before you change anything. It tells you the rules that the code does
not say out loud.

## Before you finish

Run these. All of them must pass.

```sh
black .                                        # from the repository root
cd website && pytest -v
cd website/tooling && npm run lint             # the JavaScript and the stylesheets
cd plugins/peekseek-match && dotnet test PeekSeek.sln
cd docs && npm run build                       # if you touched docs/
```

## Things you must not do

- Do not deploy. A merge to `main` releases plugins. It never deploys the site.
- Do not write a secret into a file. Secrets come from the environment.
- Do not touch the production database.
- Do not add a `utils`, `helpers`, `common` or `misc` file. Behaviour lives with the
  data that it works on.
- Do not add an em dash or an en dash to any file.

## Three lists that you may only add to

Each of these turns a position into a meaning. If you move an entry, data that is
already stored changes what it means. Add to the end instead.

| List | Where | What a position means |
| --- | --- | --- |
| `queueBit` | each file in `gamemodes/` | one bit in a queue mask |
| `permissions` | `config.yaml` | one bit in `Users.permissions` |
| `ranks` | `config.yaml` | sorted by `elo`, so the order is the ladder |

## Configuration

`config.yaml` in the repository root holds everything that is not a secret.
`gamemodes/` holds one file for each gamemode.

Every file in `gamemodes/` is a gamemode. The `gamemodes` list in `config.yaml` only
says which of them the play page shows, and in what order. Leave a mode out of that
list to hide it. The mode keeps its queue bit.

### Add a gamemode

1. Copy a file in `gamemodes/`. Name the file after the mode, in lower case, with a
   hyphen for each space.
2. Give it the next free `queueBit`. Take the highest one that exists, and add 1.
3. Add the file name to `gamemodes` in `config.yaml` if players may select it.

### The name of a gamemode is a wire value

The site sends `name` to the game server. The server then selects the plugin that
runs the match. `Gamemodes.Runs` in `plugins/peekseek-match` matches by prefix: a
plugin runs the mode when `name` is the plugin's name, or starts with that name and a
space.

`RAID 2v2` runs on the RAID plugin because it starts with `RAID `. If you rename it to
`Raiding 2v2`, no server can run it.

So: a rename is a deploy, not an edit. Change the plugin in the same release.

## Port the platform to another game

The site does not know what Unturned is. Six things do.

| What | Where | What to change |
| --- | --- | --- |
| Item IDs | `gamemodes/*.yaml` | your game's item IDs, with a comment for each |
| Guns | `guns` in `config.yaml` | magazine, rounds and fire mode per gun |
| Kits | `website/src/kits.json` | what a player can wear and carry |
| Maps | `map` in each gamemode file | your map names |
| Join link | `templates/app/spectate.html`, `js/admin.js`, `js/match.js` | `steam://run/304930` is Unturned's app ID |
| Plugins | `plugins/` | one plugin for each gamemode, plus the match plugin |

The match plugin is the only part that talks to the game. It owns the session, the
roster, the score and the screens. A gamemode plugin implements `IGamemode`, and the
match plugin calls it. To support another game, replace the plugins and keep the site.

`website/API.md` is the contract between the two. Keep it, and your server code can be
anything.

## Change the look

### The colours

`website/public/assets/css/default.css` opens with every colour on `:root`. Change a
token there, and the whole site follows. Never write a second hex literal.

The brand tokens are `--brand`, `--brand-light`, `--brand-strong`, `--brand-shade` and
`--brand-header`.

The landing page is the one page that does not load `default.css`. It declares its own
tokens at the top of `index.css`. Change both.

### The logo

| File | Where it is used |
| --- | --- |
| `website/public/img/cmw.png` | the wordmark, white, in the footer and the README |
| `website/public/img/logo.png` | the mark |
| `website/public/img/logo.ico` | the tab icon on a dark tab strip |
| `website/public/img/logo-dark.ico` | the tab icon on a light tab strip |
| `docs/src/assets/cmw.png` | the wordmark in the documentation site |
| `docs/public/favicon.svg` | the documentation tab icon, both schemes in one file |

The footer dims the wordmark with `filter: brightness(0.15)`. A white logo is what
that expects.

`docs/public/favicon.svg` holds both marks and swaps them with a media query inside
the file. Starlight always writes its own icon link last, so two links cannot work.
Rebuild that file if you change either `.ico`.

## How the code is written

- Explain **why**, never what. If nothing surprising happens, write no comment.
- Name a thing for what it is. No abbreviation that a reader cannot expand.
- `snake_case` for Python. `camelCase` for JSON fields, database columns, JavaScript
  and CSS classes. The split is deliberate: the wire format is frozen, and the Python
  that builds it is still Python.
- A CSS rule that two pages use goes in `default.css`. A rule that one page uses goes
  in that page's stylesheet. Move it, never copy it.
- No `<style>` block in a template. No `style="..."`, except to hand over one value
  that only the server knows.
- No inline `<script>`, except to hand data to a script that is already loaded.

## Where a file goes

```
config.yaml                main configuration
gamemodes/                 one file per gamemode
website/src/core/          models, matchmaking, settings, database
website/src/routes/        one router per area
website/src/templates/     Jinja2, mirroring the route that renders it
website/public/assets/     stylesheets and scripts
plugins/<name>/plugin/     the plugin
plugins/<name>/tests/      one test file per class
docs/                      the documentation site
```
