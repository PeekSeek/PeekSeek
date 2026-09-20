using System.Linq;
using PeekSeekRaid.Core.Models;
using Xunit;

namespace PeekSeekRaid.Tests;

public class BudgetTests {
    private static Budget Kit() => new([new RaidItem(33, 2), new RaidItem(36, 1), new RaidItem(33, 1)]);

    [Fact]
    public void TheSameIdListedTwiceIsOneRow() {
        Budget budget = Kit();

        Assert.Equal(3, budget.Left(33));
        Assert.Equal([(33, 3), (36, 1)], budget.Rows.Select(row => ((int)row.Id, row.Count)).ToArray());
    }

    [Fact]
    public void SpendingRunsOutAndRefuses() {
        Budget budget = Kit();

        Assert.True(budget.Spend(36));
        Assert.False(budget.Spend(36));
        Assert.False(budget.Has(36));
        Assert.Equal(0, budget.Left(36));
    }

    /// A metal floor nobody was issued, refunded off a swapped starting tile, becomes
    /// a row of its own at the end of the list.
    [Fact]
    public void ARefundOfSomethingNewJoinsTheEnd() {
        Budget budget = Kit();
        budget.Refund(369);

        Assert.Equal(1, budget.Left(369));
        Assert.Equal(369, budget.Ids.Last());
    }

    [Fact]
    public void NothingIsNegative() {
        Budget budget = new([]);

        Assert.False(budget.Spend(1));
        Assert.Equal(0, budget.Left(1));
    }
}

public class MaterialsTests {
    [Theory]
    [InlineData("Birch Wall", "Birch", "Wall")]
    [InlineData("Triangular Metal Floor", "Metal", "Triangular Floor")]
    [InlineData("Pine Ramp Wall", "Pine", "Ramp Wall")]
    [InlineData("Maple Hole", "Maple", "Hole")]
    public void TheMaterialIsOneWordAnywhereInTheName(string name, string material, string kind) {
        (string Material, string Kind)? split = Materials.Split(name);

        Assert.NotNull(split);
        Assert.Equal(material, split.Value.Material);
        Assert.Equal(kind, split.Value.Kind);
    }

    /// Brick is a housing material to the game and not to Raid: a brick wall is four
    /// charges, so it is not in the kit and never on the swap board.
    [Theory]
    [InlineData("Housing Planner")]
    [InlineData("Brick Wall")]
    [InlineData("")]
    public void ANameWithoutAMaterialIsNotAPiece(string name) {
        Assert.Null(Materials.Split(name));
    }

    /// "Metal Door" is a barricade with a material in its name. The name alone cannot
    /// tell it from a piece, and does not have to: it is never the same kind as a wall,
    /// and the swap board only ever looks at structures anyway.
    [Fact]
    public void SameKindIgnoresTheMaterial() {
        Assert.True(Materials.SameKind("Birch Wall", "Metal Wall"));
        Assert.True(Materials.SameKind("Triangular Metal Floor", "Triangular Pine Floor"));
        Assert.False(Materials.SameKind("Metal Wall", "Metal Door"));
        Assert.False(Materials.SameKind("Metal Floor", "Metal Roof"));
        Assert.False(Materials.SameKind("Metal Wall", "Housing Planner"));
    }
}
