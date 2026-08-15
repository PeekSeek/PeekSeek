using PeekSeekLobby.Commands;
using Xunit;

namespace PeekSeekLobby.Tests;

public class StatsCommandTests {
    [Fact]
    public void Format_ComputesHeadshotPercentage() {
        string result = StatsCommand.Format("Ferret", kills: 4, deaths: 2, headshots: 1);

        Assert.Equal("Ferret: 4K 2D (25% HS)", result);
    }

    [Fact]
    public void Format_NoKills_HeadshotPercentageIsZeroNotDivideByZero() {
        string result = StatsCommand.Format("Ferret", kills: 0, deaths: 3, headshots: 0);

        Assert.Equal("Ferret: 0K 3D (0% HS)", result);
    }
}
