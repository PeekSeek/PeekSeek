"""Write the two screens PeekSeek draws in game, as Unity prefabs and sprites.

    python generate.py

Overwrites Assets/PeekSeek/Bundles/Effects/PickBoard/Effect.prefab,
Assets/PeekSeek/Bundles/Effects/MatchHud/Effect.prefab, and Assets/PeekSeek/Sprites/*.
If a screen is ever changed inside Unity instead, that edit wins and this file is stale;
bring the change back here or delete this script, but do not leave the two disagreeing.

Two screens, two design languages, on purpose. The pick board is something you click,
so it is built like a page of the site: the same panel, the same selectable tile, the
same px. The match HUD is composited over play like the stream overlay, so it is built
like that: one flat fill, greyscale, red only for jeopardy, the notch with its inverse
fillets, the card with a name line over a loadout run. Sizes are the site's px and the
overlay's vw at 1920, which is what the game draws at UI scale 1.

The font is Inter, because the site asks for the system UI font and Inter is the open
one that reads the same; the game leaves a font that is not TextMeshPro's own alone.
"""

from pathlib import Path

from unityyaml import (BOTTOM, CENTER, LEFT, MIDDLE, RIGHT, TOP, Node, bottom_left, button, canvas,
                       centered, flare_alpha, image, mask, prefab, rounded_alpha, sprite, stretch,
                       text, top_center, top_left, top_right, top_stretch)

ROOT = Path(__file__).resolve().parent.parent / "Assets/PeekSeek"
SPRITES = ROOT / "Sprites"
EFFECTS = ROOT / "Bundles/Effects"

# ---- the site, for the board (website/public/assets/default.css) --------------------
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
FLAT = "#0f0f0f"        # .flat, at 0.9
INK = "#f2f2f2"
MUTED = "#8a8a8a"
RED = "#ff4f4f"         # jeopardy only
TEAM_NAME = "#d8d8d8"   # .notch .tn
NOTCH_R = 13            # --notch-r 0.7vw
CARD_R = 6              # .card 0.3vw
CARD_TEXT = 16          # --card-text 0.82vw

OPTIONS = 5
SEATS = 5


def write_sprites():
    SPRITES.mkdir(parents=True, exist_ok=True)
    round8 = sprite(SPRITES, "Round8", "b1f0c2d3e4a5b6c7d8e9f0a1b2c3d4e5", 32, 32, rounded_alpha(32, 32, 8), 8)
    round6 = sprite(SPRITES, "Round6", "c2a1d3e4f5b6c7d8e9f0a1b2c3d4e5f6", 24, 24, rounded_alpha(24, 24, 6), 6)
    notch = sprite(SPRITES, "Notch", "d3b2e4f5a6c7d8e9f0a1b2c3d4e5f6a7", 40, 40,
                   rounded_alpha(40, 40, NOTCH_R, corners=("bl", "br")), NOTCH_R)
    flare_l = sprite(SPRITES, "FlareL", "e4c3f5a6b7d8e9f0a1b2c3d4e5f6a7b8", NOTCH_R, NOTCH_R,
                     flare_alpha(NOTCH_R, NOTCH_R), 0)
    flare_r = sprite(SPRITES, "FlareR", "f5d4a6b7c8e9f0a1b2c3d4e5f6a7b8c9", NOTCH_R, NOTCH_R,
                     flare_alpha(NOTCH_R, 0), 0)
    return round8, round6, notch, flare_l, flare_r


# ---- the pick board ------------------------------------------------------------------

TILE_W, TILE_H = 200, 220
BOARD_W = PANEL_PAD * 2 + OPTIONS * TILE_W + (OPTIONS - 1) * GAP
HEADLINE_H = 28
HINT_H = 18
TILES_Y = PANEL_PAD + HEADLINE_H + 16
BOARD_H = TILES_Y + TILE_H + 14 + HINT_H + PANEL_PAD


def pick_board(round8):
    root = Node("Effect")
    # Under the game's own canvas (15), so the pause menu covers the board and not the
    # other way round. The cursor is at 30000 and always on top.
    canvas(root, sorting_order=0)

    board = root.add(centered("Board", 0, 40, BOARD_W, BOARD_H))
    image(board, PAGE, 0.94, sprite=round8)

    text(board.add(top_left("Headline", PANEL_PAD, PANEL_PAD, 880, HEADLINE_H)),
         "Round 2. You are defending North.", size=20, colour=TEXT, bold=True)
    text(board.add(top_right("Timer", PANEL_PAD, 16, 120, 36)),
         "20", size=28, colour=TEXT, halign=RIGHT, bold=True)
    text(board.add(bottom_left("Hint", PANEL_PAD, PANEL_PAD - 2, 900, HINT_H)),
         "Click a tile, or type /pick and its number.", size=13, colour=SECONDARY)

    for i in range(OPTIONS):
        tile = board.add(top_left(f"SiegePick{i}", PANEL_PAD + i * (TILE_W + GAP), TILES_Y, TILE_W, TILE_H))
        button(tile, image(tile, "#ffffff", sprite=round8, raycast=True), SUB, SUB_HOVER, SELECTED)

        # First child, so it paints under everything else on the tile: the site's
        # selected state is a fill and a label, not a border.
        taken = tile.add(stretch(f"Option{i}_Taken", active=False))
        image(taken, SELECTED, sprite=round8)
        text(taken.add(top_right("Label", GAP, 8, 80, 16)), "Selected",
             size=12, colour=SELECTED_LABEL, halign=RIGHT, bold=True)

        text(tile.add(top_left(f"Option{i}_Number", GAP, 8, 40, 16)), str(i + 1),
             size=13, colour=SECONDARY, bold=True)

        icon = tile.add(top_center(f"Option{i}_Icon", 0, 26, 96, 84))
        image(icon, "#ffffff", enabled=False, preserve_aspect=True)

        text(tile.add(top_stretch(f"Option{i}_Name", 118, 22, 12)), "Rocket Launcher",
             size=16, colour=TEXT, halign=CENTER, bold=True)
        text(tile.add(top_stretch(f"Option{i}_Blurb", 144, 58, 14)),
             "Two rockets. Opens brick, and nothing else you carry will.",
             size=13, colour=SECONDARY, halign=CENTER, valign=TOP, wrap=True)

    return root


