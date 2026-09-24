"""Write the screens PeekSeek draws in game, as Unity prefabs and sprites.

    python generate.py [<Unturned install>]

Overwrites every Effect.prefab under Assets/PeekSeek/Bundles/Effects and the kill
feed's, and Assets/PeekSeek/Sprites/*. If a screen is ever changed inside Unity
instead, that edit wins and this file is stale; bring the change back here or delete
this script, but do not leave the two disagreeing.

The attack screen's maps are cropped from Alpha Valley's own Map.png in the Unturned
install, Steam's usual place unless one is given. Without the game the crops already
in Sprites/ are kept.

Two screens, two design languages, on purpose. The boards are something you click, so
they are built like a page of the site: the same panel, the same selectable tile, the
same px. The match HUD is composited over play like the stream overlay, so it is built
like that: one flat fill, greyscale, red only for jeopardy. Sizes are the site's px and
the overlay's vw at 1920, which is what the game draws at UI scale 1.

The font is Inter, because the site asks for the system UI font and Inter is the open
one that reads the same; the game leaves a font that is not TextMeshPro's own alone.
"""

import sys
from pathlib import Path

from PIL import Image

import footprints
import sitemap
from unityyaml import (CENTER, LEFT, RIGHT, TOP, Node, aspect_fit, bottom_center, bottom_left, button, canvas,
                       castle_alpha, centered, disc_alpha, fit_width, ignore_layout, image, layout_size, mask, prefab,
                       ring_alpha, rounded_alpha, row, silhouette, skull_alpha, sprite, sprite_file, stretch, swords_alpha,
                       text, top_center, top_left, top_right, top_stretch)

ASSETS = Path(__file__).resolve().parent.parent / "Assets"
UNTURNED = Path(sys.argv[1]) if len(sys.argv) > 1 else Path(r"C:\Program Files (x86)\Steam\steamapps\common\Unturned")
ROOT = ASSETS / "PeekSeek"
SPRITES = ROOT / "Sprites"
EFFECTS = ROOT / "Bundles/Effects"
KILLFEED_EFFECTS = ASSETS / "PeekSeekKillFeed/Bundles/Effects"

# ---- the site, for the boards (website/public/assets/default.css) -------------------
PAGE = "#0a0a0a"        # .mainElement
SUB = "#1a1a1a"         # .qgrid
SUB_HOVER = "#242424"
SELECTED = "#333333"    # #3334 over the sub-panel
TEXT = "#ffffff"
SECONDARY = "#aaaaaa"
SELECTED_LABEL = "#aae0ff"
CARD = "#161616"        # .powerupbutton, the match page's pick tile
CARD_LINE = "#262626"
CARD_HOVER = "#3a3a3a"
CARD_RED = "#ff2d2d"    # the match page's selected border
CARD_RED_FILL = "#3b1a1a"  # rgba(255, 45, 45, .16) over the card
PANEL_RADIUS = 8        # .mainElement border-radius
PANEL_PAD = 20          # .mainElement padding
GAP = 10                # the gap inside chips and between tiles

# ---- the overlay, for the HUD (website/public/assets/css/overlay.css at 1920) --------
FLAT = "#0f0f0f"        # .flat
INK = "#f2f2f2"
MUTED = "#8a8a8a"
RED = "#ff4f4f"         # the side attacking, and jeopardy
BLUE = "#4f9fff"        # the side holding


# Every sprite a screen can point at, by the name the code uses. Filled by
# write_sprites before any screen is built.
SPR = {}


def write_sprites():
    SPRITES.mkdir(parents=True, exist_ok=True)

    # The maps first, before the folder is cleared: they come from the game's files,
    # and a machine without the game keeps the ones it has.
    maps = {}
    for site, (name, (anchor_x, anchor_z), _) in enumerate(SITES):
        guid = MAP_GUIDS[site]
        picture = sitemap.picture(UNTURNED / "Maps" / "Alpha Valley", anchor_x, anchor_z, MAP_METRES, 2 * MAP_PX)
        kept = (SPRITES / f"Map{name}.png").read_bytes() if picture is None and (SPRITES / f"Map{name}.png").exists() else None
        if picture is None and kept is None:
            raise SystemExit(f"No Alpha Valley Map.png under {UNTURNED} and no Sprites/Map{name}.png to keep.")
        if picture is None:
            print(f"no Unturned install at {UNTURNED}; keeping Sprites/Map{name}.png")
        maps[f"map{site}"] = (name, guid, picture or kept)

    for stale in SPRITES.glob("*"):
        stale.unlink()

    for key, (name, guid, png_bytes) in maps.items():
        SPR[key] = sprite_file(SPRITES, f"Map{name}", guid, png_bytes)

    SPR.update({
        "disc": sprite(SPRITES, "Disc", "a4c3e5f6b7d8e9f0a1b2c3d4e5f6a7b8", 64, 64, disc_alpha(64), 0),
        # The keep on the map, drawn at twice the size it is shown at like the footprints.
        "keep": sprite(SPRITES, "Keep", "b5d4f6a7c8e9f0a1b2c3d4e5f6a7b8c9", 64, 64, castle_alpha(64), 0),
        "round8": sprite(SPRITES, "Round8", "b1f0c2d3e4a5b6c7d8e9f0a1b2c3d4e5", 32, 32, rounded_alpha(32, 32, 8), 8),
        "round6": sprite(SPRITES, "Round6", "c2a1d3e4f5b6c7d8e9f0a1b2c3d4e5f6", 24, 24, rounded_alpha(24, 24, 6), 6),
        "round3": sprite(SPRITES, "Round3", "d4b2e6f7a8c9d0e1f2a3b4c5d6e7f8a9", 12, 12, rounded_alpha(12, 12, 3), 3),
        "ring6": sprite(SPRITES, "Ring6", "e5c3f7a8b9d0e1f2a3b4c5d6e7f8a9b0", 24, 24, ring_alpha(24, 24, 6), 6),
        # The PeekSeek mark, the site's own white-on-nothing cmw.png, at twice the size it
        # is shown at.
        "logo": sprite_file(SPRITES, "Logo", "f6d4a8b9c0e1f2a3b4c5d6e7f8a9b0c1", logo_png(2 * LOGO_H)),
        "castle": sprite(SPRITES, "Castle", "c8a7d9e0f1b2c3d4e5f6a7b8c9d0e1f2", 24, 24, castle_alpha(24), 0),
        "swords": sprite(SPRITES, "Swords", "d9b8e0f1a2c3d4e5f6a7b8c9d0e1f2a3", 24, 24, swords_alpha(24), 0),
        "skull": sprite(SPRITES, "Skull", "c6e5a7b8d9f0a1b2c3d4e5f6a7b8c9d0", 40, 40, skull_alpha(40), 0),
        # Drawn at twice the size they are shown at, so they stay sharp at UI scale 2.
        "square": sprite_file(SPRITES, "FootprintSquare", "e0c9f1a2b3d4e5f6a7b8c9d0e1f2a3b4",
                              footprints.picture("Square", 2 * FIGURE_W, 2 * FIGURE_H, FIGURE_METRES)),
        "hexagonal": sprite_file(SPRITES, "FootprintHexagonal", "f1d0a2b3c4e5f6a7b8c9d0e1f2a3b4c5",
                                 footprints.picture("Hexagonal", 2 * FIGURE_W, 2 * FIGURE_H, FIGURE_METRES)),
    })


