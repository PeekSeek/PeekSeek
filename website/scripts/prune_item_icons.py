"""Work out which of public/unturned's item icons are reachable, and optionally delete the
rest.

There are 1,830 PNGs in there and about 118 are reachable, so roughly 54 MB of the
repository is item art for items nothing can ask for. Deleting them is safe, but only
after one check that cannot be done from the repository alone.

Five templates render `/static/unturned/<id>.png`, and every id they can be handed comes
out of `kits.json` or a gamemode file, or out of a kit or outfit a player has saved. The
saved ones live in the database as item ids: the editors only ever offer ids from
`kits.json`, but that has not always been enforced for every slot, so a stored kit may
name an id the configuration no longer does. Delete on the configuration alone and that
player's kit renders as a broken image.

So:

    # Reads only the config. Prints what it would keep and what it would delete.
    python scripts/prune_item_icons.py

    # The real answer: unions the ids actually stored in the database.
    DB=postgresql://... python scripts/prune_item_icons.py --with-database

    # Delete, having looked at the above.
    DB=postgresql://... python scripts/prune_item_icons.py --with-database --delete

--delete refuses to run without --with-database, because the configuration on its own is
not the keep set.
"""

import argparse
import json

import yaml
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
ICONS = ROOT / "public" / "unturned"


def integers(value) -> set[int]:
    """Every integer anywhere in a nested JSON value, key or leaf.

    Deliberately blunt. An item id can be a key, a bare number, a string, or the first
    element of a `[itemId, amount, state]` tuple, and over-keeping an icon costs 30 KB
    while under-keeping one shows a player a broken image.
    """
    found = set()
    if isinstance(value, dict):
        for key, inner in value.items():
            found |= integers(key) | integers(inner)
    elif isinstance(value, (list, tuple)):
        for inner in value:
            found |= integers(inner)
    elif isinstance(value, bool):
        pass
    elif isinstance(value, int):
        found.add(value)
    elif isinstance(value, str) and value.lstrip("-").isdigit():
        found.add(int(value))
    return found


def from_config() -> set[int]:
    """Item ids named by the kit catalogue and by every gamemode."""
    ids = integers(json.loads((ROOT / "src" / "kits.json").read_text("utf-8")))
    for gamemode in sorted((ROOT.parent / "gamemodes").glob("*.yaml")):
        ids |= integers(yaml.safe_load(gamemode.read_text("utf-8")))
    return ids


def from_database() -> set[int]:
    """Item ids in kits and outfits players have actually saved."""
    sys.path.insert(0, str(ROOT))
    from src.core import models as data
    from src.core.db import session

    ids: set[int] = set()
    with session() as db:
        for kit in db.query(data.Kit).all():
            ids |= integers(kit.data)
    return ids


def main() -> int:
    options = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    options.add_argument(
        "--with-database",
        action="store_true",
        help="also read the item ids stored in saved kits and outfits. Needs DB.",
    )
    options.add_argument(
        "--delete", action="store_true", help="actually remove the unreachable icons"
    )
    args = options.parse_args()

    if args.delete and not args.with_database:
        options.error(
            "--delete needs --with-database. The configuration on its own is not the "
            "keep set: a kit saved before a slot was constrained can name an id "
            "kits.json no longer does, and deleting on config alone breaks that kit."
        )

    keep = from_config()
    print(f"{len(keep):5} ids reachable from kits.json and the gamemodes")
    if args.with_database:
        stored = from_database()
        print(f"{len(stored):5} ids in saved kits and outfits")
        print(f"{len(stored - keep):5} of those the configuration does not name")
        keep |= stored

    on_disk = {int(p.stem): p for p in ICONS.glob("*.png") if p.stem.isdigit()}
    print(f"{len(on_disk):5} icons on disk")

    wanted = keep & on_disk.keys()
    missing = sorted(keep - on_disk.keys())
    dead = sorted(on_disk.keys() - keep)

    print(f"{len(wanted):5} reachable and present")
    print(f"{len(dead):5} unreachable")
    if missing:
        # These are the real bugs in the other direction: a tile that is already broken.
        print(f"{len(missing):5} reachable but MISSING: {missing[:20]}")

    if not args.delete:
        print("\nnothing deleted. Pass --delete once the numbers above look right.")
        return 0

    for item in dead:
        on_disk[item].unlink()
    print(
        f"\ndeleted {len(dead)} icons. `git status` will show them; commit when happy."
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
