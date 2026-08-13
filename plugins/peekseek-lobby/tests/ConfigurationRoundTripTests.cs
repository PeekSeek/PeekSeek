using System.IO;
using System.Xml.Serialization;
using Xunit;

namespace PeekSeekLobby.Tests;

/// The deployed config is XML, and XmlSerializer silently drops anything it
/// can't map — AllowedKitCategories once serialised out empty on the live
/// server, which would have made /kit reject every category. These pin the
/// round-trip so a config-shaped mistake fails here instead of in production.
public class ConfigurationRoundTripTests {
    private static PeekSeekLobbyConfiguration RoundTrip(PeekSeekLobbyConfiguration config) {
        var serializer = new XmlSerializer(typeof(PeekSeekLobbyConfiguration));
        using var buffer = new StringWriter();
        serializer.Serialize(buffer, config);

        using var reader = new StringReader(buffer.ToString());
        return (PeekSeekLobbyConfiguration)serializer.Deserialize(reader);
    }

    [Fact]
    public void Defaults_SurviveAnXmlRoundTrip() {
        var config = new PeekSeekLobbyConfiguration();
        config.LoadDefaults();

        PeekSeekLobbyConfiguration loaded = RoundTrip(config);

        Assert.Equal(config.AllowedKitCategories, loaded.AllowedKitCategories);
        Assert.Equal(config.szDiscordURL, loaded.szDiscordURL);
        Assert.Equal(config.KillRewards.Count, loaded.KillRewards.Count);
        Assert.Equal(config.KillRewards[0].ItemID, loaded.KillRewards[0].ItemID);
        Assert.Equal(config.KillRewards[0].MaxCarried, loaded.KillRewards[0].MaxCarried);
        Assert.Equal(config.Spawns.Count, loaded.Spawns.Count);
        Assert.Equal(config.EnableQueue, loaded.EnableQueue);
        Assert.Equal(config.szMatchURL, loaded.szMatchURL);
    }

    /// Matchmaking stays off until it's deliberately switched on, so a fresh
    /// config never starts heartbeating at a site that isn't ready for it.
    [Fact]
    public void Defaults_LeaveMatchmakingDisabled() {
        var config = new PeekSeekLobbyConfiguration();
        config.LoadDefaults();

        Assert.False(config.EnableQueue);
    }

    [Fact]
    public void FallbackKit_SurvivesAnXmlRoundTripIntact() {
        var config = new PeekSeekLobbyConfiguration();
        config.LoadDefaults();

        PeekSeekLobbyConfiguration loaded = RoundTrip(config);

        Assert.NotNull(loaded.FallbackKits);
        Assert.NotEmpty(loaded.FallbackKits);

        var original = config.FallbackKits[0];
        var restored = loaded.FallbackKits[0];
        Assert.Equal(original.Category, restored.Category);
        Assert.Equal(original.Items.Count, restored.Items.Count);
        Assert.Equal(original.Items[0].ItemID, restored.Items[0].ItemID);
        Assert.Equal(original.Items[0].State, restored.Items[0].State);
        Assert.Equal(original.Magazine.ItemID, restored.Magazine.ItemID);
        Assert.Equal(original.Magazine.Amount, restored.Magazine.Amount);
    }

    /// The gun's attachment/ammo bytes are the whole point of the fallback —
    /// a mangled state string spawns a gun that can't shoot.
    [Fact]
    public void FallbackGunState_ParsesToTheEighteenBytesUnturnedExpects() {
        var config = new PeekSeekLobbyConfiguration();
        config.LoadDefaults();

        var loadout = RoundTrip(config).FallbackKits[0].ToLoadout();

        Assert.Equal(18, loadout.Items[0].State.Length);
    }
}