LOGO_H = 28             # the mark on the lobby's block
LOGO = ROOT.parents[4] / "website/public/img/cmw.png"


def logo_png(height):
    """The site's logo, cropped to its ink and scaled to `height`, as PNG bytes."""
    from io import BytesIO
    from PIL import Image
    mark = Image.open(LOGO).convert("RGBA")
    mark = mark.crop(mark.getbbox())
    mark = mark.resize((round(mark.width * height / mark.height), height), Image.LANCZOS)
    out = BytesIO()
    mark.save(out, "PNG", optimize=True)
    return out.getvalue()


# ---- the boards ----------------------------------------------------------------------
#
# One prefab, three panels, one shown at a time. `Pick` is the five-tile board a
# round's options go on, with each tile's picture fetched by URL. `Shape` is the
# two-tile board the footprint is chosen on before anybody builds, with the whole
# starting base drawn on each tile; those pictures are baked in, because there are two
# and they never change. `Swap` is the six-tile board a builder changes a piece's
# material on: the same piece in every other material, and a tile to take it down.
# The plugin addresses a panel by its prefix: `Pick_Headline`, `Pick2_Name`,
# `Shape_Timer`, `Swap0_Taken`, and the tiles themselves are the buttons, `BoardPick2`,
# `BoardShape0` and `BoardSwap3`, which is the name that reaches the server when one is
# clicked.

PICKS = 5
SHAPES = 2
SWAPS = 6
HEADLINE_H = 28
ITEMS = 4               # rows on a powerup tile; an option has one or two

FIGURE_W, FIGURE_H = 260, 240
FIGURE_METRES = 26      # of ground across the picture: the hexagonal base is 23 wide


def panel(face, tiles, tile_w, tile_h, headline, make_tile):
    """A `.mainElement` with a headline, a timer and `tiles` tiles in a row. Nothing
    else: what a tile is for is on the tile, and a line saying so under it was the
    one thing on the board nobody read."""
    board_w = PANEL_PAD * 2 + tiles * tile_w + (tiles - 1) * GAP
    tiles_y = PANEL_PAD + HEADLINE_H + 16
    board_h = tiles_y + tile_h + PANEL_PAD

    board = centered(face, 0, 40, board_w, board_h, active=False)
    image(board, PAGE, 0.94, sprite=SPR["round8"])

    text(board.add(top_left(f"{face}_Headline", PANEL_PAD, PANEL_PAD, board_w - PANEL_PAD * 2 - 130, HEADLINE_H)),
         headline, size=20, colour=TEXT, bold=True)
    text(board.add(top_right(f"{face}_Timer", PANEL_PAD, 16, 120, 36)),
         "20", size=28, colour=TEXT, halign=RIGHT, bold=True)

    for i in range(tiles):
        make_tile(board, face, i, PANEL_PAD + i * (tile_w + GAP), tiles_y, tile_w, tile_h)

    return board


def tile(parent, face, i, x, y, w, h, fill_tile):
    """One selectable tile: the site's `.qgridoption`, and the button the plugin hears
    as `Board{face}{i}`."""
    node = parent.add(top_left(f"Board{face}{i}", x, y, w, h))
    button(node, image(node, "#ffffff", sprite=SPR["round8"], raycast=True), SUB, SUB_HOVER, SELECTED)

    # First child, so it paints under everything else on the tile: the site's
    # selected state is a fill and a label, not a border.
    taken = node.add(stretch(f"{face}{i}_Taken", active=False))
    image(taken, SELECTED, sprite=SPR["round8"])
    text(taken.add(top_right("Label", GAP, 8, 80, 16)), "Selected",
         size=12, colour=SELECTED_LABEL, halign=RIGHT, bold=True)

    text(node.add(top_left(f"{face}{i}_Number", GAP, 8, 40, 16)), str(i + 1),
         size=13, colour=SECONDARY, bold=True)

    fill_tile(node, f"{face}{i}", i)
    return node


POWERUP_W, POWERUP_H = 160, 172
BLURB_Y, BLURB_H = 32, 42


def powerup_tile(parent, face, i, x, y, w=POWERUP_W, h=POWERUP_H):
    """A powerup: the site's selectable tile (`.qgridoption`, no outline), the name,
    one line saying what it does, then a row per item, `2x` and the item's picture.
    Selected is the same fill and label every other tile uses."""
    node = parent.add(top_left(f"Board{face}{i}", x, y, w, h))
    button(node, image(node, "#ffffff", sprite=SPR["round8"], raycast=True), SUB, SUB_HOVER, SELECTED)

    taken = node.add(stretch(f"{face}{i}_Taken", active=False))
    image(taken, SELECTED, sprite=SPR["round8"])
    text(taken.add(top_right("Label", GAP, 8, 80, 16)), "Selected",
         size=12, colour=SELECTED_LABEL, halign=RIGHT, bold=True)

    text(node.add(top_left(f"{face}{i}_Number", 10, 10, 20, 18)), str(i + 1), size=13, colour=SECONDARY, bold=True)
    text(node.add(top_left(f"{face}{i}_Name", 28, 10, w - 38, 18)), "Demolition", size=13, colour=TEXT, bold=True)
    # The one line that says what the pile of pictures under it adds up to.
    text(node.add(top_stretch(f"{face}{i}_Blurb", BLURB_Y, BLURB_H, 10)), "",
         size=11, colour=SECONDARY, valign=TOP, wrap=True)

    for k in range(ITEMS):
        row = node.add(top_left(f"{face}{i}_Item{k}", 0, BLURB_Y + BLURB_H + 4 + k * 22, w, 22, active=False))
        text(row.add(top_left(f"{face}{i}_Item{k}_Count", w / 2 - 44, 0, 40, 22)), "3x",
             size=13, colour=SECONDARY, halign=RIGHT, bold=True)
        image(row.add(top_left(f"{face}{i}_Item{k}_Icon", w / 2 + 2, 1, 40, 20)), "#ffffff", enabled=False, preserve_aspect=True)

    return node


