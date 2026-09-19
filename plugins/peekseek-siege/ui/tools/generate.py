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

import footprints
import sitemap
from unityyaml import (CENTER, LEFT, RIGHT, TOP, Node, aspect_fit, bottom_center, bottom_left, button, canvas,
                       castle_alpha, centered, disc_alpha, image, mask, prefab, rounded_alpha, silhouette, skull_alpha,
                       sprite, sprite_file, stretch, swords_alpha, text, top_center, top_left, top_right, top_stretch)

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
        "castle": sprite(SPRITES, "Castle", "c8a7d9e0f1b2c3d4e5f6a7b8c9d0e1f2", 24, 24, castle_alpha(24), 0),
        "swords": sprite(SPRITES, "Swords", "d9b8e0f1a2c3d4e5f6a7b8c9d0e1f2a3", 24, 24, swords_alpha(24), 0),
        "skull": sprite(SPRITES, "Skull", "c6e5a7b8d9f0a1b2c3d4e5f6a7b8c9d0", 40, 40, skull_alpha(40), 0),
        # Drawn at twice the size they are shown at, so they stay sharp at UI scale 2.
        "square": sprite_file(SPRITES, "FootprintSquare", "e0c9f1a2b3d4e5f6a7b8c9d0e1f2a3b4",
                              footprints.picture("Square", 2 * FIGURE_W, 2 * FIGURE_H, FIGURE_METRES)),
        "hexagonal": sprite_file(SPRITES, "FootprintHexagonal", "f1d0a2b3c4e5f6a7b8c9d0e1f2a3b4c5",
                                 footprints.picture("Hexagonal", 2 * FIGURE_W, 2 * FIGURE_H, FIGURE_METRES)),
    })


# ---- the boards ----------------------------------------------------------------------
#
# One prefab, three panels, one shown at a time. `Pick` is the five-tile board a
# round's options go on, with each tile's picture fetched by URL. `Shape` is the
# two-tile board the footprint is chosen on before anybody builds, with the whole
# starting base drawn on each tile; those pictures are baked in, because there are two
# and they never change. `Swap` is the six-tile board a builder changes a piece's
# material on: the same piece in every other material, and a tile to take it down.
# The plugin addresses a panel by its prefix: `Pick_Headline`, `Pick2_Name`,
# `Shape_Timer`, `Swap0_Taken`, and the tiles themselves are the buttons, `SiegePick2`,
# `SiegeShape0` and `SiegeSwap3`, which is the name that reaches the server when one is
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
    as `Siege{face}{i}`."""
    node = parent.add(top_left(f"Siege{face}{i}", x, y, w, h))
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


POWERUP_W, POWERUP_H = 160, 150


def powerup_tile(parent, face, i, x, y, w=POWERUP_W, h=POWERUP_H):
    """A powerup as the site's match page draws one (`.powerupbutton`): a card with a
    hairline, the name, then a row per item, `2x` and the item's picture. What you get
    is the picture, which is the whole point; the number is there to say "I take 2".
    Selected is the match page's red hairline and tint, with no label."""
    node = parent.add(top_left(f"Siege{face}{i}", x, y, w, h))
    # The card's own image is the hairline colour; the fill sits a pixel inside it,
    # which is the only border uGUI can draw.
    button(node, image(node, "#ffffff", sprite=SPR["round8"], raycast=True), CARD_LINE, CARD_HOVER, CARD_HOVER)
    image(node.add(Node("Fill", anchor_min=(0, 0), anchor_max=(1, 1), size=(-2, -2))), CARD, sprite=SPR["round8"])

    taken = node.add(stretch(f"{face}{i}_Taken", active=False))
    image(taken, CARD_RED, sprite=SPR["round8"])
    image(taken.add(Node("Fill", anchor_min=(0, 0), anchor_max=(1, 1), size=(-2, -2))), CARD_RED_FILL, sprite=SPR["round8"])

    text(node.add(top_left(f"{face}{i}_Number", 10, 10, 20, 18)), str(i + 1), size=13, colour=MUTED, bold=True)
    text(node.add(top_left(f"{face}{i}_Name", 28, 10, w - 38, 18)), "Demolition", size=13, colour=MUTED, bold=True)

    for k in range(ITEMS):
        row = node.add(top_left(f"{face}{i}_Item{k}", 0, 40 + k * 24, w, 24, active=False))
        text(row.add(top_left(f"{face}{i}_Item{k}_Count", w / 2 - 44, 0, 40, 24)), "3x",
             size=13, colour=MUTED, halign=RIGHT, bold=True)
        image(row.add(top_left(f"{face}{i}_Item{k}_Icon", w / 2 + 2, 2, 40, 20)), "#ffffff", enabled=False, preserve_aspect=True)

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
# team's spread is on the map without a word. There is no headline: the map says
# which base and the HUD says which round. The one line of text is the hint.
#
# The map sits in the middle third of the screen and nothing of ours is above or left
# of it, because the game draws chat top-left and a team wants to read each other
# while they pick. The HUD is on its own canvas over all of this.
#
# A dot's place is a position, and a position cannot be sent to a client, only shown or
# hidden, so the map and its dots are baked per site: one picture and three dots for
# each, and the plugin shows the site being attacked. The numbers are the plugin's
# config (Sites in PeekSeekSiegeConfiguration.cs), copied here; change one and
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
MAP_BOTTOM = 30         # under the tiles
DOT = 40
FACE = 24               # a teammate on a dot, the site's .userProfile.small chip
FACES = 5
KEEP = 56               # the disc under the keep
CHIP_H = 44             # the timer chip
BOARD_W = PICKS * POWERUP_W + (PICKS - 1) * GAP

