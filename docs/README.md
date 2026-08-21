# PeekSeek Docs

The documentation site for [PeekSeek](https://github.com/PeekSeek/PeekSeek), built with
[MkDocs Material](https://squidfunk.github.io/mkdocs-material/).

It covers running your own instance and forking the platform for another game. The
per-component READMEs and [website/API.md](../website/API.md) stay where they are; these
pages link to them rather than restating them.

## Run locally

```sh
pip install -r requirements.txt
mkdocs serve
```

Then open [localhost:8000](http://localhost:8000).

## Build

```sh
mkdocs build
```

## Deploy

Pushing to `main` with a change under `docs/` publishes to GitHub Pages, via
[.github/workflows/docs.yml](../.github/workflows/docs.yml). Unlike the website, this
deploy *is* a CI job.
