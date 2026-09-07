using PeekSeekSiege.Core;
using Xunit;

namespace PeekSeekSiege.Tests;

public class RoundOrderTests {
    [Theory]
    [InlineData(1, 0)]
    [InlineData(2, 0)]
    [InlineData(3, 0)]
    [InlineData(4, 1)]
    [InlineData(5, 1)]
    [InlineData(6, 1)]
    public void RegulationIsThreeEachWay(int round, int defender) {
        Assert.Equal(defender, RoundOrder.Defender(round));
    }

    /// The first overtime block is two rounds for team 1 then two for team 0, so the
    /// team that defended last in regulation does not also defend first in overtime.
    [Theory]
    [InlineData(7, 1)]
    [InlineData(8, 1)]
    [InlineData(9, 0)]
    [InlineData(10, 0)]
    [InlineData(11, 0)]
    [InlineData(12, 0)]
    [InlineData(13, 1)]
    [InlineData(14, 1)]
    public void OvertimeAlternatesWhoDefendsFirst(int round, int defender) {
        Assert.Equal(defender, RoundOrder.Defender(round));
    }

    /// Overtime never runs out. A match that stays level keeps adding blocks.
    [Fact]
    public void OvertimeHasNoEnd() {
        for (int round = 7; round < 500; round++) {
            int defender = RoundOrder.Defender(round);
            Assert.InRange(defender, 0, 1);
        }
    }

    /// Both sides get exactly half of every block, which is the whole reason blocks
    /// exist. Checked across many blocks rather than just the first.
    [Theory]
    [InlineData(7, 10)]
    [InlineData(11, 14)]
    [InlineData(23, 26)]
    public void EveryOvertimeBlockIsSplitEvenly(int first, int last) {
        int zero = 0;
        for (int round = first; round <= last; round++) {
            if (RoundOrder.Defender(round) == 0) zero++;
        }

        Assert.Equal(RoundOrder.OvertimeHalf, zero);
    }

    [Theory]
    [InlineData(1, false)]
    [InlineData(5, false)]
    [InlineData(6, true)]
    [InlineData(7, false)]
    [InlineData(8, false)]
    [InlineData(9, false)]
    [InlineData(10, true)]
    [InlineData(11, false)]
    [InlineData(14, true)]
    public void OnlyBlockEndsCanDecideAMatch(int round, bool ends) {
        Assert.Equal(ends, RoundOrder.EndsABlock(round));
    }

    [Fact]
    public void ALeadMidBlockDoesNotEndTheMatch() {
        Assert.Equal(-1, RoundOrder.Winner(4, 3, 1));
        Assert.Equal(-1, RoundOrder.Winner(8, 5, 3));
    }

    [Fact]
    public void ALeadAtTheEndOfRegulationEndsTheMatch() {
        Assert.Equal(0, RoundOrder.Winner(6, 4, 2));
        Assert.Equal(1, RoundOrder.Winner(6, 2, 4));
    }

    [Fact]
    public void LevelAtTheEndOfABlockGoesToOvertime() {
        Assert.Equal(-1, RoundOrder.Winner(6, 3, 3));
        Assert.Equal(-1, RoundOrder.Winner(10, 4, 4));
    }

    [Fact]
    public void ALeadAtTheEndOfAnOvertimeBlockEndsTheMatch() {
        Assert.Equal(0, RoundOrder.Winner(10, 5, 3));
        Assert.Equal(1, RoundOrder.Winner(14, 5, 7));
    }
}
