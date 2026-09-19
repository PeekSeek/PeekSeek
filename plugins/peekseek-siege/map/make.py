"""Make the Alpha Valley Siege map: Alpha Valley, as a Survival map, under another name.

    python make.py [<Unturned install>]

Writes Build/AlphaValleySiege/, the folder to upload to the Workshop as a map. The
install defaults to Steam's usual place; the map is never committed, because it is
forty megabytes of Nelson's files and this script is the whole difference.

Why this exists. Unturned draws its arena text and compass on every map whose Level.dat
says Arena, and it reads that off the client's own copy of the map, so no server
setting and no plugin can switch them off on Alpha Valley itself. The client also picks
a map by name from its own Maps folder before any workshop item, so the copy has to
carry a name of its own. One byte in Level.dat is the map type; the rest is Alpha Valley
untouched, plus a config line that lets a base go down near the map's player spawns,
which only Arena maps otherwise allow.
"""

import shutil
import sys
from pathlib import Path

NAME = "Alpha Valley Siege"
DEFAULT_INSTALL = Path(r"C:\Program Files (x86)\Steam\steamapps\common\Unturned")
SURVIVAL = 0

DESCRIPTION = (
    "Alpha Valley as a Survival-type map, for PeekSeek Siege. Nothing about the ground is "
    "different; the type is, so the game draws no arena text and no compass over the "
    "match HUD. Servers list this item so it downloads on join."
)


def make(install):
    source = install / "Maps" / "Alpha Valley"
    if not (source / "Level.dat").exists():
        raise SystemExit(f"No Alpha Valley at {source}. Pass the Unturned install folder.")

    out = Path(__file__).resolve().parent / "Build" / "AlphaValleySiege"
    target = out / NAME
    if out.exists():
        shutil.rmtree(out)
    shutil.copytree(source, target)

    # Level.dat: version, the owner's Steam id, the size, then the type. Version 2 and
    # up carries the type as the eleventh byte; Alpha Valley is version 2.
    level = bytearray((target / "Level.dat").read_bytes())
    if level[0] < 2 or len(level) < 11:
        raise SystemExit("Level.dat is not the layout this script knows.")
    level[10] = SURVIVAL
    (target / "Level.dat").write_bytes(bytes(level))

    config = target / "Config.json"
    text = config.read_text(encoding="utf-8").rstrip().rstrip("}").rstrip()
    text += ',\n\t"Prevent_Building_Near_Spawnpoint_Radius": 0\n}\n'
    config.write_text(text, encoding="utf-8")

    (target / "English.dat").write_text(f"Name {NAME}\nDescription {DESCRIPTION}\n", encoding="utf-8")

    # The marker the game reads to learn what kind of content a workshop item is.
    (out / "Map.meta").write_bytes(b"\x00")

    print(f"wrote {out}")


if __name__ == "__main__":
    make(Path(sys.argv[1]) if len(sys.argv) > 1 else DEFAULT_INSTALL)
