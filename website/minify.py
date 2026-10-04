"""Minify public/assets. Rewrites the files in place, which is only ever right inside
an image build.

The Dockerfile runs this after COPY public, so the repo keeps readable stylesheets
and the deployed image serves compressed ones. It rewrites the files rather than
emitting .min.css alongside them, so no template has to know which build it is in
and there is no second set of paths to keep in step.

In place means destructive: run bare in a checkout and every stylesheet and script in
the working tree is replaced by its minified form, and a commit made before noticing
loses the readable source. So the in-place pass needs --in-place, which the Dockerfile
passes and a curious contributor will not. Without it the files are read, compressed
and reported on, and nothing is written.

Idempotent: running it twice costs a little time and changes nothing.
"""

import argparse
import sys
from pathlib import Path

import rjsmin
from csscompressor import compress

ASSETS = Path(__file__).resolve().parent / "public" / "assets"


def minified(path: Path) -> str:
    if path.suffix == ".css":
        return compress(path.read_text("utf-8"))
    return rjsmin.jsmin(path.read_text("utf-8"))


def main(argv: list[str] | None = None) -> int:
    options = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    options.add_argument(
        "--in-place",
        action="store_true",
        help="rewrite the files. Without it, report the saving and write nothing.",
    )
    written = options.parse_args(argv).in_place

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
        compressed = minified(path)
        before += path.stat().st_size
        after += len(compressed.encode("utf-8"))
        if written:
            path.write_text(compressed, encoding="utf-8")

    saved = before - after
    how = "rewrote" if written else "would rewrite (--in-place to do it)"
    print(
        f"minify: {how} {len(targets)} files, {before:,} -> {after:,} bytes ({saved:,} saved)"
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
