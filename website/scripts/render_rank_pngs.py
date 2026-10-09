"""Re-render public/ranks/*.svg to the PNGs Discord embeds use.

The site draws the SVGs. Discord will not render an SVG in an embed, so /stats points
its thumbnail at a PNG beside it -- which means the two can drift, and have: the badge
art has been recoloured twice without the PNGs moving with it. Run this after any change
to the art, and commit the result.

Both are tracked on purpose. `/static/ranks/<icon>.png` is a documented URL that game
servers fetch to draw a badge over a player's head, so generating the PNGs only inside
the Docker image would serve a 404 to anyone running the app with plain uvicorn.

Needs ImageMagick on PATH (or IMAGEMAGICK set to the binary). Its internal renderer
handles these files' clip paths and Apollo's mask correctly; that is worth re-checking
by eye if the art ever gains anything more exotic.

    python scripts/render_rank_pngs.py           # render, in place
    python scripts/render_rank_pngs.py --check   # is anything stale? Writes nothing.

--check is what CI runs. It compares pixels rather than bytes, because two ImageMagick
versions do not produce byte-identical PNGs from the same SVG and a toolchain upgrade
should not read as drift in the art.
"""

import argparse
import os
import shutil
import subprocess
import sys
import tempfile

HERE = os.path.dirname(os.path.abspath(__file__))
RANKS = os.path.join(HERE, "..", "public", "ranks")
SIZE = "256x256"

# Pixels allowed to differ out of 256x256 (65,536) before a badge counts as stale. A
# recolour moves thousands; an encoder or renderer difference moves a handful of edge
# pixels at most.
TOLERANCE = 200


def render(convert: list[str], source: str, target: str) -> str | None:
    """Render one SVG. Returns an error message, or None on success."""
    # -strip and -depth 8 because a badge is flat colour: 16-bit and metadata
    # tripled the file size for nothing anybody can see at thumbnail size.
    result = subprocess.run(
        [
            *convert,
            "-background",
            "none",
            source,
            "-resize",
            SIZE,
            "-depth",
            "8",
            "-strip",
            target,
        ],
        capture_output=True,
        text=True,
    )
    return result.stderr.strip() or None if result.returncode != 0 else None


def differing_pixels(compare: list[str], a: str, b: str) -> int | None:
    """How many pixels differ between two PNGs, or None if they cannot be compared."""
    result = subprocess.run(
        [*compare, "-metric", "AE", a, b, "null:"],
        capture_output=True,
        text=True,
    )
    # `compare` exits 1 when the images differ, which is not an error here. It exits 2
    # when it could not do the comparison at all.
    if result.returncode > 1:
        return None
    try:
        return int(float(result.stderr.strip().split()[0]))
    except (ValueError, IndexError):
        return None


def main() -> int:
    options = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    options.add_argument(
        "--check",
        action="store_true",
        help="report stale PNGs instead of rewriting them. Exit 1 if any are.",
    )
    checking = options.parse_args().check

    # Version 7 only. Version 6 draws these clip paths and Apollo's mask differently,
    # so it reports every badge as drift when the art has not moved.
    magick = os.environ.get("IMAGEMAGICK") or shutil.which("magick")
    if not magick:
        print("ImageMagick 7 not found. Install it, or set IMAGEMAGICK to the binary.")
        return 1
    convert, compare = [magick], [magick, "compare"]

    svgs = sorted(f for f in os.listdir(RANKS) if f.endswith(".svg"))
    if not svgs:
        print(f"No badge art in {RANKS}")
        return 1

    stale = []
    with tempfile.TemporaryDirectory() as scratch:
        for svg in svgs:
            name = svg[: -len(".svg")]
            source = os.path.join(RANKS, svg)
            committed = os.path.join(RANKS, f"{name}.png")
            target = os.path.join(scratch, f"{name}.png") if checking else committed

            error = render(convert, source, target)
            if error:
                print(f"  FAILED {name}: {error}")
                return 1

            if not checking:
                print(f"  {name}.png  {os.path.getsize(target) // 1024}KB")
                continue

            if not os.path.exists(committed):
                stale.append((name, "no PNG committed"))
                continue
            moved = differing_pixels(compare, committed, target)
            if moved is None:
                stale.append((name, "could not be compared"))
            elif moved > TOLERANCE:
                stale.append((name, f"{moved:,} pixels differ"))
            else:
                print(f"  {name}.png  ok ({moved} pixels)")

    if not checking:
        print(f"\n{len(svgs)} badges rendered at {SIZE}.")
        return 0

    if stale:
        print(
            f"\n{len(stale)} badge PNG(s) no longer match their SVG:", file=sys.stderr
        )
        for name, why in stale:
            print(f"  {name}: {why}", file=sys.stderr)
        print(
            "\nRun `python scripts/render_rank_pngs.py` and commit the result.",
            file=sys.stderr,
        )
        return 1

    print(f"\n{len(svgs)} badges match their art.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
