"""Minify public/assets in place. Run at image build time, never in dev.

The Dockerfile runs this after COPY public, so the repo keeps readable stylesheets
and the deployed image serves compressed ones. It rewrites the files rather than
emitting .min.css alongside them, so no template has to know which build it is in
and there is no second set of paths to keep in step.

Idempotent: running it twice costs a little time and changes nothing.
"""

import sys
from pathlib import Path

import rjsmin
from csscompressor import compress

ASSETS = Path(__file__).resolve().parent / "public" / "assets"


def minified(path: Path) -> str:
    if path.suffix == ".css":
        return compress(path.read_text("utf-8"))
    return rjsmin.jsmin(path.read_text("utf-8"))


def main() -> int:
    # htmx ships minified; running a second pass over a vendored library is pure risk
    # for no gain, so anything already named .min is left exactly as it was shipped.
    targets = [
        path
        for path in sorted(ASSETS.rglob("*"))
        if path.suffix in {".css", ".js"} and not path.name.endswith(".min.js")
    ]

    if not targets:
        print(f"minify: nothing to do under {ASSETS}", file=sys.stderr)
        return 1

    before = after = 0
    for path in targets:
        original = path.stat().st_size
        path.write_text(minified(path), encoding="utf-8")
        before += original
        after += path.stat().st_size

    saved = before - after
    print(
        f"minify: {len(targets)} files, {before:,} -> {after:,} bytes ({saved:,} saved)"
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
