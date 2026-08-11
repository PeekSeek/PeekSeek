using PeekSeek.Commands;
using Xunit;

namespace PeekSeek.Tests;

public class StatsCommandTests {
    [Fact]
    public void FormatStats_ComputesHeadshotPercentage() {
        string result = StatsCommand.FormatStats("Ferret", kills: 4, deaths: 2, headshots: 1, wins: 1);

        Assert.Equal("Ferret — 4K 2D (25% HS) — 1 round(s) won this match", result);
    }

    [Fact]
    public void FormatStats_NoKills_HeadshotPercentageIsZeroNotDivideByZero() {
        string result = StatsCommand.FormatStats("Ferret", kills: 0, deaths: 3, headshots: 0, wins: 0);

        Assert.Equal("Ferret — 0K 3D (0% HS) — 0 round(s) won this match", result);
    }
}
