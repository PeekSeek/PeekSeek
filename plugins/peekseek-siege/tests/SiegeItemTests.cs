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

    /// Floors are rationed, never plentiful. A team with an endless supply of them could
    /// lay a second base beside the anchor and pick whichever one suited the round, which
    /// is the thing the single anchor exists to prevent.
    [Fact]
    public void FloorsAreNeverPlentiful() {
        PeekSeekSiegeConfiguration config = new();
        config.LoadDefaults();

        Assert.DoesNotContain(config.PlentifulSupplies, item => item.ItemId == config.AnchorFloorItemId);
        Assert.Contains(config.RationedSupplies, item => item.ItemId == config.AnchorFloorItemId);
    }

    /// Metal is the scarce material, so none of it may appear in the list that is topped
    /// back up every few seconds. The metal wall matters most: it is what a breach charge
    /// has to be spent on.
    [Fact]
    public void MetalIsNeverPlentiful() {
        PeekSeekSiegeConfiguration config = new();
        config.LoadDefaults();

        foreach (ushort metal in new ushort[] { 369, 371, 378, 1332 }) {
            Assert.DoesNotContain(config.PlentifulSupplies, item => item.ItemId == metal);
        }
    }

    /// The site marker is placed by the gamemode and never handed out. If a player could
    /// carry one they could stand a second marker on their own plot, and the anchor is
    /// what every base restore is measured from.
    [Fact]
    public void NobodyIsEverGivenASiteMarker() {
        PeekSeekSiegeConfiguration config = new();
        config.LoadDefaults();

        var handedOut = config.RationedSupplies
            .Concat(config.PlentifulSupplies)
            .Concat(config.DefaultItems)
            .Concat(config.DefenceOptions.SelectMany(o => o.Items))
            .Concat(config.RaidOptions.SelectMany(o => o.Items));

        // The anchor floor is the one exception: a rationed metal floor is the same item
        // the anchor is made of, and that is fine, because the anchor is protected from
        // salvage and damage by id wherever it stands.
        foreach (SiegeItem item in handedOut) {
            Assert.NotEqual(config.AnchorMarkerItemId, item.ItemId);
        }
    }
}
