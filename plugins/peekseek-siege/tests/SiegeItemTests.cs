using System.Linq;
using PeekSeekSiege.Core.Models;
using Xunit;

namespace PeekSeekSiege.Tests;

public class SiegeItemTests {
    [Fact]
    public void NoStateMeansNoBytes() {
        Assert.Empty(new SiegeItem { ItemId = 366, State = "" }.StateBytes());
        Assert.Empty(new SiegeItem { ItemId = 366, State = null }.StateBytes());
    }

    /// The state string is copied straight out of the site's mmconfig.json, so it has
    /// to parse exactly as written there, spaces and all.
    [Fact]
    public void StateParsesTheSiteFormat() {
        SiegeItem item = new() { ItemId = 346, State = "146,0,0,0,0,0,0,0,185,4,1,1,1,100,100,100,100,100" };

        byte[] bytes = item.StateBytes();

        Assert.Equal(18, bytes.Length);
        Assert.Equal(146, bytes[0]);
        Assert.Equal(185, bytes[8]);
        Assert.Equal(100, bytes[17]);
    }

    [Fact]
    public void SpacesAroundTheCommasAreTolerated() {
        Assert.Equal(new byte[] { 1, 2, 3 }, new SiegeItem { State = "1, 2 ,3" }.StateBytes());
    }
}

public class ConfigurationTests {
    /// The board is drawn for five tiles. A config with a sixth would silently not show
    /// it, so the shipped defaults have to be exactly five each.
    [Fact]
    public void BothOptionListsHaveFive() {
        PeekSeekSiegeConfiguration config = new();
        config.LoadDefaults();

        Assert.Equal(5, config.DefenceOptions.Count);
        Assert.Equal(5, config.RaidOptions.Count);
    }

    /// Every option needs a name and a blurb, because both are drawn on the tile and an
    /// empty one reads as a broken UI rather than as a missing option.
    [Fact]
    public void EveryOptionIsLabelled() {
        PeekSeekSiegeConfiguration config = new();
        config.LoadDefaults();

        foreach (SiegeOption option in config.DefenceOptions.Concat(config.RaidOptions)) {
            Assert.False(string.IsNullOrWhiteSpace(option.Name));
            Assert.False(string.IsNullOrWhiteSpace(option.Blurb));
            Assert.NotEmpty(option.Items);
        }
    }

    /// No floors in the build supplies. This is the rule that forces a team to build off
    /// the anchor instead of laying a second foundation somewhere better, and it is easy
    /// to break by pasting an id in without checking what it is.
    [Fact]
    public void BuildSuppliesContainNoFloors() {
        PeekSeekSiegeConfiguration config = new();
        config.LoadDefaults();

        Assert.DoesNotContain(config.BuildSupplies, item => item.ItemId == config.AnchorFloorItemId);
    }

    /// The anchor is placed by the gamemode and never handed out, so neither of its two
    /// pieces may appear in anything a player is given.
    [Fact]
    public void NobodyIsEverGivenAnAnchorPiece() {
        PeekSeekSiegeConfiguration config = new();
        config.LoadDefaults();

        var handedOut = config.BuildSupplies
            .Concat(config.DefaultItems)
            .Concat(config.DefenceOptions.SelectMany(o => o.Items))
            .Concat(config.RaidOptions.SelectMany(o => o.Items));

        foreach (SiegeItem item in handedOut) {
            Assert.NotEqual(config.AnchorFloorItemId, item.ItemId);
            Assert.NotEqual(config.AnchorClaimFlagItemId, item.ItemId);
        }
    }
}
