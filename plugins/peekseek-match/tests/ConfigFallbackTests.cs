using PeekSeek;
using Xunit;

namespace PeekSeek.Tests;

/// Rocket never merges a new field into a written configuration.xml, so every server
/// that has been running since before a field existed reads it as null or zero. These
/// hold the one thing that stops that being a screen nobody can see: a file with
/// nothing to say about a field gets the default, and a file that does say something
/// keeps it.
public class ConfigFallbackTests {
    [Fact]
    public void AFileWithoutTheNewScreensGetsTheirDefaults() {
        var live = new PeekSeekConfiguration();

        var filled = live.FillInWhatOlderFilesLack();

        Assert.NotEmpty(filled);
        var fresh = new PeekSeekConfiguration();
        fresh.LoadDefaults();
        Assert.Equal(fresh.BannerEffectGUID, live.BannerEffectGUID);
        Assert.Equal(fresh.BannerKey, live.BannerKey);
        Assert.Equal(fresh.BannerSeconds, live.BannerSeconds);
        Assert.Equal(fresh.DeathCardEffectGUID, live.DeathCardEffectGUID);
        Assert.Equal(fresh.ScoreboardEffectGUID, live.ScoreboardEffectGUID);
        Assert.Equal(fresh.TagEffectGUID, live.TagEffectGUID);
    }

    /// The ping bands are not a screen, but they fail harder than one. PSManager reads
    /// them on load and again on every hit, so a file written before the field existed
    /// took the plugin down with a NullReferenceException instead of handicapping
    /// nobody.
    [Fact]
    public void AFileWithoutPingBandsGetsTheDefaultOnes() {
        var live = new PeekSeekConfiguration();

        var filled = live.FillInWhatOlderFilesLack();

        Assert.Contains(nameof(live.PingConfig), filled);
        Assert.NotEmpty(live.PingConfig);
    }

    /// An empty list is an answer, not a gap. A file that writes the element and puts
    /// no bands in it has turned the handicap off on purpose, and filling it back in
    /// would overrule the operator.
    [Fact]
    public void AFileThatWritesNoPingBandsKeepsNone() {
        var live = new PeekSeekConfiguration { PingConfig = [] };

        var filled = live.FillInWhatOlderFilesLack();

        Assert.DoesNotContain(nameof(live.PingConfig), filled);
        Assert.Empty(live.PingConfig);
    }

    /// The written file is still the truth for anything it says.
    [Fact]
    public void AFileThatSetsAFieldKeepsIt() {
        var live = new PeekSeekConfiguration {
            BannerEffectGUID = "0123456789abcdef0123456789abcdef",
            BannerKey = 999,
            BannerSeconds = 9f
        };

        var filled = live.FillInWhatOlderFilesLack();

        Assert.DoesNotContain(nameof(live.BannerEffectGUID), filled);
        Assert.Equal("0123456789abcdef0123456789abcdef", live.BannerEffectGUID);
        Assert.Equal((short)999, live.BannerKey);
        Assert.Equal(9f, live.BannerSeconds);
    }
}