def option_tile(parent, face, i, x, y, w, h):
    return tile(parent, face, i, x, y, w, h, pick_tile)


def shape_board_tile(parent, face, i, x, y, w, h):
    return tile(parent, face, i, x, y, w, h, shape_tile)


def pick_tile(tile, name, _):
    """A round's option: the item's picture from the site, its name, one line."""
    image(tile.add(top_center(f"{name}_Icon", 0, 26, 96, 84)), "#ffffff", enabled=False, preserve_aspect=True)
    text(tile.add(top_stretch(f"{name}_Name", 118, 22, 12)), "Rocket Launcher",
         size=16, colour=TEXT, halign=CENTER, bold=True)
    text(tile.add(top_stretch(f"{name}_Blurb", 144, 58, 14)),
         "Two rockets. Opens brick, and nothing else you carry will.",
         size=13, colour=SECONDARY, halign=CENTER, valign=TOP, wrap=True)


def shape_tile(tile, name, i):
    """A footprint: the whole starting base, drawn, then its name and one line. Tile
    order is the plugin's BaseShape order, Square then Hexagonal."""
    figure = ("square", "hexagonal")[i]
    image(tile.add(top_center(f"{name}_Figure", 0, 26, FIGURE_W, FIGURE_H)),
          "#ffffff", sprite=SPR[figure], sliced=False, preserve_aspect=True)
    text(tile.add(top_stretch(f"{name}_Name", 274, 22, 12)), figure.capitalize(),
         size=16, colour=TEXT, halign=CENTER, bold=True)
    text(tile.add(top_stretch(f"{name}_Blurb", 300, 32, 14)), "",
         size=13, colour=SECONDARY, halign=CENTER, valign=TOP, wrap=True)


def boards():
    root = Node("Effect")
    # Under the game's own canvas (15), so the pause menu covers the board and not the
    # other way round. The cursor is at 30000 and always on top.
    canvas(root, sorting_order=0)

    root.add(panel("Pick", PICKS, POWERUP_W, POWERUP_H, "Powerup", powerup_tile))
    root.add(panel("Shape", SHAPES, 300, 340, "Footprint. The first pick on the team counts.", shape_board_tile))
    root.add(panel("Swap", SWAPS, 200, 220, "Metal Wall", option_tile))

    root.add(attack_screen())
    return root


# ---- the attack screen ---------------------------------------------------------------
#
# The attacker's pick, whole screen: the base they are taking, as the map's own GPS
# picture cropped round the anchor with a keep on it and a dot on every spawn, and
# the five raid tiles under it. A dot is a button; the plugin lights the viewer's own
# and hangs the faces of teammates off whichever dot each of them chose, so the
# team's spread is on the map without a word. Two lines under the HUD say which
# round, which base, and that the board wants two answers; every dot carries which
# way and how far, sent by the plugin from the same numbers the spawns are.
#
# The map sits in the middle third of the screen and nothing of ours is above or left
# of it, because the game draws chat top-left and a team wants to read each other
# while they pick. The HUD is on its own canvas over all of this.
#
# A dot's place is a position, and a position cannot be sent to a client, only shown or
# hidden, so the map and its dots are baked per site: one picture and three dots for
# each, and the plugin shows the site being attacked. The numbers are the plugin's
# config (Sites in PeekSeekRaidConfiguration.cs), copied here; change one and
# change the other.
#
# Nothing here is placed by a row count. The canvas is the screen at the player's UI
# scale, so a 1080-row layout runs off a 768-row screen. Instead the tiles hang off
# the bottom edge, the map takes whatever is left between them and the HUD as the
# largest square that fits (an AspectRatioFitter, uGUI's own), and every dot is at a
# fraction of the map rather than a pixel of it, so the same prefab is right on a
# laptop and on a 1440p monitor.

MAP_PX = 640            # the map at most; the sprite is twice this
MAP_METRES = 260        # of ground across it: the furthest spawn is 108 m out
MAP_Y = 136             # below the HUD's line of faces
HEADLINE_Y = 74         # the two lines between the HUD and the map
MAP_BOTTOM = 30         # under the tiles
DOT = 40
FACE = 24               # a teammate on a dot, the site's .userProfile.small chip
FACES = 5
KEEP = 56               # the disc under the keep
CHIP_H = 44             # the timer chip
BOARD_W = PICKS * POWERUP_W + (PICKS - 1) * GAP

SITES = (
    # name, anchor (x, z), the spawns in dot order; all from PeekSeekRaidConfiguration.cs
    ("North", (74.97, 311.35), ((152.60, 383.00), (8.21, 286.77), (91.95, 232.95))),
    ("South", (224.10, -379.60), ((150.36, -364.48), (207.59, -306.13), (311.91, -369.68))),
)
MAP_GUIDS = ("e2f1a3b4c5d6e7f8a9b0c1d2e3f4a5b6", "f3a2b4c5d6e7f8a9b0c1d2e3f4a5b6c7")


def attack_screen():
    screen = stretch("Attack", active=False)
    image(screen, PAGE, 0.94)

    # A column down the middle, from under the HUD to just off the bottom edge.
    text(screen.add(top_center("Attack_Headline", 0, HEADLINE_Y, 700, 26)), "Round 4  You attack North",
         size=20, colour=TEXT, halign=CENTER, bold=True)
    text(screen.add(top_center("Attack_Line", 0, HEADLINE_Y + 28, 700, 20)), "Where you come in, then what you bring",
         size=13, colour=SECONDARY, halign=CENTER)

    column = screen.add(Node("Attack_Column", anchor_min=(0.5, 0), anchor_max=(0.5, 1),
                             position=(0, (MAP_BOTTOM - MAP_Y) / 2), size=(BOARD_W, -(MAP_Y + MAP_BOTTOM))))

    board = column.add(bottom_center("Attack_Board", 0, 0, BOARD_W, POWERUP_H))
    for i in range(PICKS):
        powerup_tile(board, "Attack", i, i * (POWERUP_W + GAP), 0)

    # What is left above the tiles, and the map the largest square in it, at most
    # the size the picture was drawn for.
    area = column.add(Node("Attack_MapArea", anchor_min=(0.5, 0), anchor_max=(0.5, 1),
                           position=(0, (POWERUP_H + 20) / 2), size=(MAP_PX, -(POWERUP_H + 20))))
    frame = area.add(centered("Attack_Map", 0, 0, MAP_PX, MAP_PX))
    aspect_fit(frame)
    # The frame clips the picture to the site's corners, the way a face is.
    image(frame, "#ffffff", sprite=SPR["round8"])
    mask(frame)

    for site, (name, anchor, spawns) in enumerate(SITES):
        ground = frame.add(stretch(f"Attack_Map{site}", active=False))
        image(ground, "#ffffff", sprite=SPR[f"map{site}"], sliced=False)

        for i, spawn in enumerate(spawns):
            dot(ground, site, i, *on_map(anchor, spawn))

    keep = frame.add(centered("Keep", 0, 0, KEEP, KEEP))
    image(keep, PAGE, 0.9, sprite=SPR["disc"], sliced=False)
    image(keep.add(centered("KeepFigure", 0, 0, 32, 32)), TEXT, sprite=SPR["keep"], sliced=False)

    text(frame.add(top_left("North", 12, 8, 20, 20)), "N", size=12, colour=TEXT, bold=True)
    text(frame.add(top_left("Attack_Site", 34, 8, 200, 20)), "North", size=14, colour=TEXT, bold=True)

    chip = frame.add(top_right("Clock", 10, 10, 80, CHIP_H))
    image(chip, PAGE, 0.9, sprite=SPR["round8"])
    text(chip.add(stretch("Attack_Timer")), "20", size=28, colour=TEXT, halign=CENTER, bold=True)

    return screen