SITES = (
    # name, anchor (x, z), the spawns in dot order; all from PeekSeekSiegeConfiguration.cs
    ("North", (74.97, 311.35), ((152.60, 383.00), (8.21, 286.77), (91.95, 232.95))),
    ("South", (234.63, -240.41), ((155.81, -173.20), (310.10, -184.88), (305.46, -321.41))),
)
MAP_GUIDS = ("e2f1a3b4c5d6e7f8a9b0c1d2e3f4a5b6", "f3a2b4c5d6e7f8a9b0c1d2e3f4a5b6c7")


def attack_screen():
    screen = stretch("Attack", active=False)
    image(screen, PAGE, 0.94)

    # A column down the middle, from under the HUD to just off the bottom edge.
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

    chip = frame.add(top_right("Clock", 10, 10, 80, CHIP_H))
    image(chip, PAGE, 0.9, sprite=SPR["round8"])
    text(chip.add(stretch("Attack_Timer")), "20", size=28, colour=TEXT, halign=CENTER, bold=True)

    return screen


def on_map(anchor, point):
    """Where a world point lands on the map, as fractions of it from the bottom-left
    corner, which is how a uGUI anchor counts. North is up, so world z runs up."""
    return (0.5 + (point[0] - anchor[0]) / MAP_METRES, 0.5 + (point[1] - anchor[1]) / MAP_METRES)


def dot(ground, site, i, fx, fy):
    """A spawn on the map: a dark disc with its number, the button `SiegeSpawn{site}{i}`,
    anchored at a fraction of the map so it stays on its spawn at any size.
    `Spawn{site}{i}_On` is the same disc in white, shown on the one the viewer chose;
    `Spawn{site}{i}_Face{k}` are the teammates coming in there, in a row beside it."""
    node = ground.add(Node(f"SiegeSpawn{site}{i}", anchor_min=(fx, fy), anchor_max=(fx, fy), size=(DOT, DOT)))
    button(node, image(node, "#ffffff", sprite=SPR["disc"], sliced=False, raycast=True), PAGE, SUB_HOVER, SELECTED)
    text(node.add(stretch(f"Spawn{site}{i}_Number")), str(i + 1), size=16, colour=TEXT, halign=CENTER, bold=True)

    on = node.add(stretch(f"Spawn{site}{i}_On", active=False))
    image(on, "#ffffff", sprite=SPR["disc"], sliced=False)
    text(on.add(stretch("Number")), str(i + 1), size=16, colour=PAGE, halign=CENTER, bold=True)

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
# nothing, a face per player with their pick hanging off its corner; the viewer's own
# team on the left. No names and no guns: a face and a pick say who somebody is.
#
# The block is 360 by 50, ten down from the top edge, and that is the game's rect, not
# ours. Siege runs on vanilla Alpha Valley, an Arena map, and on every Arena map the
# client draws two boxes of its own at the top of the screen (PlayerLifeUI: the arena
# message, 300 wide from 180 left of centre, and the round number, 50 wide ending 180
# right of it, both 50 tall in a container inset 10 from the screen edge) with the
# compass under them, 360 wide from y 70 to 120. Nothing a server sends can switch the
# boxes off, so the block is exactly their rect and stops there: the compass is left
# showing. Both canvases scale by the same UI scale, so the px agree. The block's
# corners are the site's 8px on all four sides, since it touches nothing; the game's
# boxes are drawn a pixel in from their rect with a radius of nine, so they never show
# past ours.
#
# Faces are the block's height and sit beside it, on the same line. The clock is set
# in a monospaced face because a proportional one shuffled the readout every second;
# the block says who, how many and how long, and nothing else.

