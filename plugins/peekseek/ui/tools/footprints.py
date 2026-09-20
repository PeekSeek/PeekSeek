"""The two footprints a base can start from, drawn from above for the board's tiles.

Each picture is the whole starting base as the plugin lays it: every floor on the
housing grid, a pillar on every corner, the flag in the middle. The geometry is
BaseLayout.cs in plain floats, and has to stay in step with it: a tile that shows a
base the plugin does not build is a lie a player finds out about thirty seconds later.

Floors are six metres to a side. A triangle's pivot is 2.196 from its apex on the line
to it, which puts its base edge three metres behind the pivot, the same as a square's
edge is from its centre. Yaw is Unity's, 0 facing +Z, and a piece's local +Z is
(sin yaw, cos yaw); north is up in the picture, so Z points up.
"""

import math
from io import BytesIO

from PIL import Image, ImageDraw

EDGE = 6.0
HALF = 3.0
APEX_PIVOT = 2.1961524
HEX_APOTHEM = 5.1961524
LINK = 0.02

# The board's tile grey and the site's greys, so the picture is part of the tile rather
# than a photo stuck on it.
FLOOR = (0xc8, 0xc8, 0xc8, 255)
SEAM = (0x1a, 0x1a, 0x1a, 255)
PILLAR = (0xf2, 0xf2, 0xf2, 255)
FLAG = (0xff, 0x00, 0x00, 255)
FLAG_RING = (0xff, 0xff, 0xff, 255)


def floors(shape):
    """(x, z, yaw, triangle) for every floor of a footprint."""
    if shape == "Square":
        return [(across * EDGE, along * EDGE, 0.0, False)
                for across in (-1, 0, 1) for along in (-1, 0, 1)]

    pieces = []
    for k in range(6):
        yaw = k * 60.0
        sin, cos = math.sin(math.radians(yaw)), math.cos(math.radians(yaw))
        pieces.append((-APEX_PIVOT * sin, -APEX_PIVOT * cos, yaw, True))
        out = HEX_APOTHEM + HALF
        pieces.append((-out * sin, -out * cos, yaw, False))
    return pieces


def corners(piece):
    x, z, yaw, triangle = piece
    sin, cos = math.sin(math.radians(yaw)), math.cos(math.radians(yaw))

    def world(lx, lz):
        return (x + lx * cos + lz * sin, z - lx * sin + lz * cos)

    if triangle:
        return [world(0, APEX_PIVOT), world(HALF, -HALF), world(-HALF, -HALF)]
    return [world(-HALF, -HALF), world(HALF, -HALF), world(HALF, HALF), world(-HALF, HALF)]


def vertices(shape):
    found = []
    for piece in floors(shape):
        for x, z in corners(piece):
            if not any(abs(vx - x) < LINK and abs(vz - z) < LINK for vx, vz in found):
                found.append((x, z))
    return found


def picture(shape, width, height, metres_across):
    """A PNG of the footprint, `metres_across` of ground fitted to `width` pixels, drawn
    at four times the size and shrunk so the edges are smooth."""
    scale = 4
    w, h = width * scale, height * scale
    px_per_m = w / metres_across
    image = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    draw = ImageDraw.Draw(image)

    def at(x, z):
        return (w / 2 + x * px_per_m, h / 2 - z * px_per_m)

    seam = max(1, round(0.25 * px_per_m))
    for piece in floors(shape):
        draw.polygon([at(x, z) for x, z in corners(piece)], fill=FLOOR, outline=SEAM, width=seam)

    pillar = 0.45 * px_per_m
    for x, z in vertices(shape):
        if abs(x) < LINK and abs(z) < LINK:
            continue
        cx, cy = at(x, z)
        draw.ellipse((cx - pillar, cy - pillar, cx + pillar, cy + pillar), fill=PILLAR)

    ring, dot = 0.9 * px_per_m, 0.6 * px_per_m
    cx, cy = at(0, 0)
    draw.ellipse((cx - ring, cy - ring, cx + ring, cy + ring), fill=FLAG_RING)
    draw.ellipse((cx - dot, cy - dot, cx + dot, cy + dot), fill=FLAG)

    out = BytesIO()
    image.resize((width, height), Image.LANCZOS).save(out, "PNG")
    return out.getvalue()
