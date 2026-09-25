using System.Collections.Generic;
using System.Linq;
using System.Xml.Serialization;

namespace PeekSeekRaid.Core.Models;

/// A triangular piece a hexagonal base is handed, counted off the square piece it
/// stands in for: a triangle floor off the maple floor, a triangle roof off the maple
/// roof. Kept as a link rather than a number so the two kits cannot drift apart.
public class TrianglePiece {
    [XmlAttribute]
    public ushort ItemId;

    [XmlAttribute]
    public ushort SquareOf;

    public TrianglePiece() { }

    public TrianglePiece(ushort itemId, ushort squareOf) {
        ItemId = itemId;
        SquareOf = squareOf;
    }

    /// A hexagonal team's kit: every square piece at `squareShare` of its count, and
    /// every triangle at `triangleShare` of the count of the square piece it stands in
    /// for, both rounded down. A hexagon has fewer squares to build on and triangles
    /// for the rest, so it gets less of each rather than both in full.
    public static List<RaidItem> HexagonalKit(
        IEnumerable<RaidItem> squares, IEnumerable<TrianglePiece> triangles, float squareShare, float triangleShare) {
        List<RaidItem> kit = squares.Select(item => item.Scaled(squareShare)).ToList();

        foreach (TrianglePiece triangle in triangles) {
            byte square = squares.FirstOrDefault(item => item.ItemId == triangle.SquareOf)?.Amount ?? 0;
            kit.Add(new RaidItem(triangle.ItemId, square).Scaled(triangleShare));
        }

        return kit.Where(item => item.Amount > 0).ToList();
    }
}
