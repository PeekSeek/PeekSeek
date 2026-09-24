using System;
using System.Collections.Generic;

namespace PeekSeekRaid.Core.Models;

public enum BaseShape {
    /// Nine square floors, three by three.
    Square,

    /// Six triangles meeting in the middle, and a square on each of the six edges.
    Hexagonal
}

/// One floor piece of a starting base, relative to the site anchor: where its pivot
/// goes, which way it faces, and whether it is a triangle.
public struct FloorPiece {
    public float X, Z, Yaw;
    public bool Triangle;
}

/// A starting base as Unturned's housing grid sees it, so every piece laid here links
/// to every piece a player lays after.
///
/// Everything is HousingConnections.cs in the U3-SDK, in plain floats so a test can read
/// it without Unity: floors are six metres to a side and link within two centimetres,
/// a pillar stands at a floor vertex half a wall (2.125) above the floor's plane, and a
/// triangle's pivot is not its centroid but a point 2.196 from its apex on the line
/// to it, which puts its base edge exactly three metres behind the pivot, the same as
/// a square's edge is from its centre. That last fact is what makes a triangle on a
/// square's edge sit one full edge length from the square's centre, same yaw.
///
/// Yaw is Unity's: 0 faces +Z, and a piece's local +Z is (sin yaw, cos yaw). For a
/// triangle, local +Z points at the apex.
public static class BaseLayout {
    public const float Edge = 6f;
    public const float HalfEdge = 3f;
    public const float HalfWallHeight = 2.125f;

    /// A storey: a pillar is a wall tall, and the next one stacks exactly this much
    /// higher, on the same vertex.
    public const float WallHeight = 4.25f;
    public const float TriangleApexPivotOffset = 2.1961524f;
    public const float LinkTolerance = 0.02f;

    /// The apothem of the hexagon six triangles make: the height of one of them.
    private const float HexApothem = 5.1961524f;

    public static IEnumerable<FloorPiece> Floors(BaseShape shape) {
        if (shape == BaseShape.Square) {
            for (int across = -1; across <= 1; across++) {
                for (int along = -1; along <= 1; along++) {
                    yield return new FloorPiece { X = across * Edge, Z = along * Edge, Yaw = 0f };
                }
            }
            yield break;
        }

        for (int k = 0; k < 6; k++) {
            float yaw = k * 60f;
            (float sin, float cos) = Direction(yaw);

            // A triangle whose apex is the middle: its pivot sits 2.196 back from the
            // apex along the direction it faces, and it faces the middle.
            yield return new FloorPiece {
                X = -TriangleApexPivotOffset * sin, Z = -TriangleApexPivotOffset * cos, Yaw = yaw, Triangle = true
            };

            // A square on that triangle's base edge, facing the middle too, its near
            // edge on the hexagon's edge.
            float squareOut = HexApothem + HalfEdge;
            yield return new FloorPiece { X = -squareOut * sin, Z = -squareOut * cos, Yaw = yaw };
        }
    }

    /// Every distinct corner of every floor, which is where a pillar can stand. A corner
    /// shared by several floors is listed once; the game links them within two
    /// centimetres, so anything closer than that is the same corner.
    public static List<(float X, float Z, float Yaw)> Vertices(BaseShape shape) {
        List<(float X, float Z, float Yaw)> found = [];

        foreach (FloorPiece piece in Floors(shape)) {
            foreach ((float x, float z) in Corners(piece)) {
                bool known = found.Exists(v => Math.Abs(v.X - x) < LinkTolerance && Math.Abs(v.Z - z) < LinkTolerance);
                if (!known) found.Add((x, z, piece.Yaw));
            }
        }

        return found;
    }

    public static IEnumerable<(float X, float Z)> Corners(FloorPiece piece) {
        (float sin, float cos) = Direction(piece.Yaw);

        // Local (x, z) to world: x along the piece's right, z along its facing.
        (float, float) World(float lx, float lz) => (piece.X + lx * cos + lz * sin, piece.Z - lx * sin + lz * cos);

        if (piece.Triangle) {
            yield return World(0f, TriangleApexPivotOffset);
            yield return World(-HalfEdge, -HalfEdge);
            yield return World(HalfEdge, -HalfEdge);
            yield break;
        }

        yield return World(-HalfEdge, -HalfEdge);
        yield return World(HalfEdge, -HalfEdge);
        yield return World(-HalfEdge, HalfEdge);
        yield return World(HalfEdge, HalfEdge);
    }

    private static (float sin, float cos) Direction(float yaw) {
        double radians = yaw * Math.PI / 180d;
        return ((float)Math.Sin(radians), (float)Math.Cos(radians));
    }
}
