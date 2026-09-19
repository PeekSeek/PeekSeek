using System;
using System.Collections.Generic;
using System.Linq;
using PeekSeekSiege.Core.Models;
using Xunit;

namespace PeekSeekSiege.Tests;

/// The starting base has to sit on Unturned's housing grid to the centimetre, or the
/// pillars link to nothing and the first thing a player tries to attach refuses to go
/// down. These check the layouts against the numbers in HousingConnections.cs.
public class BaseLayoutTests {
    [Theory]
    [InlineData(BaseShape.Square, 9, 0)]
    [InlineData(BaseShape.Hexagonal, 6, 6)]
    public void EachShapeHasItsPieces(BaseShape shape, int squares, int triangles) {
        List<FloorPiece> pieces = BaseLayout.Floors(shape).ToList();

        Assert.Equal(squares, pieces.Count(p => !p.Triangle));
        Assert.Equal(triangles, pieces.Count(p => p.Triangle));
    }

    /// A three-by-three grid has sixteen corners. A hexagon of six triangles has seven
    /// (six round the edge and the one they all share in the middle), and six squares on
    /// its edges add twelve more, which would be wrong if a square were a hair off the
    /// triangle it sits on.
    [Theory]
    [InlineData(BaseShape.Square, 16)]
    [InlineData(BaseShape.Hexagonal, 19)]
    public void EveryCornerIsCountedOnce(BaseShape shape, int corners) {
        Assert.Equal(corners, BaseLayout.Vertices(shape).Count);
    }

    /// Every piece shares at least two corners with the rest of the base, which is
    /// another way of saying it shares an edge: nothing is placed loose.
    [Theory]
    [InlineData(BaseShape.Square)]
    [InlineData(BaseShape.Hexagonal)]
    public void EveryPieceSharesAnEdge(BaseShape shape) {
        List<FloorPiece> pieces = BaseLayout.Floors(shape).ToList();

        for (int i = 0; i < pieces.Count; i++) {
            List<(float X, float Z)> mine = BaseLayout.Corners(pieces[i]).ToList();
            int shared = mine.Count(corner => pieces.Where((_, j) => j != i)
                .SelectMany(BaseLayout.Corners)
                .Any(other => Near(other, corner)));

            Assert.True(shared >= 2, $"Piece {i} of {shape} shares {shared} corner(s).");
        }
    }

    /// The hexagon's outer corners are six metres from the middle, the length of a
    /// side, because six equilateral triangles of side six make a hexagon of side six.
    [Fact]
    public void TheHexagonIsSixMetresAcrossFromTheMiddle() {
        List<(float X, float Z, float Yaw)> vertices = BaseLayout.Vertices(BaseShape.Hexagonal);
        int atSix = vertices.Count(v => Math.Abs(Math.Sqrt(v.X * v.X + v.Z * v.Z) - BaseLayout.Edge) < BaseLayout.LinkTolerance);

        Assert.Equal(6, atSix);
        Assert.Contains(vertices, v => Math.Abs(v.X) < BaseLayout.LinkTolerance && Math.Abs(v.Z) < BaseLayout.LinkTolerance);
    }

    /// The numbers the game links on. If these drift from HousingConnections.cs, every
    /// pillar is a quarter metre off and nothing attaches to it.
    [Fact]
    public void TheGridIsUnturneds() {
        Assert.Equal(6f, BaseLayout.Edge);
        Assert.Equal(2.125f, BaseLayout.HalfWallHeight);
        // Circumradius minus the pivot's distance from the centroid: 2 root 3 minus (3 minus root 3).
        Assert.Equal(BaseLayout.TriangleApexPivotOffset, 3f * (float)Math.Sqrt(3) - 3f, 3);
    }

    private static bool Near((float X, float Z) a, (float X, float Z) b) {
        return Math.Abs(a.X - b.X) < BaseLayout.LinkTolerance && Math.Abs(a.Z - b.Z) < BaseLayout.LinkTolerance;
    }
}
