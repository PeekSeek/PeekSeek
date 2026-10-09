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
        Assert.NotNull(config.AllowedKitCategories);
        Assert.NotEmpty(config.AllowedKitCategories);
        Assert.NotNull(config.KillRewards);
        Assert.NotEmpty(config.KillRewards);
        Assert.NotNull(config.StreakRewards);
        Assert.NotEmpty(config.StreakRewards);
        Assert.All(config.StreakRewards, reward => Assert.NotEmpty(reward.Items));
    }

    /// Nothing is handed out for free any more: the heal is earned per kill and
    /// the bounce moved to the streaks, so spawning grants nothing at all. A cap
    /// of one is what stops a long life turning into a stockpile.
    [Fact]
    public void LoadDefaults_EarnsEveryRewardRatherThanSpawningWithIt() {
        var config = new PeekSeekLobbyConfiguration();

        config.LoadDefaults();

        Assert.All(config.KillRewards, reward => Assert.Equal((byte)1, reward.MaxCarried));
        Assert.All(config.KillRewards, reward => Assert.False(reward.GrantOnSpawn));

        RewardItem heal = config.KillRewards.Find(reward => reward.ItemID == 395);
        Assert.NotNull(heal);

        // The bounce is a streak reward now, not standard equipment.
        Assert.DoesNotContain(config.KillRewards, reward => reward.ItemID == 1838);
    }

    /// The streaks the arena is meant to pay out, pinned to the exact kill counts
    /// and items they were specified with, a silently renumbered streak is the
    /// kind of thing nobody notices until someone asks where their drum went.
    [Fact]
    public void LoadDefaults_PaysOutTheSpecifiedStreaks() {
        var config = new PeekSeekLobbyConfiguration();

        config.LoadDefaults();

        StreakReward doubleKill = config.StreakRewards.Find(reward => reward.KillsRequired == 2);
        Assert.NotNull(doubleKill);
        Assert.Contains(doubleKill.Items, item => item.ItemID == 1103 && item.Amount == 1);
        Assert.Contains(doubleKill.Items, item => item.ItemID == 1838 && item.Amount == 1);

        // Both drums are offered; only the one the equipped gun can chamber is
        // ever fitted, which is what leaves snipers with nothing.
        StreakReward drum = config.StreakRewards.Find(reward => reward.KillsRequired == 3);
        Assert.NotNull(drum);
        Assert.Contains(drum.Items, item => item.ItemID == 125);
        Assert.Contains(drum.Items, item => item.ItemID == 17);

        StreakReward rampage = config.StreakRewards.Find(reward => reward.KillsRequired == 5);
        Assert.NotNull(rampage);
        Assert.Contains(rampage.Items, item => item.ItemID == 1520 && item.Amount == 2);
        Assert.Contains(rampage.Items, item => item.ItemID == 1838 && item.Amount == 2);
        Assert.Contains("rampage", rampage.szChatMessage);
    }

    /// A minute between payouts of the same streak, and ten seconds of slack
    /// between the kills that build one.
    [Fact]
    public void LoadDefaults_PutsEveryStreakOnAMinuteCooldown() {
        var config = new PeekSeekLobbyConfiguration();

        config.LoadDefaults();

        Assert.Equal(60f, config.StreakWindowSeconds);
        Assert.All(config.StreakRewards, reward => Assert.Equal(60f, reward.CooldownSeconds));
    }

    [Fact]
    public void LoadDefaults_SetsKnownDefaultValues() {
        var config = new PeekSeekLobbyConfiguration();

        config.LoadDefaults();

        Assert.Equal("paris", config.szRegion);
        Assert.Equal(5f, config.SpawnProtectionSeconds);
        Assert.Equal(5f, config.RespawnDelaySeconds);
        Assert.Equal("#FFFFFF", config.szHexDefaultChatColor);
        Assert.Equal(["meta", "sniper"], config.AllowedKitCategories);
    }

    /// One spare, not the five a match kit carries: a lobby life is short and
    /// re-kitted constantly, so more than one is just clutter. The fallback kits
    /// carry the same count so the two don't tell different stories.
    [Fact]
    public void LoadDefaults_CarriesASingleSpareMagazine() {
        var config = new PeekSeekLobbyConfiguration();

        config.LoadDefaults();

        Assert.Equal((byte)1, config.SpareMagazines);
        Assert.All(config.FallbackKits, kit => Assert.Equal((byte)1, kit.Magazine.Amount));
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
