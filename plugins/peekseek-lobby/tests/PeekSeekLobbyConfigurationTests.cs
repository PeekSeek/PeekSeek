using System.Linq;
using System.Reflection;
using System.Xml.Serialization;
using PeekSeekLobby.Core.Models;
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

    /// The bounce is standard equipment and the heal is earned, so only the
    /// bounce may be handed out on spawn. A cap of one each is what stops a long
    /// life turning into a stockpile.
    [Fact]
    public void LoadDefaults_SpawnsWithABounceButEarnsTheHeal() {
        var config = new PeekSeekLobbyConfiguration();

        config.LoadDefaults();

        Assert.Equal(2, config.KillRewards.Count);
        Assert.All(config.KillRewards, reward => Assert.Equal((byte)1, reward.MaxCarried));

        RewardItem bounce = config.KillRewards.Find(reward => reward.ItemID == 1838);
        RewardItem heal = config.KillRewards.Find(reward => reward.ItemID == 395);

        Assert.NotNull(bounce);
        Assert.NotNull(heal);
        Assert.True(bounce.GrantOnSpawn);
        Assert.False(heal.GrantOnSpawn);
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
