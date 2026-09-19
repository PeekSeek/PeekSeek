"""Write the two screens PeekSeek draws in game, as Unity prefabs and sprites.

    python generate.py

Overwrites Assets/PeekSeek/Bundles/Effects/PickBoard/Effect.prefab,
Assets/PeekSeek/Bundles/Effects/MatchHud/Effect.prefab, and Assets/PeekSeek/Sprites/*.
If a screen is ever changed inside Unity instead, that edit wins and this file is stale;
bring the change back here or delete this script, but do not leave the two disagreeing.

Two screens, two design languages, on purpose. The boards are something you click, so
they are built like a page of the site: the same panel, the same selectable tile, the
same px. The match HUD is composited over play like the stream overlay, so it is built
like that: one flat fill, greyscale, red only for jeopardy. Sizes are the site's px and
the overlay's vw at 1920, which is what the game draws at UI scale 1.

The font is Inter, because the site asks for the system UI font and Inter is the open
one that reads the same; the game leaves a font that is not TextMeshPro's own alone.
"""

from pathlib import Path

import footprints
from unityyaml import (CENTER, LEFT, RIGHT, TOP, Node, bottom_left, button, canvas, centered, image, mask,
                       prefab, rounded_alpha, shield_alpha, silhouette, sprite, sprite_file, stretch, swords_alpha,
                       text, top_center, top_left, top_right, top_stretch)

ASSETS = Path(__file__).resolve().parent.parent / "Assets"
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
PANEL_RADIUS = 8        # .mainElement border-radius
PANEL_PAD = 20          # .mainElement padding
GAP = 10                # the gap inside chips and between tiles

# ---- the overlay, for the HUD (website/public/assets/css/overlay.css at 1920) --------
FLAT = "#0f0f0f"        # .flat
INK = "#f2f2f2"
MUTED = "#8a8a8a"
RED = "#ff4f4f"         # jeopardy only


# Every sprite a screen can point at, by the name the code uses. Filled by
# write_sprites before any screen is built.
SPR = {}


