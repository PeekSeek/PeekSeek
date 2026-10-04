# The screen generator

Six Python files that write the in-game screens as Unity prefabs and sprites, so a layout
is a diff in a script rather than a change nobody can review inside the Editor. Nothing
here runs at build time or on a server: you run it when a screen changes, commit what it
wrote, and the Unity build turns that into the mod.

## Setup

```sh
pip install -r requirements.txt
```

Pillow is the only dependency, and `generate.py` needs an Unturned install to read the
map picture and the item icons out of. Pass its path or let the script find the default
one.

## What each file is

| File | What it does |
| --- | --- |
| `generate.py` | The entry point. Writes every prefab and sprite; run this one. |
| `unityyaml.py` | The handful of Unity objects a uGUI screen is made of, written as the YAML Unity saves. Everything else builds on it. |
| `footprints.py` | Draws a starting base from above, for RAID's board tiles. |
| `sitemap.py` | Crops a map's own satellite picture round a site, for the attack screen. |
| `raidboard.py` | RAID's two pick boards and the six class glyphs they carry. |
| `sounds.py` | Writes one effect per sound into the kill feed's bundle. Run after `sound-design/make_sounds.py ship`. |
| `WorkshopUpload/` | Publishes the finished mod to the Steam Workshop. |

## Running it

```sh
python generate.py                      # finds the default Unturned install
python generate.py "D:/Steam/.../Unturned"
```

The prefabs are Unity's own YAML, so the Editor opens them like anything else. If a screen
is ever changed inside Unity instead, that edit wins and the script is stale: bring the
change back into the script or delete it, but do not leave the two disagreeing.

## Why RAID's board lives here

`raidboard.py` and `sitemap.py` are not Match's, and sitting in Match's Unity project is
the wrong place for them on the face of it. They are here because `generate.py` imports
both as siblings, and it is one generator for every screen in the game: the mod is a
single bundle that every server loads, so there is no second project to move them to. A
copy under `plugins/peekseek-raid/` would need `generate.py` to put it on `sys.path`,
which is a worse trade than the odd neighbour.

If RAID ever gets a bundle of its own, they move with it.