def on_map(anchor, point):
    """Where a world point lands on the map, as fractions of it from the bottom-left
    corner, which is how a uGUI anchor counts. North is up, so world z runs up."""
    return (0.5 + (point[0] - anchor[0]) / MAP_METRES, 0.5 + (point[1] - anchor[1]) / MAP_METRES)


def dot(ground, site, i, fx, fy):
    """A spawn on the map: a dark disc with its number, the button `BoardSpawn{site}{i}`,
    anchored at a fraction of the map so it stays on its spawn at any size.
    `Spawn{site}{i}_On` is the same disc in white, shown on the one the viewer chose;
    `Spawn{site}{i}_Face{k}` are the teammates coming in there, in a row beside it."""
    node = ground.add(Node(f"BoardSpawn{site}{i}", anchor_min=(fx, fy), anchor_max=(fx, fy), size=(DOT, DOT)))
    button(node, image(node, "#ffffff", sprite=SPR["disc"], sliced=False, raycast=True), PAGE, SUB_HOVER, SELECTED)
    text(node.add(stretch(f"Spawn{site}{i}_Number")), str(i + 1), size=16, colour=TEXT, halign=CENTER, bold=True)

    on = node.add(stretch(f"Spawn{site}{i}_On", active=False))
    image(on, "#ffffff", sprite=SPR["disc"], sliced=False)
    text(on.add(stretch("Number")), str(i + 1), size=16, colour=PAGE, halign=CENTER, bold=True)

    # Which way and how far, under the dot; the plugin sends it, from the spawn's own
    # numbers, so a dot is something a team can name to each other.
    text(node.add(Node(f"Spawn{site}{i}_Label", anchor_min=(0.5, 0), anchor_max=(0.5, 0), pivot=(0.5, 1),
                       position=(0, -2), size=(160, 18))), "NE  106 m", size=12, colour=TEXT, halign=CENTER, bold=True)

    for k in range(FACES):
        face = node.add(top_left(f"Spawn{site}{i}_Face{k}_Frame", DOT + 6 + k * (FACE + 3), (DOT - FACE) / 2, FACE, FACE))
        image(face, "#ffffff", sprite=SPR["round6"])
        mask(face)
        image(face.add(stretch(f"Spawn{site}{i}_Face{k}", active=False)), "#ffffff", enabled=False)


# ---- the match HUD -------------------------------------------------------------------
#
# The concept (conceptArtOverlay.png): one bar along the top. In the middle, on the one
# filled block, `team 0 [castle] 0:30 [swords] 1 team2`, the castle beside whoever holds
# the flag this round and the swords beside whoever is after it. To either side, on
# nothing, a face per player with their pick on its corner and their health under it;
# the viewer's own team on the left. No names and no guns: a face and a pick say who
# somebody is.
#
# The score line is a layout row, not a list of pixel positions. Not every gamemode has
# a castle and swords, or a clock, and a marker a gamemode does not have is switched
# off, at which point the row closes up round it and the line is `team 0  1 team2`,
# centred, with no hole where the marker was. That is uGUI's own HorizontalLayoutGroup
# doing what it is for: nothing here has to know which gamemode is running.
#
# The block is 360 by 50, ten down from the top edge, and that is the game's rect, not
# ours. Every PeekSeek map is an Arena map, and on every Arena map the client draws two
# boxes of its own at the top of the screen (PlayerLifeUI: the arena message, 300 wide
# from 180 left of centre, and the round number, 50 wide ending 180 right of it, both
# 50 tall in a container inset 10 from the screen edge) with the compass under them,
# 360 wide from y 70 to 120. Nothing a server sends can switch the boxes off, so the
# block is exactly their rect and stops there: the compass is left showing. Both
# canvases scale by the same UI scale, so the px agree. The block's corners are the
# site's 8px on all four sides, since it touches nothing; the game's boxes are drawn a
# pixel in from their rect with a radius of nine, so they never show past ours.
#
# Faces are the block's height and sit beside it, on the same line. The pick is a chip
# on the face's outer bottom corner, mostly on the face and a little over its edge,
# tilted outward; the health bar runs under the face, green until half and amber
# after, and is only there while there is something to say: a whole face and a dead
# one both hide it. The clock is set in a monospaced face because a proportional one
# shuffled the readout every second; the block says who, how many and how long, and
# nothing else.

BLOCK_W, BLOCK_H = 360, 50
BLOCK_Y = 10            # the arena boxes' container inset
ROW = 50                # the score line, the whole block
SEAT = 50               # a face, the site's .userProfile chip
SEAT_GAP = 6
WING_PAD = 8            # between the block and the first face
SEATS = 5
WING_W = WING_PAD + SEATS * (SEAT + SEAT_GAP) - SEAT_GAP
HUD_W = BLOCK_W + 2 * WING_W
PICK = 20               # the pick chip on a face's corner
PICK_OVER = 3           # how far it pokes past the face's outer edge
PICK_TILT = 10          # degrees; the one playful thing on the bar
ROLE = 20               # the castle or the swords
HEALTH_H = 6            # the bar under a face
HEALTH_GAP = 3
HEALTH_STEPS = 10       # what the plugin lights, one message each
PING_H = 13             # the viewer's own ping under their face
GONE = 16               # the cross on a face whose player has left

GREEN = "#6fb65a"       # the health bar, above half
AMBER = "#d9a521"       # and at half or under
TRACK = "#2c2c2c"       # what is left of it


