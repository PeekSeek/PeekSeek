"""Re-render public/ranks/*.svg to the PNGs Discord embeds use.

The site draws the SVGs. Discord will not render an SVG in an embed, so /stats points
its thumbnail at a PNG beside it -- which means the two can drift, and have: the badge
art has been recoloured twice without the PNGs moving with it. Run this after any change
to the art, and commit the result.

Needs ImageMagick on PATH (or IMAGEMAGICK set to the binary). Its internal renderer
handles these files' clip paths and Apollo's mask correctly; that is worth re-checking
by eye if the art ever gains anything more exotic.

    python scripts/render_rank_pngs.py
"""

import os
import shutil
import subprocess
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
RANKS = os.path.join(HERE, "..", "public", "ranks")
SIZE = "256x256"


def main() -> int:
    magick = os.environ.get("IMAGEMAGICK") or shutil.which("magick")
    if not magick:
        print("ImageMagick not found. Install it, or set IMAGEMAGICK to the binary.")
        return 1

    svgs = sorted(f for f in os.listdir(RANKS) if f.endswith(".svg"))
    if not svgs:
        print(f"No badge art in {RANKS}")
        return 1

    for svg in svgs:
        name = svg[: -len(".svg")]
        source = os.path.join(RANKS, svg)
        target = os.path.join(RANKS, f"{name}.png")
        # -strip and -depth 8 because a badge is flat colour: 16-bit and metadata
        # tripled the file size for nothing anybody can see at thumbnail size.
        result = subprocess.run(
            [
                magick,
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
        if result.returncode != 0:
            print(f"  FAILED {name}: {result.stderr.strip()}")
            return 1
        print(f"  {name}.png  {os.path.getsize(target) // 1024}KB")

    print(f"\n{len(svgs)} badges rendered at {SIZE}.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
