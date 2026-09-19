using PeekSeekSiege.Core;
using Xunit;

namespace PeekSeekSiege.Tests;

public class WordingTests {
    /// A test server runs thirty-second build phases, and "0 minutes" is what those
    /// were announced as.
    [Theory]
    [InlineData(30, "30 seconds")]
    [InlineData(1, "1 second")]
    [InlineData(60, "1 minute")]
    [InlineData(90, "90 seconds")]
    [InlineData(300, "5 minutes")]
    public void ADurationIsSaidInTheUnitThatDividesIt(int seconds, string said) {
        Assert.Equal(said, Wording.Duration(seconds));
    }

    /// The chat form of the map says where a spawn lies from the base. Eight winds,
    /// each covering 45 degrees centred on its own heading.
    [Theory]
    [InlineData(0f, "north")]
    [InlineData(22f, "north")]
    [InlineData(23f, "north-east")]
    [InlineData(90f, "east")]
    [InlineData(180f, "south")]
    [InlineData(270f, "west")]
    [InlineData(337f, "north-west")]
    [InlineData(338f, "north")]
    [InlineData(-45f, "north-west")]
    public void ABearingIsSaidAsAWind(float bearing, string said) {
        Assert.Equal(said, Wording.Compass(bearing));
    }

    /// Every list the build phase hands out has to exist, because it is walked without
    /// asking. A fresh install once had two of them null, which a server only escaped
    /// because its config had been written out and read back as empty.
    [Fact]
    public void EveryHandoutListHasADefault() {
        PeekSeekSiegeConfiguration config = new();
        config.LoadDefaults();

        Assert.NotNull(config.BuildTools);
        Assert.NotNull(config.FallbackOutfit);
        Assert.NotEmpty(config.FallbackOutfit);
    }
}
