using PeekSeekRaid.Core;
using Xunit;

namespace PeekSeekRaid.Tests;

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

    /// Every list the build phase hands out has to exist, because it is walked without
    /// asking. A fresh install once had two of them null, which a server only escaped
    /// because its config had been written out and read back as empty.
    [Fact]
    public void EveryHandoutListHasADefault() {
        PeekSeekRaidConfiguration config = new();
        config.LoadDefaults();

        Assert.NotNull(config.BuildTools);
        Assert.NotNull(config.FallbackOutfit);
        Assert.NotEmpty(config.FallbackOutfit);
    }
}
