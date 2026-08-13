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
    }

    [Fact]
    public void LoadDefaults_SetsKnownDefaultValues() {
        var config = new PeekSeekLobbyConfiguration();

        config.LoadDefaults();

        Assert.Equal((ushort)1, config.GamemodeBitmask);
        Assert.Equal("paris", config.szRegion);
        Assert.Equal((ushort)395, config.BloodBagItemID);
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
