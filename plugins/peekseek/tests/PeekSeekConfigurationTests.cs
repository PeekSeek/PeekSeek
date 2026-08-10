using System.Linq;
using System.Reflection;
using System.Xml.Serialization;
using Xunit;

namespace PeekSeek.Tests;

public class PeekSeekConfigurationTests {
    [Fact]
    public void LoadDefaults_PopulatesAllCollections() {
        var config = new PeekSeekConfiguration();

        config.LoadDefaults();

        Assert.NotNull(config.Spawns);
        Assert.NotEmpty(config.Spawns);
        Assert.NotNull(config.PingConfig);
        Assert.NotEmpty(config.PingConfig);
    }

    [Fact]
    public void LoadDefaults_SetsKnownDefaultValues() {
        var config = new PeekSeekConfiguration();

        config.LoadDefaults();

        Assert.Equal((short)120, config.sJoinTimeout);
        Assert.Equal("spectator", config.szSpectatorGroup);
        Assert.Equal("#FFFFFF", config.szHexDefaultChatColor);
        Assert.False(config.UseTestingJSON);
    }

    [Fact]
    public void XmlRootAttribute_IsConfig_SoLegacyConfigFilesLoad() {
        var attr = typeof(PeekSeekConfiguration)
            .GetCustomAttributes<XmlRootAttribute>(inherit: false)
            .SingleOrDefault();

        Assert.NotNull(attr);
        Assert.Equal("Config", attr!.ElementName);
    }
}
