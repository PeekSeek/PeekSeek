using System.IO;
using System.Linq;
using PeekSeekRaid.Core.Models;
using Xunit;

namespace PeekSeekRaid.Tests;

public class RaidItemTests {
    [Fact]
    public void NoStateMeansNoBytes() {
        Assert.Empty(new RaidItem { ItemId = 366, State = "" }.StateBytes());
        Assert.Empty(new RaidItem { ItemId = 366, State = null }.StateBytes());
    }

    /// The state string is copied straight out of the site's mmconfig.json, so it has
    /// to parse exactly as written there, spaces and all.
    [Fact]
    public void StateParsesTheSiteFormat() {
        RaidItem item = new() { ItemId = 346, State = "146,0,0,0,0,0,0,0,185,4,1,1,1,100,100,100,100,100" };

        byte[] bytes = item.StateBytes();

        Assert.Equal(18, bytes.Length);
        Assert.Equal(146, bytes[0]);
        Assert.Equal(185, bytes[8]);
        Assert.Equal(100, bytes[17]);
    }

    [Fact]
    public void SpacesAroundTheCommasAreTolerated() {
        Assert.Equal(new byte[] { 1, 2, 3 }, new RaidItem { State = "1, 2 ,3" }.StateBytes());
    }
}

public class ConfigurationTests {
    /// The board is drawn for five tiles. A config with a sixth would silently not show
    /// it, so the shipped defaults have to be exactly five each.
    [Fact]
    public void BothOptionListsHaveFive() {
        PeekSeekRaidConfiguration config = new();
        config.LoadDefaults();

        Assert.Equal(5, config.DefenceOptions.Count);
        Assert.Equal(5, config.RaidOptions.Count);
    }

    /// Every option needs a name and a blurb, because both are drawn on the tile and an
    /// empty one reads as a broken UI rather than as a missing option.
    [Fact]
    public void EveryOptionIsLabelled() {
        PeekSeekRaidConfiguration config = new();
        config.LoadDefaults();

        foreach (RaidOption option in config.DefenceOptions.Concat(config.RaidOptions)) {
            Assert.False(string.IsNullOrWhiteSpace(option.Name));
            Assert.False(string.IsNullOrWhiteSpace(option.Blurb));
            Assert.NotEmpty(option.Items);
        }
    }

    /// Every tile shows a picture, and the picture is an item the site has one for. The
    /// icons come from the same folder the site's loadout editor reads, so this walks up
    /// to the repo and looks in it: an option pointing at an item with no icon is a
    /// blank tile in game, and nothing else would say so before a player saw it.
    [Fact]
    public void EveryOptionHasAnIconTheSiteServes() {
        PeekSeekRaidConfiguration config = new();
        config.LoadDefaults();

        string icons = Path.Combine(RepoRoot(), "website", "public", "unturned");
        Assert.True(Directory.Exists(icons), $"Expected the site's icon folder at {icons}");

        foreach (RaidOption option in config.DefenceOptions.Concat(config.RaidOptions)) {
            Assert.NotEqual(0, option.IconItemId);
            Assert.True(
                File.Exists(Path.Combine(icons, option.IconItemId + ".png")),
                $"{option.Name} shows item {option.IconItemId}, which the site has no icon for.");
        }
    }

    /// The board asks for `Icon` when it is set and the first item otherwise, so an
    /// option only has to say which item it is a picture of when that is not obvious.
    [Fact]
    public void AnOptionIsPicturedAsItsFirstItemUnlessItSaysOtherwise() {
        RaidOption plain = new() { Items = [new RaidItem(519, 1), new RaidItem(520, 2)] };
        RaidOption chosen = new() { Icon = 1408, Items = [new RaidItem(254, 2), new RaidItem(1408, 1)] };

        Assert.Equal(519, plain.IconItemId);
        Assert.Equal(1408, chosen.IconItemId);
        Assert.Equal(0, new RaidOption().IconItemId);
    }

    private static string RepoRoot() {
        DirectoryInfo dir = new(Directory.GetCurrentDirectory());
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "website"))) dir = dir.Parent;
        return dir?.FullName ?? Directory.GetCurrentDirectory();
    }

    /// One of every piece has to fit in the hands page at once, because that is what a
    /// builder carries: a piece with none of its kind in the inventory is a piece the
    /// planner cannot offer. Structures are two or four cells each; the count here is
    /// the worst case, every piece four cells.
    [Fact]
    public void OneOfEveryPieceFitsInTheHands() {
        PeekSeekRaidConfiguration config = new();
        config.LoadDefaults();

        int kinds = config.BuildSupplies.Concat(config.TriangleSupplies).Select(item => item.ItemId).Distinct().Count();
        Assert.True(kinds * 4 <= config.HandsWidth * config.HandsHeight,
            $"{kinds} kinds of piece for {config.HandsWidth * config.HandsHeight} cells of hands.");
    }

    /// Every piece in the budget and every kit item shows on a panel or a tile with a
    /// picture the site serves, from the same folder the site's loadout editor reads.
    [Fact]
    public void EveryBudgetPieceHasAnIconTheSiteServes() {
        PeekSeekRaidConfiguration config = new();
        config.LoadDefaults();

        string icons = Path.Combine(RepoRoot(), "website", "public", "unturned");
        foreach (RaidItem item in config.BuildSupplies.Concat(config.TriangleSupplies)) {
            Assert.True(
                File.Exists(Path.Combine(icons, item.ItemId + ".png")),
                $"Budget piece {item.ItemId} has no icon on the site.");
        }
    }

    /// Attackers may be given charges, which are placed, but never anything that
    /// builds: a wall an attacker puts up inside the base is a base change mid-round,
    /// and the flag is the defenders' to stand.
    [Fact]
    public void RaidersCarryNothingThatBuilds() {
        PeekSeekRaidConfiguration config = new();
        config.LoadDefaults();

        ushort[] builds = [1215, 1212, 1210, 1216, 1984, 57, 49, 59, 53, 55, 318, 378, 1332, 1158, 1234];

        foreach (RaidItem item in config.RaidOptions.SelectMany(option => option.Items)) {
            Assert.DoesNotContain(builds, id => id == item.ItemId);
        }
    }

    /// The sentry kit is only a kit because the sentry goes down armed: the gun is
    /// configured, and it has to be a gun.
    [Fact]
    public void TheSentryIsArmedWithAGun() {
        PeekSeekRaidConfiguration config = new();
        config.LoadDefaults();

        Assert.NotNull(config.SentryFirearm);
        Assert.Equal(112, config.SentryFirearm.ItemId);
        Assert.Contains(config.DefenceOptions, option => option.Items.Any(item => item.ItemId == 43131));
    }

    /// The site marker is placed by the gamemode and never handed out. If a player could
    /// carry one they could stand a second marker on their own plot, and the anchor is
    /// what every base restore is measured from.
    [Fact]
    public void NobodyIsEverGivenASiteMarker() {
        PeekSeekRaidConfiguration config = new();
        config.LoadDefaults();

        var handedOut = config.BuildSupplies
            .Concat(config.DefaultItems)
            .Concat(config.DefenceOptions.SelectMany(o => o.Items))
            .Concat(config.RaidOptions.SelectMany(o => o.Items));

        foreach (RaidItem item in handedOut) {
            Assert.NotEqual(config.AnchorMarkerItemId, item.ItemId);
        }
    }
}
