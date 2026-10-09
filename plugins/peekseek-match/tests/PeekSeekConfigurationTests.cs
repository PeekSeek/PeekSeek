using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml.Linq;
using System.Xml.Serialization;
using Xunit;

namespace PeekSeek.Tests;

public class PeekSeekConfigurationTests {
    [Fact]
    public void LoadDefaults_PopulatesAllCollections() {
        var config = new PeekSeekConfiguration();

        config.LoadDefaults();

        Assert.NotNull(config.PingConfig);
        Assert.NotEmpty(config.PingConfig);
        Assert.NotEmpty(config.BannerEffectGUID);
        Assert.NotEmpty(config.ScoreboardEffectGUID);
    }

    [Fact]
    public void LoadDefaults_SetsKnownDefaultValues() {
        var config = new PeekSeekConfiguration();

        config.LoadDefaults();

        Assert.Equal((ushort)120, config.LobbyTimeoutSeconds);
        Assert.Equal((ushort)10, config.DelayKickAfterWin);
        Assert.Equal(0UL, config.LobbyServerRelaySGSSteamID);
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

    /// The config deployed with 1.0 must keep working verbatim. XmlSerializer drops elements it
    /// does not recognise without complaining, so a field this project renames or never declares
    /// silently reverts to its default on a server that has been running for years.
    ///
    /// Two elements have knowingly left: `Spawns`, because the round spawns belong to
    /// PeekSeek ARENA now and live in its own config, and
    /// `LobbyTimeoutBroadcastSecondsRemaining`, because the wait is on the bar's clock
    /// and no longer counted down in chat.
    [Fact]
    public void Live10Config_HasAFieldForEveryElementItSets() {
        var declared = new HashSet<string>(
            typeof(PeekSeekConfiguration).GetFields().Select(field => field.Name)
        ) { "Spawns", "LobbyTimeoutBroadcastSecondsRemaining" };

        var elements = XDocument.Load(FixturePath("live-1.0.configuration.xml"))
            .Root.Elements()
            .Select(element => element.Name.LocalName);

        Assert.All(elements, name => Assert.Contains(name, declared));
    }

    private static string FixturePath(string name) {
        // Assembly.Location points at a shadow copy under the test host, AppContext.BaseDirectory does not.
        return Path.Combine(AppContext.BaseDirectory, "fixtures", name);
    }
}
