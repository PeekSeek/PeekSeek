# Contributing

Rules, not preferences. Every question below has one answer, so nobody has to hold an
opinion about it in review.

## Branches

Two permanent branches, and nothing else lives long.

| Branch | What it is |
| --- | --- |
| `main` | What is released. Plugin tags are cut from here. |
| `develop` | Integration. Sits on `main` unless something is in flight. |

Work happens on a short-lived branch off `develop`, named `<type>/<what-it-does>`:
`feat/`, `fix/`, `hotfix/`, or `chore/`. Open a PR, get it green, merge it, delete the
branch in the same click. A branch that has been merged has no reason to still exist,
and a branch that has been open long enough to fall behind `main` is a merge conflict
someone will pay for later.

## Where a file goes

```
plugins/<name>/plugin/     the plugin itself
plugins/<name>/tests/      its tests, one file per class under test
refs/                      source stand-ins for assemblies we may not redistribute
website/src/core/          domain logic: models, matchmaking, settings, db
website/src/routes/        one APIRouter per area, named after the area
website/src/templates/     Jinja2, mirroring the route that renders it
website/public/assets/css/ stylesheets
website/public/assets/js/  scripts
website/scripts/           one-off tools you run by hand, never at request time
website/tooling/           the JS and CSS linters, kept out of the runtime image
```

There is no `utils`, `helpers`, `common`, or `misc` in anything maintained here, and
adding one is not a judgement call. Behaviour lives with the data it operates on.

Two places break rules on this page, and both are named rather than quietly tolerated:

- `plugins/utelemetry/plugin/src/util` is exactly the catch-all this rule forbids. The
  component is dormant - one commit, one feature, a different author - and it is the only
  directory here that is not MIT. Removing it is the fix, not reorganising it.
- `LobbyManager.cs`, `PSManager.cs`, `Raid.cs` and `RaidWorld.cs` carry around forty
  `// ---- section ----` banner comments, which restate organisation rather than explain
  behaviour. Each banner names a class that wants extracting; they go away by splitting
  those four files, not by deleting the lines. Do not add more.

## Names

**snake_case for Python identifiers. camelCase for JSON field names.** The split is on
purpose: the wire format is a frozen contract with the deployed plugins, documented in
[website/API.md](website/API.md), and the Python that builds it is still Python.

So `match.selection_ends_at()` fills `"selectionEndsAt"`, and a renaming pass over the
former never touches the latter. Database columns are camelCase too, for the same reason:
they are the wire format at rest.

Elsewhere: `PascalCase` for classes, `UPPER_SNAKE` for module constants, `camelCase` for
JS and for CSS class names. No abbreviation a reader cannot expand - `--avatar-size`, not
`--av` - and no type tags in names.

## CSS

**A rule used by more than one page lives in `css/default.css`. A rule used by one page
lives in that page's stylesheet.** That is the whole rule.

- `default.css` loads on every page from `layout.html`. It holds the reset, the page
  shell, and the shared components (`.mainElement`, `.userProfile`, `.settingsButton`,
  the banners, the pick tiles, the match cards).
- Every other stylesheet is named after the template it serves (`play.css` for
  `app/play.html`) and is loaded from that template's `{% block addheader %}`.
- When a second page needs a rule, **move** it to `default.css`. Do not copy it.
  `esports.css` and `esports2.css` were once the same file, drifted four rules apart,
  and a fix landed on one page and not the other. That is the failure this rule exists
  to prevent.

Reach for a token, not a hex literal. `default.css` opens with the whole palette on
`:root`, named for the job each colour does; add a token when a second rule needs a
colour, and never a second literal. There were 294 literals in here once, which is how
`#ff4444` and `#f04444` came to sit side by side doing the same thing.

No `<style>` blocks in templates. No `style="..."` attributes, with exactly one
exception: a value only the server can know, handed over as a custom property and
consumed by a stylesheet rule.

```html
<i style="--held:{{ you.bar.held }}%"></i>
```

A percentage worked out from a match result cannot live in a stylesheet, so pretending
otherwise would just mean the rule gets broken quietly. Everything else - the widths, the
colours, the animation - stays in CSS. Anything that is not a bare custom property still
needs a class.

## JavaScript

- One file per area, named after it, in `css`-adjacent `js/`. `camelCase` filenames.
- No inline `<script>` except to hand server data to a script that is already loaded, or
  to mount a third-party embed. A handoff is data and nothing else:

  ```html
  <script id="eloHistory" type="application/json">{{ eloHistory | tojson }}</script>
  <script src="/static/assets/js/profile.js" defer></script>
  ```

  Configuration is not data. A chart definition, an event handler or a branch belongs in
  the `.js` file, however short it looks sitting in the template.
- A script loaded from a CDN is pinned to an exact version and carries an `integrity`
  hash. These pages render session state; a floating major tag lets a changed build run
  with full privileges on them.
- A script that both esports pages need is its own file (`localTime.js`), not a copy in
  each template.

## Comments

Explain **why**, in full sentences, directly above the thing they explain. Never restate
what the code already says. If nothing non-obvious is going on, write no comment.

```python
"""Endpoints the Unturned game servers call. Auth is by allow-listed server IP.

These paths are a frozen contract with the deployed C# plugin (RemoteAPI.cs), so
rename them only alongside a plugin release.
"""
```

`///` in C#, `/* */` or `//` in JS, docstrings in Python.

## Commits and PRs

Imperative summary line. The body explains why: the bug, the incident, or the constraint
that made the change necessary, not a changelog of what moved where. No self-attribution
trailers.

## Before you open the PR

```sh
# Python, from the repository root: the bot, the sound generator and the Unity build
# tools are all linted too, which is what pyproject.toml's excludes are for.
black .

cd website && pytest -v
cd website/tooling && npm install && npm run lint     # the JS and the stylesheets
cd plugins/peekseek && dotnet format && dotnet test PeekSeek.sln
```

CI runs the same checks per component, path-filtered, so a website change never runs the
C# jobs. The exception is `bot.yml`, which is filtered on `website/src/core/**` as well
as `bot/**`, because the bot imports the site's own moderation code and a refactor there
can break it with every website job green.

Dependencies are pinned. `website/requirements.txt` is the list a human edits;
`requirements.lock` is what anything installs, hashes and all. Change one and regenerate
both locks with the commands in the header of that file.

## Production assets

`website/minify.py` minifies `public/assets` in place, which is only ever right inside an
image build. The repo always holds readable source; only the image holds compressed
files, and nothing in a template needs to know which of the two it is running against.

In place means destructive, so it needs `--in-place` to write anything. The Dockerfile
passes it. Run it bare and it tells you what it would save and touches nothing.
