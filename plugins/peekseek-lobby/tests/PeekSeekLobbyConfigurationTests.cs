using System.Linq;
using System.Reflection;
using System.Xml.Serialization;
using Xunit;

namespace PeekSeekLobby.Tests;

public class PeekSeekLobbyConfigurationTests {
    [Fact]
    public void LoadDefaults_PopulatesAllCollections() {
        var config = new PeekSeekLobbyConfiguration();

        config.LoadDefaults();

        Assert.NotNull(config.Spawns);
        Assert.NotEmpty(config.Spawns);
        Assert.NotNull(config.RespawnCountdownAtSecondsRemaining);
        Assert.NotEmpty(config.RespawnCountdownAtSecondsRemaining);
        Assert.NotNull(config.AllowedKitCategories);
        Assert.NotEmpty(config.AllowedKitCategories);
        Assert.NotNull(config.KillRewards);
        Assert.NotEmpty(config.KillRewards);
    }

    /// Spawning with a heal and a bounce is the intended loadout, and a cap of
    /// one each is what stops a long life turning into a stockpile.
    [Fact]
    public void LoadDefaults_GivesOneHealAndOneBounce() {
        var config = new PeekSeekLobbyConfiguration();

        config.LoadDefaults();

        Assert.Equal(2, config.KillRewards.Count);
        Assert.Contains(config.KillRewards, reward => reward.ItemID == 395);
        Assert.Contains(config.KillRewards, reward => reward.ItemID == 1838);
        Assert.All(config.KillRewards, reward => Assert.Equal((byte)1, reward.MaxCarried));
    }

    [Fact]
    public void LoadDefaults_SetsKnownDefaultValues() {
        var config = new PeekSeekLobbyConfiguration();

        config.LoadDefaults();

        Assert.Equal((ushort)1, config.GamemodeBitmask);
        Assert.Equal("paris", config.szRegion);
        Assert.Equal(5f, config.SpawnProtectionSeconds);
        Assert.Equal(10f, config.RespawnDelaySeconds);
        Assert.Equal("#FFFFFF", config.szHexDefaultChatColor);
        Assert.Equal(["meta", "sniper"], config.AllowedKitCategories);
    }

    [Fact]
    public void XmlRootAttribute_IsConfig() {
        var attr = typeof(PeekSeekLobbyConfiguration)
            .GetCustomAttributes<XmlRootAttribute>(inherit: false)
            .SingleOrDefault();

        Assert.NotNull(attr);
        Assert.Equal("Config", attr!.ElementName);
    }
}
