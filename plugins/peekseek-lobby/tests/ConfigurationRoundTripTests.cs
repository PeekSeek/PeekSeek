using System.IO;
using System.Xml.Serialization;
using Xunit;

namespace PeekSeekLobby.Tests;

/// The deployed config is XML, and XmlSerializer silently drops anything it
/// can't map, AllowedKitCategories once serialised out empty on the live
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
        Assert.Equal(config.QueueEffectGUID, loaded.QueueEffectGUID);
        Assert.Equal(config.QueueRegions[1].Code, loaded.QueueRegions[1].Code);
        Assert.Equal(config.KillRewards.Count, loaded.KillRewards.Count);
        Assert.Equal(config.KillRewards[0].ItemID, loaded.KillRewards[0].ItemID);
        Assert.Equal(config.KillRewards[0].MaxCarried, loaded.KillRewards[0].MaxCarried);
        Assert.Equal(config.KillRewards[0].GrantOnSpawn, loaded.KillRewards[0].GrantOnSpawn);
        Assert.Equal(config.Spawns.Count, loaded.Spawns.Count);
        Assert.Equal(config.EnableQueue, loaded.EnableQueue);
        Assert.Equal(config.UnlimitedStamina, loaded.UnlimitedStamina);
        Assert.Equal(config.ForceSoloPlayers, loaded.ForceSoloPlayers);
        Assert.Equal(config.szMatchURL, loaded.szMatchURL);
        Assert.Equal(config.ResetIntervalSeconds, loaded.ResetIntervalSeconds);
        Assert.Equal(config.StreakWindowSeconds, loaded.StreakWindowSeconds);
        Assert.Equal(config.SpawnProtectionSeconds, loaded.SpawnProtectionSeconds);
        Assert.Equal(config.RespawnDelaySeconds, loaded.RespawnDelaySeconds);
    }

    /// Matchmaking is on, and stays switchable, the lobby is live, but a server
    /// that wants to be a pure warm-up box has to be able to turn it back off.
    [Fact]
    public void Defaults_EnableMatchmaking() {
        var config = new PeekSeekLobbyConfiguration();
        config.LoadDefaults();

        Assert.True(config.EnableQueue);

        config.EnableQueue = false;
        Assert.False(RoundTrip(config).EnableQueue);
    }

    /// Every field of a streak reward has to survive the XML, the item list most
    /// of all, a streak that deserialises with no items pays out nothing and
    /// says nothing about why.
    [Fact]
    public void StreakRewards_SurviveAnXmlRoundTripIntact() {
        var config = new PeekSeekLobbyConfiguration();
        config.LoadDefaults();

        PeekSeekLobbyConfiguration loaded = RoundTrip(config);

        Assert.Equal(config.StreakRewards.Count, loaded.StreakRewards.Count);
        for (int i = 0; i < config.StreakRewards.Count; i++) {
            var original = config.StreakRewards[i];
            var restored = loaded.StreakRewards[i];

            Assert.Equal(original.KillsRequired, restored.KillsRequired);
            Assert.Equal(original.CooldownSeconds, restored.CooldownSeconds);
            Assert.Equal(original.szChatMessage, restored.szChatMessage);
            Assert.Equal(original.Items.Count, restored.Items.Count);
            Assert.Equal(original.Items[0].ItemID, restored.Items[0].ItemID);
            Assert.Equal(original.Items[0].Amount, restored.Items[0].Amount);
        }
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

    /// The gun's attachment/ammo bytes are the whole point of the fallback,
    /// a mangled state string spawns a gun that can't shoot.
    [Fact]
    public void FallbackGunState_ParsesToTheEighteenBytesUnturnedExpects() {
        var config = new PeekSeekLobbyConfiguration();
        config.LoadDefaults();

        var loadout = RoundTrip(config).FallbackKits[0].ToLoadout();

        Assert.Equal(18, loadout.Items[0].State.Length);
    }
}