def match_hud():
    root = Node("Effect")
    canvas(root, sorting_order=16)

    hud = root.add(top_center("Hud", 0, 0, HUD_W, BLOCK_Y + BLOCK_H + HEALTH_GAP + HEALTH_H + PING_H))

    block = hud.add(top_center("Block", 0, BLOCK_Y, BLOCK_W, BLOCK_H))
    image(block, FLAT, sprite=SPR["round8"])

    # The score line: every field at its own width, closed up round whatever is off.
    # The clock comes in two shapes the plugin chooses between: bare, centred on the
    # line, or under a label, for the one phase that needs naming.
    line = block.add(stretch("Line"))
    row(line, spacing=4)
    text(line.add(top_left("Team0_Name", 0, 0, 80, ROW)), "North", size=16, colour=INK, halign=RIGHT, bold=True)
    text(line.add(top_left("Score0", 0, 0, 28, ROW)), "0", size=22, colour=INK, halign=CENTER, bold=True)
    role(line, "Castle0", "Swords0")
    text(line.add(top_left("Timer", 0, 0, 68, ROW, active=False)), "0:30", size=18, colour=INK, halign=CENTER, mono=True)
    clock = line.add(top_left("Clock", 0, 0, 68, ROW, active=False))
    text(clock.add(top_left("Clock_Phase", 0, 7, 68, 12)), "BUILD",
         size=9, colour=MUTED, halign=CENTER, bold=True, spacing=12)
    text(clock.add(top_left("Clock_Timer", 0, 19, 68, 26)), "4:12", size=18, colour=INK, halign=CENTER, mono=True)
    role(line, "Castle1", "Swords1")
    text(line.add(top_left("Score1", 0, 0, 28, ROW)), "1", size=22, colour=INK, halign=CENTER, bold=True)
    text(line.add(top_left("Team1_Name", 0, 0, 80, ROW)), "South", size=16, colour=INK, halign=LEFT, bold=True)

    # Faces run outward from the block on each side, on its line. A face is two
    # pictures of the same download, one at full strength and one at half, and the
    # plugin shows the half one while that player is out of the round: nothing a
    # server can send sets an alpha, but it can choose which of two images is on.
    for prefix, place, corner, tilt in (("L", top_left, 0, -PICK_TILT), ("R", top_right, 1, PICK_TILT)):
        for i in range(SEATS):
            x = WING_W - WING_PAD - (i + 1) * SEAT - i * SEAT_GAP
            seat = hud.add(place(f"{prefix}{i}", x, BLOCK_Y + BLOCK_H - SEAT, SEAT, SEAT + HEALTH_GAP + HEALTH_H + PING_H, active=False))

            frame = seat.add(top_left(f"{prefix}{i}_Frame", 0, 0, SEAT, SEAT))
            image(frame, "#ffffff", sprite=SPR["round8"])
            mask(frame)
            image(frame.add(stretch(f"{prefix}{i}_Avatar")), "#ffffff", enabled=False)
            image(frame.add(stretch(f"{prefix}{i}_Faded", active=False)), "#ffffff", 0.5, enabled=False)

            pick_chip(seat, f"{prefix}{i}_Pick", corner, tilt)
            gone_mark(seat, f"{prefix}{i}_Gone", 1 - corner)
            health_bar(seat, f"{prefix}{i}")
            # Under the health bar, for the viewer's own face only: their ping, in the
            # colour of what it is costing them. Off for everybody else.
            text(seat.add(top_left(f"{prefix}{i}_Ping", -10, SEAT + HEALTH_GAP + HEALTH_H + 1, SEAT + 20, PING_H, active=False)),
                 "190 ms", size=10, colour=INK, halign=CENTER, bold=True)

    return root


def role(line, castle, swords):
    """The two role markers, one after the other in the line; the plugin shows one, or
    neither between rounds and in a gamemode that has no such thing. The castle is
    the side holding, in blue; the swords the side attacking, in red."""
    image(line.add(top_left(castle, 0, 0, ROLE, ROLE, active=False)), BLUE, sprite=SPR["castle"], sliced=False)
    image(line.add(top_left(swords, 0, 0, ROLE, ROLE, active=False)), RED, sprite=SPR["swords"], sliced=False)


def pick_chip(seat, name, corner, tilt):
    """The pick on a face's outer bottom corner: the item's picture on nothing, mostly
    on the face, a little past its edge, tilted outward. Off until there is a pick to
    show."""
    centre_x = PICK / 2 - PICK_OVER
    chip = seat.add(Node(name, anchor_min=(corner, 1), anchor_max=(corner, 1),
                         position=(centre_x if corner == 0 else -centre_x, -(SEAT - PICK / 2 + 1)),
                         size=(PICK, PICK), rotation=tilt, active=False))
    image(chip.add(stretch(f"{name}_Icon")), "#ffffff", enabled=False, preserve_aspect=True)


def gone_mark(seat, name, corner):
    """A cross on the face's inner top corner for a player who has left the server,
    which a faded face alone does not say: faded is dead, and a leaver is not coming
    back next round."""
    mark = seat.add(Node(name, anchor_min=(corner, 1), anchor_max=(corner, 1),
                         position=(4 if corner == 0 else -4, -4), size=(GONE, GONE), active=False))
    text(mark.add(stretch("Cross")), "×", size=15, colour=INK, halign=CENTER, bold=True)


def health_bar(seat, name):
    """The bar under a face: a rounded track with ten steps of fill in it, in two
    colours the plugin chooses between, because it can turn an image on or off but
    not recolour it. The track is the mask, so the steps come out with its rounded
    ends. Everything here starts off; the plugin lights what there is."""
    bar = seat.add(top_left(f"{name}_Health", 0, SEAT + HEALTH_GAP, SEAT, HEALTH_H, active=False))
    image(bar, TRACK, sprite=SPR["round3"])
    mask(bar, show=True)

    step = SEAT / HEALTH_STEPS
    for colour_name, colour in (("Green", GREEN), ("Amber", AMBER)):
        fill = bar.add(stretch(f"{name}_Health{colour_name}", active=False))
        for k in range(HEALTH_STEPS):
            image(fill.add(top_left(f"{name}_{colour_name}{k}", k * step, 0, step, HEALTH_H, active=False)), colour)


# ---- the lobby's bar -----------------------------------------------------------------
#
# The match bar, for a server with no match on it: the same block, ten down and the
# arena boxes' width, and a face per player to either side. On the block, two things
# a player standing here wants to know: whether they are queued and for how long,
# and how long the arena has left before it resets. Each is a label and a clock the
# plugin switches on when there is something to say, in one row that closes up round
# what is off, so a block with only the arena on it is still centred.
#
# There are no teams, so faces go round the block by score, the first on the left, the
# second on the right, and so on, six a side. On a face's outer corner, their kills
# this arena as a white tilted number, on nothing; a face at half strength is
# somebody who is not fighting, dead, waiting at the spawn, or away, and away gets
# a mark on its inner corner so the two can be told apart.