BLOCK_W, BLOCK_H = 360, 50
BLOCK_Y = 10            # the arena boxes' container inset
ROW = 50                # the score line, the whole block
SEAT = 50               # a face, the site's .userProfile chip
SEAT_GAP = 6
WING_PAD = 8            # between the block and the first face
SEATS = 5
WING_W = WING_PAD + SEATS * (SEAT + SEAT_GAP) - SEAT_GAP
HUD_W = BLOCK_W + 2 * WING_W
PICK = 22               # the pick icon hanging off a face
PICK_TILT = 14          # degrees; the one playful thing on the bar
ROLE = 20               # the castle or the swords


def match_hud():
    root = Node("Effect")
    canvas(root, sorting_order=16)

    hud = root.add(top_center("Hud", 0, 0, HUD_W, BLOCK_Y + BLOCK_H + PICK // 2))

    block = hud.add(top_center("Block", 0, BLOCK_Y, BLOCK_W, BLOCK_H))
    image(block, FLAT, sprite=SPR["round8"])

    # The score line, left to right, each field vertically centred in the row.
    y = BLOCK_H - ROW
    x = 8
    text(block.add(top_left("Team0_Name", x, y, 80, ROW)), "North", size=16, colour=INK, halign=RIGHT, bold=True)
    x += 84
    text(block.add(top_left("Score0", x, y, 28, ROW)), "0", size=22, colour=INK, halign=CENTER, bold=True)
    x += 30
    role(block, "Castle0", "Swords0", x, y)
    x += 24
    text(block.add(top_left("Timer", x, y, 68, ROW)), "0:30", size=18, colour=INK, halign=CENTER, mono=True)
    x += 72
    role(block, "Castle1", "Swords1", x, y)
    x += 22
    text(block.add(top_left("Score1", x, y, 28, ROW)), "1", size=22, colour=INK, halign=CENTER, bold=True)
    x += 32
    text(block.add(top_left("Team1_Name", x, y, 80, ROW)), "South", size=16, colour=INK, halign=LEFT, bold=True)

    # Faces run outward from the block on each side, on its line. A face is two
    # pictures of the same download, one at full strength and one at half, and the
    # plugin shows the half one while that player is out of the round: nothing a
    # server can send sets an alpha, but it can choose which of two images is on.
    for prefix, place, corner, tilt in (("L", top_left, 0, -PICK_TILT), ("R", top_right, 1, PICK_TILT)):
        for i in range(SEATS):
            x = WING_W - WING_PAD - (i + 1) * SEAT - i * SEAT_GAP
            seat = hud.add(place(f"{prefix}{i}", x, BLOCK_Y + BLOCK_H - SEAT, SEAT, SEAT + PICK // 2, active=False))

            frame = seat.add(top_left(f"{prefix}{i}_Frame", 0, 0, SEAT, SEAT))
            image(frame, "#ffffff", sprite=SPR["round8"])
            mask(frame)
            image(frame.add(stretch(f"{prefix}{i}_Avatar")), "#ffffff", enabled=False)
            image(frame.add(stretch(f"{prefix}{i}_Faded", active=False)), "#ffffff", 0.5, enabled=False)

            # Hanging off the face's outer bottom corner, tilted outward.
            pick = seat.add(Node(f"{prefix}{i}_Pick", anchor_min=(corner, 1), anchor_max=(corner, 1),
                                 position=(3 if corner == 0 else -3, -(SEAT - 3)), size=(PICK, PICK), rotation=tilt))
            image(pick, "#ffffff", enabled=False, preserve_aspect=True)

    return root


def role(block, castle, swords, x, y):
    """The two role markers share a slot; the plugin shows one, or neither between
    rounds. The castle is the side holding, in blue; the swords the side attacking, in
    red."""
    y += (ROW - ROLE) // 2
    image(block.add(top_left(castle, x, y, ROLE, ROLE, active=False)), BLUE, sprite=SPR["castle"], sliced=False)
    image(block.add(top_left(swords, x, y, ROLE, ROLE, active=False)), RED, sprite=SPR["swords"], sliced=False)


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
# Its own mod and its own plugin, so a server that is not running Siege can still show
# it. Top right, a row per kill: who, with what, whom; the weapon a white silhouette,
# a roman numeral beside the killer's name from their second kill of a life. The fill is
# the HUD's block. A row a viewer is in, either end, gets the one red the overlay has,
# as a hairline round it.

FEED_ROWS = 6
FEED_W, FEED_H = 312, 26
FEED_GAP = 4
NAME_W = 110
BADGE_W = 24
WEAPON_W = 40
LONE_W = 8 + WEAPON_W + 4 + NAME_W + 8


def weapon_slot(parent, name, x):
    """The weapon's silhouette, or the skull when there is no item to show: the plugin
    turns one on and the other off."""
    image(parent.add(top_left(f"{name}_Weapon", x, 3, WEAPON_W, FEED_H - 6)), "#ffffff", enabled=False,
          preserve_aspect=True, material=SILHOUETTE)
    image(parent.add(top_left(f"{name}_Skull", x + (WEAPON_W - 18) // 2, 4, 18, 18, active=False)), INK,
          sprite=SPR["skull"], sliced=False)


def kill_feed():
    root = Node("Effect")
    canvas(root, sorting_order=16)

    feed = root.add(top_right("Feed", 20, 20, FEED_W + 4, FEED_ROWS * (FEED_H + FEED_GAP)))

    for i in range(FEED_ROWS):
        y = i * (FEED_H + FEED_GAP)
        row = feed.add(top_right(f"Row{i}", 0, y, FEED_W + 4, FEED_H + 4, active=False))

        # The hairline is a red rounded rectangle one pixel proud of the black one on
        # every side; there is no border property to set, only a shape behind a shape.
        image(row.add(stretch(f"Row{i}_Mine", active=False)), RED, sprite=SPR["round6"])
        fill = row.add(centered(f"Row{i}_Fill", 0, 0, FEED_W, FEED_H))
        image(fill, FLAT, sprite=SPR["round6"])

        # Two shapes of row. A duel is who, with what, whom. A death with nobody on
        # the other end of it, a fall, a mine of your own, the open ground, is the
        # skull and the name, on a shorter fill so it does not read as a kill with the
        # killer missing. The skull also stands in for a weapon the server cannot name.
        duel = fill.add(stretch(f"Row{i}_Duel"))
        x = 8
        text(duel.add(top_left(f"Row{i}_Killer", x, 0, NAME_W, FEED_H)), "Ferret", size=13, colour=INK, halign=RIGHT, bold=True)
        x += NAME_W + 4
        text(duel.add(top_left(f"Row{i}_Streak", x, 0, BADGE_W, FEED_H)), "II", size=11, colour=RED, halign=CENTER, bold=True)
        x += BADGE_W + 4
        weapon_slot(duel, f"Row{i}", x)
        x += WEAPON_W + 4
        text(duel.add(top_left(f"Row{i}_Victim", x, 0, NAME_W, FEED_H)), "Homeless", size=13, colour=INK, halign=LEFT, bold=True)

        lone = row.add(top_right(f"Row{i}_Lone", 0, 0, LONE_W + 4, FEED_H + 4, active=False))
        image(lone.add(stretch(f"Row{i}_Lone_Mine", active=False)), RED, sprite=SPR["round6"])
        lone_fill = lone.add(centered(f"Row{i}_LoneFill", 0, 0, LONE_W, FEED_H))
        image(lone_fill, FLAT, sprite=SPR["round6"])
        weapon_slot(lone_fill, f"Row{i}_Lone", 8)
        text(lone_fill.add(top_left(f"Row{i}_Lone_Victim", 8 + WEAPON_W + 4, 0, NAME_W, FEED_H)), "Homeless",
             size=13, colour=INK, halign=LEFT, bold=True)

    return root


if __name__ == "__main__":
    write_sprites()
    SILHOUETTE = silhouette(ROOT / "Shaders")

    screens = (
        (EFFECTS / "PickBoard", boards()),
        (EFFECTS / "MatchHud", match_hud()),
        (EFFECTS / "BuildPanel", build_panel()),
        (KILLFEED_EFFECTS / "KillFeed", kill_feed()),
    )
    for folder, root in screens:
        target = folder / "Effect.prefab"
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_text(prefab(root), encoding="utf-8", newline="\n")
        print(f"wrote {target}")
