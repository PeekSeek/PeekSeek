using System.Linq;
using PeekSeekRaid.Core.Models;
using Xunit;

namespace PeekSeekRaid.Tests;

public class PieceFamilyTests {
    private static PeekSeekRaidConfiguration Defaults() {
        PeekSeekRaidConfiguration config = new();
        config.LoadDefaults();
        return config;
    }

    /// A rampart or a window is still the maple wall the team paid for, so taking one
    /// down gives back a maple wall, and a hole gives back a maple roof.
    [Fact]
    public void TakingAFormDownGivesBackWhatItCounts() {
        PeekSeekRaidConfiguration config = Defaults();

        Assert.Equal(33, PieceFamily.RefundFor(config.PieceFamilies, 442));
        Assert.Equal(33, PieceFamily.RefundFor(config.PieceFamilies, 34));
        Assert.Equal(35, PieceFamily.RefundFor(config.PieceFamilies, 319));
    }

    /// Metal is only itself: no metal window, no metal rampart.
    [Fact]
    public void MetalHasNoOtherShapes() {
        PeekSeekRaidConfiguration config = Defaults();

        foreach (ushort metal in new ushort[] { 370, 371, 373 }) {
            Assert.Null(PieceFamily.Of(config.PieceFamilies, metal));
            Assert.Equal(metal, PieceFamily.RefundFor(config.PieceFamilies, metal));
        }
    }

    /// Every family counts as something in the kit, and the window glass goes into is
    /// a shape a wall can take.
    [Fact]
    public void EveryFamilyCountsAsAKitPiece() {
        PeekSeekRaidConfiguration config = Defaults();

        foreach (PieceFamily family in config.PieceFamilies) {
            Assert.Contains(config.BuildSupplies, item => item.ItemId == family.Counts);
            Assert.Contains(family.Counts, family.Forms);
        }
        Assert.Contains(config.PieceFamilies, family => family.Forms.Contains(config.WindowItemId));
    }

    /// Doors come with doorways, so nobody is ever handed one to build with.
    [Fact]
    public void DoorsAreNotInTheKit() {
        PeekSeekRaidConfiguration config = Defaults();

        Assert.DoesNotContain(config.BuildSupplies, item => item.ItemId == config.DoorItemId);
        Assert.Equal(6, config.BuildSupplies.Where(item => item.ItemId is 32 or 370).Sum(item => item.Amount));
    }

    /// A hexagon gets 0.7 of every square piece and 0.9 of the square floors and roofs
    /// again as triangles, rounded down.
    [Fact]
    public void TheHexagonKitIsTheSquareKitScaled() {
        PeekSeekRaidConfiguration config = Defaults();

        var kit = TrianglePiece.HexagonalKit(config.BuildSupplies, config.TriangleSupplies, 0.7f, 0.9f)
            .ToDictionary(item => item.ItemId, item => (int)item.Amount);

        Assert.Equal(44, kit[33]);
        Assert.Equal(2, kit[370]);
        Assert.Equal(8, kit[31]);
        Assert.Equal(22, kit[35]);
        Assert.Equal(10, kit[1262]);
        Assert.Equal(28, kit[1266]);
    }

    /// Every raider past the first is a full share of metal, half a share of wood, and
    /// nothing on a doorway; a 1v1 is the kit as written.
    [Fact]
    public void TheKitGrowsWithTheTeamThatRaidsIt() {
        RaidItem[] kit = [new RaidItem(371, 12), new RaidItem(33, 64), new RaidItem(370, 3)];
        double Extra(ushort id) => id switch { 371 => 1.0, 33 => 0.5, _ => 0.0 };

        Assert.Equal(new byte[] { 12, 64, 3 }, TeamSize.ForEnemies(kit, 1, Extra).Select(item => item.Amount));
        Assert.Equal(new byte[] { 24, 96, 3 }, TeamSize.ForEnemies(kit, 2, Extra).Select(item => item.Amount));
        Assert.Equal(new byte[] { 36, 128, 3 }, TeamSize.ForEnemies(kit, 3, Extra).Select(item => item.Amount));
        Assert.Equal(new byte[] { 12, 64, 3 }, TeamSize.ForEnemies(kit, 0, Extra).Select(item => item.Amount));
    }
}