LOBBY_SEATS = 6
LOBBY_WING_W = WING_PAD + LOBBY_SEATS * (SEAT + SEAT_GAP) - SEAT_GAP
KILLS_W = 30


def lobby_bar():
    root = Node("Effect")
    canvas(root, sorting_order=16)

    hud = root.add(top_center("Hud", 0, 0, BLOCK_W + 2 * LOBBY_WING_W, BLOCK_Y + BLOCK_H))

    block = hud.add(top_center("Block", 0, BLOCK_Y, BLOCK_W, BLOCK_H))
    image(block, FLAT, sprite=SPR["round8"])

    line = block.add(stretch("Line"))
    row(line, spacing=8)
    text(line.add(top_left("State", 0, 0, 90, ROW, active=False)), "QUEUE",
         size=11, colour=MUTED, halign=RIGHT, bold=True, spacing=8)
    text(line.add(top_left("State_Timer", 0, 0, 56, ROW, active=False)), "1:24", size=18, colour=INK, halign=LEFT, mono=True)
    line.add(top_left("Gap", 0, 0, 16, ROW, active=False))
    text(line.add(top_left("Arena", 0, 0, 62, ROW, active=False)), "ARENA",
         size=11, colour=MUTED, halign=RIGHT, bold=True, spacing=8)
    text(line.add(top_left("Arena_Timer", 0, 0, 56, ROW, active=False)), "2:10", size=18, colour=INK, halign=LEFT, mono=True)

    for prefix, place, corner, tilt in (("L", top_left, 0, -PICK_TILT), ("R", top_right, 1, PICK_TILT)):
        for i in range(LOBBY_SEATS):
            x = LOBBY_WING_W - WING_PAD - (i + 1) * SEAT - i * SEAT_GAP
            seat = hud.add(place(f"{prefix}{i}", x, BLOCK_Y, SEAT, SEAT, active=False))
            frame = seat.add(top_left(f"{prefix}{i}_Frame", 0, 0, SEAT, SEAT))
            image(frame, "#ffffff", sprite=SPR["round8"])
            mask(frame)
            image(frame.add(stretch(f"{prefix}{i}_Avatar")), "#ffffff", enabled=False)
            image(frame.add(stretch(f"{prefix}{i}_Faded", active=False)), "#ffffff", 0.5, enabled=False)

            kills = seat.add(Node(f"{prefix}{i}_Kills", anchor_min=(corner, 0), anchor_max=(corner, 0),
                                  position=(6 if corner == 0 else -6, 4), size=(KILLS_W, 18), rotation=tilt, active=False))
            text(kills.add(stretch("Number")), "12", size=14, colour=INK, halign=CENTER, bold=True)
            away = seat.add(Node(f"{prefix}{i}_Away", anchor_min=(1 - corner, 1), anchor_max=(1 - corner, 1),
                                 position=(2 if corner == 1 else -2, -2), size=(26, 12), active=False))
            text(away.add(stretch("Label")), "AFK", size=8, colour=INK, halign=CENTER, bold=True)

    return root


# ---- the build panel -----------------------------------------------------------------
#
# Beside the screen during the build phase: what the team has left to lay, a row per
# kind of piece. The planner shows what can go down; this shows how many. The hint at
# the bottom is baked in rather than sent, because the game only replaces the
# `<plugin_0/>` token with the player's own binding when the effect is spawned.

PANEL_W = 240
PANEL_ROWS = 16
PANEL_ROW_H = 28
PANEL_PAD_ = 12
PANEL_HEAD = 26
PANEL_HINT = 40
PANEL_H = PANEL_PAD_ + PANEL_HEAD + PANEL_ROWS * PANEL_ROW_H + 8 + PANEL_HINT + PANEL_PAD_


def build_panel():
    root = Node("Effect")
    canvas(root, sorting_order=16)

    board = root.add(top_right("Panel", 20, 120, PANEL_W, PANEL_H))
    image(board, FLAT, 0.9, sprite=SPR["round8"])

    text(board.add(top_left("Title", PANEL_PAD_, PANEL_PAD_, PANEL_W - 2 * PANEL_PAD_, PANEL_HEAD)), "Left to lay",
         size=11, colour=MUTED, bold=True, uppercase=True, spacing=10)

    for i in range(PANEL_ROWS):
        y = PANEL_PAD_ + PANEL_HEAD + i * PANEL_ROW_H
        row = board.add(top_left(f"Row{i}", PANEL_PAD_, y, PANEL_W - 2 * PANEL_PAD_, PANEL_ROW_H, active=False))
        image(row.add(top_left(f"Row{i}_Icon", 0, 2, 24, 24)), "#ffffff", enabled=False, preserve_aspect=True)
        text(row.add(top_left(f"Row{i}_Count", 30, 0, 34, PANEL_ROW_H)), "12", size=15, colour=INK, halign=RIGHT, bold=True)
        text(row.add(top_left(f"Row{i}_Name", 72, 0, PANEL_W - 2 * PANEL_PAD_ - 72, PANEL_ROW_H)), "Maple Wall",
             size=13, colour=MUTED)

    text(board.add(bottom_left("Hint", PANEL_PAD_, PANEL_PAD_, PANEL_W - 2 * PANEL_PAD_, PANEL_HINT)),
         "Salvage a piece to change what it is made of, or take it down.",
         size=12, colour=MUTED, valign=TOP, wrap=True)

    return root


# ---- the kill feed --------------------------------------------------------------------
#
# Its own mod, so the lobby can carry the feed without the rest of the screens. Top
# right, a row per kill: who, with what, whom; the weapon a white silhouette, a skull
# after it for a headshot, a roman numeral beside the killer's name from their second
# kill of a life. The fill is the HUD's block. A row a viewer is in, either end, gets
# the one red the overlay has, as a hairline round it.
#
# A row is a run of parts a fixed gap apart, and exactly as wide as the run: a layout
# row with a size fitter, each text as wide as its text, each picture a fixed size.
# So a long name makes the row wider, never pokes out of it, and a part the plugin
# turns off, the killer of a death nobody dealt, a streak of one, a weapon the game
# cannot name, takes no space and leaves no gap. Rows hang off the right edge and grow
# leftward. The hairline is a ring one pixel proud of the fill, stretched with it and
# ignored by the layout, because a border cannot be set on an Image but a ring behind
# nothing draws the same.

FEED_ROWS = 6
FEED_H = 26
FEED_GAP = 4
FEED_PAD = 8            # inside the row's ends
FEED_SPACING = 6        # between the parts
WEAPON_W = 40
HEADSHOT = 16


