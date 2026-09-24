"""The ground round a site, seen from above, for the attack screen's map.

Unturned's `Map.png` is a map's own satellite picture, the one the GPS shows: north up,
world x left to right, world z bottom to top, and the whole level across it, border
included. Alpha Valley is a Small map, 1024 metres and 1024 pixels, so a world point
(x, z) is the pixel (x + 512, 512 - z); the size is read off Level.dat rather than
assumed, in case that ever changes. The picture is Nelson's and is never committed as
source. It is read out of the Unturned install, the crops are what is committed, and a
machine without the game keeps the crops it has.
"""

from io import BytesIO
from pathlib import Path

from PIL import Image

LEVEL_SIZES = (512, 1024, 2048, 4096, 8192)      # ELevelSize: TINY, SMALL, MEDIUM, LARGE, INSANE


def picture(map_folder, anchor_x, anchor_z, metres, pixels):
    """`metres` of ground square round the anchor, as a `pixels` square PNG, or None
    when the map is not on this machine."""
    folder = Path(map_folder)
    source = folder / "Map.png"
    level = folder / "Level.dat"
    if not source.exists() or not level.exists():
        return None

    # Level.dat: a version byte, the owner's Steam id, then the size as an ELevelSize.
    size = LEVEL_SIZES[level.read_bytes()[9]]

    whole = Image.open(source).convert("RGB")
    scale = whole.width / size
    half = metres / 2
    box = (
        (anchor_x + size / 2 - half) * scale,
        (size / 2 - anchor_z - half) * scale,
        (anchor_x + size / 2 + half) * scale,
        (size / 2 - anchor_z + half) * scale,
    )
    crop = whole.crop(tuple(int(round(edge)) for edge in box)).resize((pixels, pixels), Image.LANCZOS)

    out = BytesIO()
    crop.save(out, "PNG", optimize=True)
    return out.getvalue()