def write_sprites():
    SPRITES.mkdir(parents=True, exist_ok=True)
    for stale in SPRITES.glob("*"):
        stale.unlink()

    SPR.update({
        "round8": sprite(SPRITES, "Round8", "b1f0c2d3e4a5b6c7d8e9f0a1b2c3d4e5", 32, 32, rounded_alpha(32, 32, 8), 8),
        "round6": sprite(SPRITES, "Round6", "c2a1d3e4f5b6c7d8e9f0a1b2c3d4e5f6", 24, 24, rounded_alpha(24, 24, 6), 6),
        "notch": sprite(SPRITES, "Notch", "d3b2e4f5a6c7d8e9f0a1b2c3d4e5f6a7", 32, 32,
                        rounded_alpha(32, 32, 8, corners=("bl", "br")), 8),
        "shield": sprite(SPRITES, "Shield", "c8a7d9e0f1b2c3d4e5f6a7b8c9d0e1f2", 24, 24, shield_alpha(24), 0),
        "swords": sprite(SPRITES, "Swords", "d9b8e0f1a2c3d4e5f6a7b8c9d0e1f2a3", 24, 24, swords_alpha(24), 0),
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
HINT_H = 18

FIGURE_W, FIGURE_H = 260, 240
FIGURE_METRES = 26      # of ground across the picture: the hexagonal base is 23 wide


def panel(face, tiles, tile_w, tile_h, headline, hint, fill_tile):
    """A `.mainElement` with a headline, a timer, `tiles` tiles in a row, and a hint.
    `fill_tile(tile, name, i)` adds what is particular to a tile under its number."""
    board_w = PANEL_PAD * 2 + tiles * tile_w + (tiles - 1) * GAP
    tiles_y = PANEL_PAD + HEADLINE_H + 16
    board_h = tiles_y + tile_h + 14 + HINT_H + PANEL_PAD

    board = centered(face, 0, 40, board_w, board_h, active=False)
    image(board, PAGE, 0.94, sprite=SPR["round8"])

    text(board.add(top_left(f"{face}_Headline", PANEL_PAD, PANEL_PAD, board_w - PANEL_PAD * 2 - 130, HEADLINE_H)),
         headline, size=20, colour=TEXT, bold=True)
    text(board.add(top_right(f"{face}_Timer", PANEL_PAD, 16, 120, 36)),
         "20", size=28, colour=TEXT, halign=RIGHT, bold=True)
    text(board.add(bottom_left(f"{face}_Hint", PANEL_PAD, PANEL_PAD - 2, board_w - PANEL_PAD * 2, HINT_H)),
         hint, size=13, colour=SECONDARY)

    for i in range(tiles):
        tile = board.add(top_left(f"Siege{face}{i}", PANEL_PAD + i * (tile_w + GAP), tiles_y, tile_w, tile_h))
        button(tile, image(tile, "#ffffff", sprite=SPR["round8"], raycast=True), SUB, SUB_HOVER, SELECTED)

        # First child, so it paints under everything else on the tile: the site's
        # selected state is a fill and a label, not a border.
        taken = tile.add(stretch(f"{face}{i}_Taken", active=False))
        image(taken, SELECTED, sprite=SPR["round8"])
        text(taken.add(top_right("Label", GAP, 8, 80, 16)), "Selected",
             size=12, colour=SELECTED_LABEL, halign=RIGHT, bold=True)

        text(tile.add(top_left(f"{face}{i}_Number", GAP, 8, 40, 16)), str(i + 1),
             size=13, colour=SECONDARY, bold=True)

        fill_tile(tile, f"{face}{i}", i)

    return board


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

    root.add(panel("Pick", PICKS, 200, 220, "Round 2. You are defending North.",
                   "Click a tile, or type /pick and its number.", pick_tile))

    root.add(panel("Shape", SHAPES, 300, 340, "Your footprint. What the base starts from.",
                   "Click a tile, or type /pick and its number. The first pick on your team counts.",
                   shape_tile))

    root.add(panel("Swap", SWAPS, 200, 220, "Metal Wall. Make it something else.",
                   "Click a tile. The piece you are looking at changes where it stands, and what it was goes back on the pile.",
                   pick_tile))
    return root


# ---- the match HUD -------------------------------------------------------------------
#
# The concept (conceptArtOverlay.png): one bar along the top. In the middle, on the one
# filled block, `team 0 [shield] 0:30 [swords] 1 team2`, the shield beside whoever holds
# the flag this round and the swords beside whoever is after it. To either side, on
# nothing, a face per player with their pick hanging off its corner; the viewer's own
# team on the left. No names and no guns: a face and a pick say who somebody is.
#
# The block is exactly as tall as a face. The arena text and compass the game used to
# draw here are gone with the map type (see the README on the map), so there is
# nothing left to cover. The clock is set in a monospaced face because a proportional
# one made the whole readout shuffle every second; the small label under it says what
# the clock is counting.

BLOCK_W, BLOCK_H = 350, 50
SEAT = 50               # a face, the site's .userProfile chip
SEAT_GAP = 6
WING_PAD = 8            # between the block and the first face
SEATS = 5
WING_W = WING_PAD + SEATS * (SEAT + SEAT_GAP) - SEAT_GAP
HUD_W = BLOCK_W + 2 * WING_W
PICK = 22               # the pick icon hanging off a face
PICK_TILT = 14          # degrees; the one playful thing on the bar


def match_hud():
    root = Node("Effect")
    canvas(root, sorting_order=16)

    hud = root.add(top_center("Hud", 0, 0, HUD_W, BLOCK_H))

    block = hud.add(top_center("Block", 0, 0, BLOCK_W, BLOCK_H))
    image(block, FLAT, sprite=SPR["notch"])

    # The score line, left to right, each field vertically centred in the bar.
    x = 10
    text(block.add(top_left("Team0_Name", x, 0, 78, BLOCK_H)), "North", size=16, colour=INK, halign=RIGHT, bold=True)
    x += 82
    text(block.add(top_left("Score0", x, 0, 26, BLOCK_H)), "0", size=22, colour=INK, halign=CENTER, bold=True)
    x += 28
    role(block, "Shield0", "Swords0", x)
    x += 22
    text(block.add(top_left("Timer", x, 4, 66, 26)), "0:30", size=18, colour=INK, halign=CENTER, mono=True)
    text(block.add(top_left("Mid", x, 30, 66, 16)), "Round 1",
         size=9, colour=MUTED, halign=CENTER, valign=TOP, bold=True, uppercase=True, spacing=8)
    x += 68
    role(block, "Shield1", "Swords1", x)
    x += 22
    text(block.add(top_left("Score1", x, 0, 26, BLOCK_H)), "1", size=22, colour=INK, halign=CENTER, bold=True)
    x += 30
    text(block.add(top_left("Team1_Name", x, 0, 78, BLOCK_H)), "South", size=16, colour=INK, halign=LEFT, bold=True)

    # Faces run outward from the block on each side. A face is two pictures of the
    # same download, one at full strength and one at half, and the plugin shows the
    # half one while that player is out of the round: nothing a server can send sets
    # an alpha, but it can choose which of two images is on.
    for prefix, place, corner, tilt in (("L", top_left, 0, -PICK_TILT), ("R", top_right, 1, PICK_TILT)):
        for i in range(SEATS):
            x = WING_W - WING_PAD - (i + 1) * SEAT - i * SEAT_GAP
            seat = hud.add(place(f"{prefix}{i}", x, 0, SEAT, SEAT + PICK // 2, active=False))

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


def role(block, shield, swords, x):
    """The two role markers share a slot; the plugin shows one, or neither between rounds."""
    image(block.add(top_left(shield, x, (BLOCK_H - 20) // 2, 20, 20, active=False)), INK, 0.85, sprite=SPR["shield"], sliced=False)
    image(block.add(top_left(swords, x, (BLOCK_H - 20) // 2, 20, 20, active=False)), INK, 0.85, sprite=SPR["swords"], sliced=False)


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
         "Look at a piece and press <plugin_0/> to change what it is made of, or take it down.",
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

        x = 8
        text(fill.add(top_left(f"Row{i}_Killer", x, 0, NAME_W, FEED_H)), "Ferret", size=13, colour=INK, halign=RIGHT, bold=True)
        x += NAME_W + 4
        text(fill.add(top_left(f"Row{i}_Streak", x, 0, BADGE_W, FEED_H)), "II", size=11, colour=RED, halign=CENTER, bold=True)
        x += BADGE_W + 4
        image(fill.add(top_left(f"Row{i}_Weapon", x, 3, WEAPON_W, FEED_H - 6)), "#ffffff", enabled=False,
              preserve_aspect=True, material=SILHOUETTE)
        x += WEAPON_W + 4
        text(fill.add(top_left(f"Row{i}_Victim", x, 0, NAME_W, FEED_H)), "Homeless", size=13, colour=INK, halign=LEFT, bold=True)

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
