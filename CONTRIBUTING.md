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
website/src/core/          domain logic: models, matchmaking, settings, db
website/src/routes/        one APIRouter per area, named after the area
website/src/templates/     Jinja2, mirroring the route that renders it
website/public/assets/css/ stylesheets
website/public/assets/js/  scripts
```

There is no `utils`, `helpers`, `common`, or `misc` anywhere in this repo, and adding
one is not a judgement call. Behaviour lives with the data it operates on.

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

No `<style>` blocks in templates. No `style="..."` attributes. If markup needs a style,
it needs a class, and the class goes in one of the files above.

## JavaScript

- One file per area, named after it, in `css`-adjacent `js/`. `camelCase` filenames.
- No inline `<script>` except to hand server data to a script that is already loaded
  (`let data = {{ data | safe }}`) or to mount a third-party embed.
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
cd website && black . && pytest -v
cd plugins/peekseek && dotnet format && dotnet test PeekSeek.sln
```

CI runs the same checks per component, path-filtered, so a website change never runs the
C# jobs.

## Production assets

`website/minify.py` runs at Docker build time and minifies `public/assets` in place. The
repo always holds readable source; only the image holds compressed files. Nothing in a
template needs to know which of the two it is running against.