def kill_feed():
    root = Node("Effect")
    canvas(root, sorting_order=16)

    feed = root.add(top_right("Feed", 20, 20, 600, FEED_ROWS * (FEED_H + FEED_GAP)))

    for i in range(FEED_ROWS):
        fill = feed.add(top_right(f"Row{i}", 0, i * (FEED_H + FEED_GAP), 0, FEED_H, active=False))
        image(fill, FLAT, sprite=SPR["round6"])
        row(fill, spacing=FEED_SPACING, padding=FEED_PAD, control_width=True)
        fit_width(fill)

        mine = fill.add(Node(f"Row{i}_Mine", anchor_min=(0, 0), anchor_max=(1, 1), size=(2, 2), active=False))
        image(mine, RED, sprite=SPR["ring6"])
        ignore_layout(mine)

        text(fill.add(top_left(f"Row{i}_Killer", 0, 0, 0, FEED_H)), "Ferret", size=13, colour=INK, bold=True)
        text(fill.add(top_left(f"Row{i}_Streak", 0, 0, 0, FEED_H, active=False)), "II", size=11, colour=RED, bold=True)
        weapon = fill.add(top_left(f"Row{i}_Weapon", 0, 0, WEAPON_W, FEED_H - 6, active=False))
        image(weapon, "#ffffff", enabled=False, preserve_aspect=True, material=SILHOUETTE)
        layout_size(weapon, WEAPON_W, FEED_H - 6)
        skull = fill.add(top_left(f"Row{i}_Headshot", 0, 0, HEADSHOT, HEADSHOT, active=False))
        image(skull, INK, sprite=SPR["skull"], sliced=False)
        layout_size(skull, HEADSHOT, HEADSHOT)
        text(fill.add(top_left(f"Row{i}_Victim", 0, 0, 0, FEED_H)), "Homeless", size=13, colour=INK, bold=True)

    return root


# ---- the banner -----------------------------------------------------------------------
#
# The overlay's banner, in game: a small label over a large name, and one line under
# it, centred a quarter of the way down the screen for a few seconds after a round or
# a match. The same three lines the stream shows, so a viewer and a player read the
# same thing. Red is the plugin's, by rich text, for the match-winning one.

BANNER_W, BANNER_H = 620, 92


def banner():
    root = Node("Effect")
    canvas(root, sorting_order=17)

    box = root.add(Node("Banner", anchor_min=(0.5, 0.74), anchor_max=(0.5, 0.74), size=(BANNER_W, BANNER_H)))
    image(box, FLAT, 0.9, sprite=SPR["round8"])
    text(box.add(top_stretch("Banner_Label", 10, 16, 16)), "TAKES ROUND 4",
         size=11, colour=MUTED, halign=CENTER, bold=True, spacing=12)
    text(box.add(top_stretch("Banner_Name", 26, 38, 16)), "South", size=30, colour=INK, halign=CENTER, bold=True)
    text(box.add(top_stretch("Banner_Line", 66, 18, 16)), "The flag is down", size=12, colour=MUTED, halign=CENTER)

    return root


# ---- the death card -------------------------------------------------------------------
#
# What a death is, said to the one it happened to: who, with what, from how far, what
# they had left and what you did to them. Bottom centre, above the game's own hotbar,
# for a few seconds. The top line is a run of parts, as wide as it is; the second
# line is one sentence. A death nobody dealt is the top line with the cause alone.
#
# The respawn count sits in the same effect, in the middle of the screen, for a server
# that brings the dead back on a clock: the lobby. A match server never turns it on.

CARD_W = 560
CARD_Y = 150            # up from the bottom edge, clear of the hotbar


def death_card():
    root = Node("Effect")
    canvas(root, sorting_order=17)

    card = root.add(bottom_center("Card", 0, CARD_Y, CARD_W, 62, active=False))
    image(card, FLAT, 0.9, sprite=SPR["round8"])

    top = card.add(top_center("Card_Top", 0, 10, 0, 22))
    row(top, spacing=8, control_width=True)
    fit_width(top)
    text(top.add(top_left("Card_Label", 0, 0, 0, 22)), "KILLED BY", size=11, colour=MUTED, bold=True, spacing=10)
    text(top.add(top_left("Card_Killer", 0, 0, 0, 22)), "Ferret", size=14, colour=INK, bold=True)
    weapon = top.add(top_left("Card_Weapon", 0, 0, WEAPON_W, 20, active=False))
    image(weapon, "#ffffff", enabled=False, preserve_aspect=True, material=SILHOUETTE)
    layout_size(weapon, WEAPON_W, 20)
    skull = top.add(top_left("Card_Headshot", 0, 0, HEADSHOT, HEADSHOT, active=False))
    image(skull, INK, sprite=SPR["skull"], sliced=False)
    layout_size(skull, HEADSHOT, HEADSHOT)
    text(top.add(top_left("Card_Distance", 0, 0, 0, 22, active=False)), "34 m", size=13, colour=INK)

    text(card.add(top_stretch("Card_Line", 36, 18, 12)), "They had 27 HP left    You dealt 73 in 2 hits",
         size=12, colour=MUTED, halign=CENTER)

    count = root.add(Node("Respawn", anchor_min=(0.5, 0.5), anchor_max=(0.5, 0.5), position=(0, 40), size=(240, 80), active=False))
    text(count.add(top_stretch("Respawn_Count", 0, 52, 0)), "5", size=44, colour=INK, halign=CENTER, mono=True)
    text(count.add(top_stretch("Respawn_Label", 56, 16, 0)), "RESPAWNING",
         size=11, colour=MUTED, halign=CENTER, bold=True, spacing=12)

    return root


# ---- the scoreboard -------------------------------------------------------------------
#
# A page of the site: a title, four numbered columns, and a row per player with their
# face and name, the viewer's own row filled. A row can instead be a section heading,
# a team's name over its players, so one prefab draws a two-team match and a lobby's
# single list. Held up on a plugin key during play; up for everybody, unasked, when
# a match ends or the lobby's arena resets.

BOARD_PANEL_W = 620
BOARD_ROWS = 12
BOARD_ROW_H = 28
BOARD_ROW_GAP = 2
BOARD_COLS = ("A", "B", "C", "D")
BOARD_COL_W = 56
BOARD_ROWS_Y = 84


