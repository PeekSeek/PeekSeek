"""RAID's pick boards: the defender's class grid and the attacker's under the map, and
the six class glyphs they carry.

A RAID board is every powerup a side has, five by two, each tile its class glyph in
front of its name. The plugin addresses:

    Board{face}{i}                   the tile, a button; i = row * 5 + column
    {face}{i}_Name, _Blurb           text
    {face}{i}_Item{k}, _Icon, _Count the items, in a row
    {face}{i}_Glyph{g}               one of the six glyphs, shown by the plugin
    {face}{i}_Taken                  the viewer's own pick, lit
    {face}{i}_Claimed, _Claimer      somebody else's, greyed, with their face

A glyph cannot be sent as a picture from the server without the site serving it, so
every glyph is on every tile and the plugin shows the one it means.
"""

import re
from io import BytesIO

from PIL import Image, ImageDraw

# The sheet's glyphs, in its own 100 by 106 box, one per class in PowerupClass order.
GLYPHS = (
    (
        "Destruction",
        "fill",
        "M50 31l4.5 11 11-5-5 11 11 4.5-11 4.5 5 11-11-5-4.5 11-4.5-11-11 5 5-11-11-4.5 11-4.5-5-11 11 5z",
    ),
    ("Initializer", "chevrons", None),
    ("Support", "crosshair", None),
    (
        "Special",
        "fill",
        "M50 31c1.8 11.5 7.5 17.2 19 19-11.5 1.8-17.2 7.5-19 19-1.8-11.5-7.5-17.2-19-19 11.5-1.8 17.2-7.5 19-19z",
    ),
    (
        "Armorer",
        "fill",
        "M50 34l16 6v11c0 10-6.6 17.4-16 20.5C40.6 68.4 34 61 34 51V40z",
    ),
    ("Warden", "plus", None),
)

# The part of that box the glyphs use, so the sprite is all glyph.
BOX = (28.0, 30.0, 72.0, 76.0)


def _path(d):
    """The points of an SVG path of M, l, v, V, c, C and z, curves sampled."""
    tokens = re.findall(r"[MlvVcCz]|-?\d*\.?\d+", d)
    points, x, y, i, command = [], 0.0, 0.0, 0, None

    def number():
        nonlocal i
        i += 1
        return float(tokens[i - 1])

    while i < len(tokens):
        if re.match(r"[A-Za-z]", tokens[i]):
            command = tokens[i]
            i += 1
            if command == "z":
                continue
        if command == "M":
            x, y = number(), number()
            points.append((x, y))
            command = "l"
        elif command == "l":
            x, y = x + number(), y + number()
            points.append((x, y))
        elif command == "v":
            y += number()
            points.append((x, y))
        elif command == "V":
            y = number()
            points.append((x, y))
        elif command in ("c", "C"):
            c = [number() for _ in range(6)]
            if command == "c":
                c = [c[0] + x, c[1] + y, c[2] + x, c[3] + y, c[4] + x, c[5] + y]
            x0, y0 = x, y
            for step in range(1, 13):
                t = step / 12
                u = 1 - t
                points.append(
                    (
                        u**3 * x0
                        + 3 * u * u * t * c[0]
                        + 3 * u * t * t * c[2]
                        + t**3 * c[4],
                        u**3 * y0
                        + 3 * u * u * t * c[1]
                        + 3 * u * t * t * c[3]
                        + t**3 * c[5],
                    )
                )
            x, y = c[4], c[5]
    return points


def glyph_png(index, size):
    """One class glyph, white on nothing, drawn four times over and scaled down."""
    big = size * 4
    scale = big / max(BOX[2] - BOX[0], BOX[3] - BOX[1])
    image = Image.new("RGBA", (big, big), (255, 255, 255, 0))
    draw = ImageDraw.Draw(image)

    def at(px, py):
        return ((px - BOX[0]) * scale, (py - BOX[1]) * scale)

    def line(points, width):
        draw.line(
            [at(*p) for p in points],
            fill="white",
            width=round(width * scale),
            joint="curve",
        )
        for p in (points[0], points[-1]):
            r = width * scale / 2
            cx, cy = at(*p)
            draw.ellipse((cx - r, cy - r, cx + r, cy + r), fill="white")

    _, kind, d = GLYPHS[index]
    if kind == "fill":
        draw.polygon([at(*p) for p in _path(d)], fill="white")
    elif kind == "chevrons":
        line([(38, 52), (50, 40), (62, 52)], 6)
        line([(38, 66), (50, 54), (62, 66)], 6)
    elif kind == "crosshair":
        cx, cy = at(50, 53)
        r, w = 12 * scale, 4.5 * scale
        draw.ellipse(
            (cx - r - w / 2, cy - r - w / 2, cx + r + w / 2, cy + r + w / 2),
            outline="white",
            width=round(w),
        )
        for a, b in (
            ((50, 34), (50, 42)),
            ((50, 64), (50, 72)),
            ((31, 53), (39, 53)),
            ((61, 53), (69, 53)),
        ):
            line([a, b], 4.5)
        d = 3.5 * scale
        draw.ellipse((cx - d, cy - d, cx + d, cy + d), fill="white")
    elif kind == "plus":
        line([(50, 38), (50, 68)], 8)
        line([(35, 53), (65, 53)], 8)

    out = BytesIO()
    image.resize((size, size), Image.LANCZOS).save(out, "PNG", optimize=True)
    return out.getvalue()


GLYPH_GUIDS = (
    "a1e7c2d9b3f84a6e9d0c1b2a3f4e5d60",
    "b2f8d3e0c4a95b7f0e1d2c3b4a5f6e71",
    "c3a9e4f1d5b06c80f1e2d3c4b5a6f782",
    "d4b0f5a2e6c17d91a2f3e4d5c6b7a893",
    "e5c1a6b3f7d28ea2b3a4f5e6d7c8b9a4",
    "f6d2b7c4a8e39fb3c4b5a6f7e8d9cab5",
)