# ---- the match HUD -------------------------------------------------------------------
#
# One block, not a notch with cards around it. It sits exactly where Unturned's own
# arena text (y 0-50, x +-180) and compass (y 60-110) are drawn on an Arena map, at a
# canvas sort order above the game's (15), so it covers both: there is no flag or config
# that turns either off on an Arena level. The shape is still the overlay's notch, only
# wide enough for two rosters, with the inverse fillets at its ends.

HUD_H = 112
HUD_PAD = 16
SEAT_W = 36             # a 32px face and a 4px gap
AVATAR = 32
ROSTER_W = SEATS * SEAT_W - 4
CENTER_W = 140
HUD_W = HUD_PAD * 2 + ROSTER_W * 2 + 12 * 2 + CENTER_W
ROW2_Y = 58


def match_hud(round6, notch_sprite, flare_l, flare_r):
    root = Node("Effect")
    canvas(root, sorting_order=16)

    block = root.add(top_center("Block", 0, 0, HUD_W, HUD_H))
    image(block, FLAT, 0.9, sprite=notch_sprite)

    flare_left = block.add(Node("FlareL", anchor_min=(0, 1), anchor_max=(0, 1), pivot=(1, 1), size=(NOTCH_R, NOTCH_R)))
    image(flare_left, FLAT, 0.9, sprite=flare_l)
    flare_right = block.add(Node("FlareR", anchor_min=(1, 1), anchor_max=(1, 1), pivot=(0, 1), size=(NOTCH_R, NOTCH_R)))
    image(flare_right, FLAT, 0.9, sprite=flare_r)

    # Row one: names over each roster, scores and the clock in the middle.
    center_x = HUD_PAD + ROSTER_W + 12
    text(block.add(top_left("Team0_Name", HUD_PAD, 8, ROSTER_W, 40)), "North",
         size=18, colour=TEAM_NAME, halign=RIGHT, bold=True)
    text(block.add(top_left("Score0", center_x, 8, 40, 40)), "0",
         size=28, colour=INK, halign=CENTER, bold=True)
    text(block.add(top_left("Timer", center_x + 40, 8, 60, 40)), "0:30",
         size=24, colour=INK, halign=CENTER, bold=True)
    text(block.add(top_left("Score1", center_x + 100, 8, 40, 40)), "0",
         size=28, colour=INK, halign=CENTER, bold=True)
    text(block.add(top_left("Team1_Name", center_x + CENTER_W + 12, 8, ROSTER_W, 40)), "South",
         size=18, colour=TEAM_NAME, halign=LEFT, bold=True)

    # Row two: the round label under the clock, a seat per player under each name.
    text(block.add(top_left("Mid", center_x, ROW2_Y + 4, CENTER_W, 16)), "Round 1",
         size=12, colour=MUTED, halign=CENTER, bold=True, uppercase=True, spacing=10)

    for prefix, place in (("L", top_left), ("R", top_right)):
        for i in range(SEATS):
            x = HUD_PAD + i * SEAT_W
            seat = block.add(place(f"{prefix}{i}", x, ROW2_Y, AVATAR, HUD_H - ROW2_Y - 6, active=False))

            frame = seat.add(top_left(f"{prefix}{i}_Frame", 0, 0, AVATAR, AVATAR))
            image(frame, "#ffffff", sprite=round6)
            mask(frame)
            image(frame.add(stretch(f"{prefix}{i}_Avatar")), "#ffffff", enabled=False)

            # The loadout run, gun then pick, under the face.
            image(seat.add(top_left(f"{prefix}{i}_Gun", 0, AVATAR + 2, 16, 14)), "#ffffff", enabled=False, preserve_aspect=True)
            image(seat.add(top_left(f"{prefix}{i}_Pick", 18, AVATAR + 2, 14, 14)), "#ffffff", enabled=False, preserve_aspect=True)

            # Out of the round: the overlay fades the card rather than removing it.
            image(seat.add(stretch(f"{prefix}{i}_Out", active=False)), FLAT, 0.7)

        # A hairline under each roster. The viewer's own side is ink, the other side is
        # the overlay's red: for a player, the other team is the jeopardy.
        rule = block.add(place(f"{prefix}_Rule", HUD_PAD, HUD_H - 4, ROSTER_W, 2))
        image(rule, INK if prefix == "L" else RED, 0.5 if prefix == "L" else 0.85)

    return root


if __name__ == "__main__":
    round8, round6, notch, flare_l, flare_r = write_sprites()

    for name, root in (("PickBoard", pick_board(round8)), ("MatchHud", match_hud(round6, notch, flare_l, flare_r))):
        target = EFFECTS / name / "Effect.prefab"
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_text(prefab(root), encoding="utf-8", newline="\n")
        print(f"wrote {target}")