def scoreboard():
    root = Node("Effect")
    canvas(root, sorting_order=1)

    height = BOARD_ROWS_Y + BOARD_ROWS * (BOARD_ROW_H + BOARD_ROW_GAP) + PANEL_PAD
    board = root.add(centered("Board", 0, 20, BOARD_PANEL_W, height))
    image(board, PAGE, 0.94, sprite=SPR["round8"])

    text(board.add(top_left("Board_Title", PANEL_PAD, PANEL_PAD, BOARD_PANEL_W - 2 * PANEL_PAD, 26)), "North wins 3-1",
         size=20, colour=TEXT, bold=True)
    text(board.add(top_left("Board_Sub", PANEL_PAD, PANEL_PAD + 26, BOARD_PANEL_W - 2 * PANEL_PAD, 18)), "",
         size=12, colour=SECONDARY)

    first_col = BOARD_PANEL_W - PANEL_PAD - len(BOARD_COLS) * BOARD_COL_W
    for c, col in enumerate(BOARD_COLS):
        text(board.add(top_left(f"Head_{col}", first_col + c * BOARD_COL_W, BOARD_ROWS_Y - 18, BOARD_COL_W, 14)), col,
             size=10, colour=SECONDARY, halign=RIGHT, bold=True, spacing=6)

    for i in range(BOARD_ROWS):
        y = BOARD_ROWS_Y + i * (BOARD_ROW_H + BOARD_ROW_GAP)
        line = board.add(top_left(f"Row{i}", PANEL_PAD - 10, y, BOARD_PANEL_W - 2 * PANEL_PAD + 20, BOARD_ROW_H, active=False))
        image(line.add(stretch(f"Row{i}_Me", active=False)), SELECTED, sprite=SPR["round6"])

        text(line.add(top_left(f"Row{i}_Head", 10, 0, 300, BOARD_ROW_H, active=False)), "NORTH",
             size=10, colour=SECONDARY, bold=True, spacing=8)

        body = line.add(stretch(f"Row{i}_Body"))
        face = body.add(top_left(f"Row{i}_Frame", 10, (BOARD_ROW_H - 22) / 2, 22, 22))
        image(face, "#ffffff", sprite=SPR["round6"])
        mask(face)
        image(face.add(stretch(f"Row{i}_Avatar")), "#ffffff", enabled=False)
        text(body.add(top_left(f"Row{i}_Name", 40, 0, 230, BOARD_ROW_H)), "kieran", size=13, colour=TEXT, bold=True)
        text(body.add(top_left(f"Row{i}_Tag", 276, 0, 60, BOARD_ROW_H, active=False)), "MVP",
             size=9, colour=SELECTED_LABEL, bold=True, spacing=8)
        for c, col in enumerate(BOARD_COLS):
            text(body.add(top_left(f"Row{i}_{col}", 10 + first_col - PANEL_PAD + c * BOARD_COL_W, 0, BOARD_COL_W, BOARD_ROW_H)), "0",
                 size=13, colour=TEXT, halign=RIGHT)

    return root


# ---- the choice -----------------------------------------------------------------------
#
# A question with two answers, the way the boards ask one: a headline, a line under
# it, and two tiles. The lobby asks whether to queue with it. Buttons are
# `BoardChoice0` and `BoardChoice1`, in the boards' own naming so a click lands in
# the same handler shape.

CHOICES = 2
CHOICE_W, CHOICE_H = 220, 96
CHOICE_LINE_H = 40


def choice():
    root = Node("Effect")
    canvas(root, sorting_order=0)

    board_w = PANEL_PAD * 2 + CHOICES * CHOICE_W + (CHOICES - 1) * GAP
    tiles_y = PANEL_PAD + HEADLINE_H + 6 + CHOICE_LINE_H + 10
    board = root.add(centered("Choice", 0, 40, board_w, tiles_y + CHOICE_H + PANEL_PAD))
    image(board, PAGE, 0.94, sprite=SPR["round8"])

    text(board.add(top_left("Choice_Headline", PANEL_PAD, PANEL_PAD, board_w - 2 * PANEL_PAD, HEADLINE_H)), "Queue for a match?",
         size=20, colour=TEXT, bold=True)
    text(board.add(top_left("Choice_Line", PANEL_PAD, PANEL_PAD + HEADLINE_H + 6, board_w - 2 * PANEL_PAD, CHOICE_LINE_H)), "",
         size=13, colour=SECONDARY, valign=TOP, wrap=True)

    for i in range(CHOICES):
        node = board.add(top_left(f"BoardChoice{i}", PANEL_PAD + i * (CHOICE_W + GAP), tiles_y, CHOICE_W, CHOICE_H))
        button(node, image(node, "#ffffff", sprite=SPR["round8"], raycast=True), SUB, SUB_HOVER, SELECTED)
        text(node.add(top_stretch(f"Choice{i}_Name", 22, 24, 12)), "Queue", size=16, colour=TEXT, halign=CENTER, bold=True)
        text(node.add(top_stretch(f"Choice{i}_Blurb", 50, 36, 14)), "",
             size=12, colour=SECONDARY, halign=CENTER, valign=TOP, wrap=True)

    return root


# ---- the tag --------------------------------------------------------------------------
#
# One short thing said at the bottom of the screen for as long as it is true: a word,
# up to three pips counting something down, and a second word in red. Raid says OFF
# BASE with it, the grace running out in the pips and the damage in red after.

TAG_Y = 110
PIP_W, PIP_H = 10, 5


def tag():
    root = Node("Effect")
    canvas(root, sorting_order=17)

    box = root.add(bottom_center("Tag", 0, TAG_Y, 0, 30))
    image(box, FLAT, 0.9, sprite=SPR["round6"])
    row(box, spacing=8, padding=12, control_width=True)
    fit_width(box)

    text(box.add(top_left("Tag_Text", 0, 0, 0, 30)), "OFF BASE", size=12, colour=INK, bold=True, spacing=8)
    for k in range(3):
        pip = box.add(top_left(f"Tag_Pip{k}", 0, 0, PIP_W, PIP_H, active=False))
        image(pip, INK, sprite=SPR["round3"])
        layout_size(pip, PIP_W, PIP_H)
    text(box.add(top_left("Tag_Hurt", 0, 0, 0, 30, active=False)), "-12 / s", size=12, colour=RED, bold=True)

    return root


if __name__ == "__main__":
    write_sprites()
    with Image.open(SPRITES / "Logo.png") as mark:
        LOGO_W = round(LOGO_H * mark.width / mark.height)
    SILHOUETTE = silhouette(ROOT / "Shaders")

    screens = (
        (EFFECTS / "PickBoard", boards()),
        (EFFECTS / "MatchHud", match_hud()),
        (EFFECTS / "BuildPanel", build_panel()),
        (EFFECTS / "Tag", tag()),
        (KILLFEED_EFFECTS / "KillFeed", kill_feed()),
        (KILLFEED_EFFECTS / "LobbyBar", lobby_bar()),
        (KILLFEED_EFFECTS / "Banner", banner()),
        (KILLFEED_EFFECTS / "DeathCard", death_card()),
        (KILLFEED_EFFECTS / "Scoreboard", scoreboard()),
        (KILLFEED_EFFECTS / "Choice", choice()),
    )
    for folder, root in screens:
        target = folder / "Effect.prefab"
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_text(prefab(root), encoding="utf-8", newline="\n")
        print(f"wrote {target}")
