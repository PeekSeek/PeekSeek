using PeekSeek.Gamemodes;
using Xunit;

namespace PeekSeek.Tests;

public class GamemodeNameTests {
    [Theory]
    [InlineData("Raid", "Raid", true)]
    [InlineData("Raid", "RAID 2v2", true)]
    [InlineData("Raid", "raid 1v1", true)]
    [InlineData("Raid", "Raider", false)]
    [InlineData("Raid", "Arena 1v1", false)]
    [InlineData("Arena", "Arena Drums 1v1", true)]
    [InlineData("Raid", null, false)]
    public void Runs_MatchesTheModeNameOrItWithASize(string mode, string site, bool expected) {
        Assert.Equal(expected, Gamemodes.Gamemodes.Runs(mode, site));
    }
}
