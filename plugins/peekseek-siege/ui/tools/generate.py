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
                       centered, flare_alpha, image, mask, prefab, rounded_alpha, shield_alpha, sprite,
                       stretch, swords_alpha, text, top_center, top_left, top_right, top_stretch)

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
    wing_l = sprite(SPRITES, "WingL", "a6e5b7c8d9f0a1b2c3d4e5f6a7b8c9d0", 32, 32, rounded_alpha(32, 32, 8, corners=("bl",)), 8)
    wing_r = sprite(SPRITES, "WingR", "b7f6c8d9e0a1b2c3d4e5f6a7b8c9d0e1", 32, 32, rounded_alpha(32, 32, 8, corners=("br",)), 8)
    shield = sprite(SPRITES, "Shield", "c8a7d9e0f1b2c3d4e5f6a7b8c9d0e1f2", 24, 24, shield_alpha(24), 0)
    swords = sprite(SPRITES, "Swords", "d9b8e0f1a2c3d4e5f6a7b8c9d0e1f2a3", 24, 24, swords_alpha(24), 0)
    return {
        "round8": round8, "round6": round6, "notch": notch, "flare_l": flare_l, "flare_r": flare_r,
        "wing_l": wing_l, "wing_r": wing_r, "shield": shield, "swords": swords,
    }


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
# The concept (conceptArtOverlay.png): one bar across the top. Faces on the wings, the
# pick hanging under each; `team 0 [shield] 0:30 [swords] 1 team2` in the middle, the
# shield beside whoever holds the flag this round and the swords beside whoever is
# after it. No names: a face and a gun say who somebody is faster than a name does.
#
# The middle section reaches down to 112px because Unturned draws its arena text (y
# 0-50, x +-180) and compass (y 60-110) there on every Arena map and nothing a server
# sends can switch them off; at a canvas sort order above the game's (15) this covers
# both. The round label lives in that apron so the depth is not for nothing.

BAR_H = 44
APRON_W, APRON_H = 420, 112
SEAT = 44
SEAT_GAP = 4
SEAT_H = 64             # a face and the loadout run under it
WING_PAD = 8
WING_W = WING_PAD + SEATS * (SEAT + SEAT_GAP) - SEAT_GAP + WING_PAD
HUD_W = APRON_W + 2 * WING_W


def match_hud(sprites):
    root = Node("Effect")
    canvas(root, sorting_order=16)

    block = root.add(top_center("Block", 0, 0, HUD_W, APRON_H))

    apron = block.add(top_center("Apron", 0, 0, APRON_W, APRON_H))
    image(apron, FLAT, 0.9, sprite=sprites["notch"])

    left = block.add(top_left("WingL", 0, 0, WING_W, BAR_H))
    image(left, FLAT, 0.9, sprite=sprites["wing_l"])
    right = block.add(top_right("WingR", 0, 0, WING_W, BAR_H))
    image(right, FLAT, 0.9, sprite=sprites["wing_r"])

    flare_left = block.add(Node("FlareL", anchor_min=(0, 1), anchor_max=(0, 1), pivot=(1, 1), size=(NOTCH_R, NOTCH_R)))
    image(flare_left, FLAT, 0.9, sprite=sprites["flare_l"])
    flare_right = block.add(Node("FlareR", anchor_min=(1, 1), anchor_max=(1, 1), pivot=(0, 1), size=(NOTCH_R, NOTCH_R)))
    image(flare_right, FLAT, 0.9, sprite=sprites["flare_r"])

    # The middle, left to right, all in the bar's height.
    x = 11
    text(apron.add(top_left("Team0_Name", x, 0, 100, BAR_H)), "team", size=16, colour=INK, halign=RIGHT, bold=True)
    x += 106
    text(apron.add(top_left("Score0", x, 0, 30, BAR_H)), "0", size=22, colour=INK, halign=CENTER, bold=True)
    x += 34
    image(apron.add(top_left("Shield0", x, 12, 20, 20, active=False)), INK, 0.85, sprite=sprites["shield"])
    image(apron.add(top_left("Swords0", x, 12, 20, 20, active=False)), INK, 0.85, sprite=sprites["swords"])
    x += 24
    text(apron.add(top_left("Timer", x, 0, 70, BAR_H)), "0:30", size=20, colour=INK, halign=CENTER, bold=True)
    x += 74
    image(apron.add(top_left("Shield1", x, 12, 20, 20, active=False)), INK, 0.85, sprite=sprites["shield"])
    image(apron.add(top_left("Swords1", x, 12, 20, 20, active=False)), INK, 0.85, sprite=sprites["swords"])
    x += 24
    text(apron.add(top_left("Score1", x, 0, 30, BAR_H)), "0", size=22, colour=INK, halign=CENTER, bold=True)
    x += 34
    text(apron.add(top_left("Team1_Name", x, 0, 100, BAR_H)), "team2", size=16, colour=INK, halign=LEFT, bold=True)

    text(apron.add(top_stretch("Mid", 66, 16, 20)), "Round 1",
         size=11, colour=MUTED, halign=CENTER, bold=True, uppercase=True, spacing=10)

    # Seats run outward from the middle on each wing: a face in the bar, the gun and
    # the pick hanging under it. Out of the round fades the whole seat.
    for prefix, place in (("L", top_left), ("R", top_right)):
        for i in range(SEATS):
            x = WING_PAD + i * (SEAT + SEAT_GAP)
            seat = block.add(place(f"{prefix}{i}", (WING_W - x - SEAT), 0, SEAT, SEAT_H, active=False))

            frame = seat.add(top_left(f"{prefix}{i}_Frame", 0, 0, SEAT, SEAT))
            image(frame, "#ffffff", sprite=sprites["round6"])
            mask(frame)
            image(frame.add(stretch(f"{prefix}{i}_Avatar")), "#ffffff", enabled=False)

            image(seat.add(place(f"{prefix}{i}_Gun", 0, SEAT + 2, 24, 16)), "#ffffff", enabled=False, preserve_aspect=True)
            image(seat.add(place(f"{prefix}{i}_Pick", 26, SEAT + 1, 18, 18)), "#ffffff", enabled=False, preserve_aspect=True)

            image(seat.add(stretch(f"{prefix}{i}_Out", active=False)), FLAT, 0.7)

    return root


if __name__ == "__main__":
    sprites = write_sprites()

    for name, root in (("PickBoard", pick_board(sprites["round8"])), ("MatchHud", match_hud(sprites))):
        target = EFFECTS / name / "Effect.prefab"
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_text(prefab(root), encoding="utf-8", newline="\n")
        print(f"wrote {target}")
