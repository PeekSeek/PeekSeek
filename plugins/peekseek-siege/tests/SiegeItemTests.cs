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

    /// Nobody is given a floor, of any material, in either tier. A team that could lay
    /// its own floor could start a second base beside the anchor and pick whichever one
    /// suited the round; with only roofs to build upwards on, the anchor is the only
    /// foundation there is. 369 and 51 are the metal and birch floors.
    [Fact]
    public void NobodyIsGivenAFloor() {
        PeekSeekSiegeConfiguration config = new();
        config.LoadDefaults();

        foreach (ushort floor in new ushort[] { 369, 51 }) {
            Assert.DoesNotContain(config.RationedSupplies, item => item.ItemId == floor);
            Assert.DoesNotContain(config.PlentifulSupplies, item => item.ItemId == floor);
        }
    }

    /// Brick is the scarce material, so none of it may appear in the list that is topped
    /// back up every few seconds. The brick wall matters most: it is what a rocket has to
    /// be spent on. Doors and hatches are rationed for the same reason.
    [Fact]
    public void BrickAndDoorsAreNeverPlentiful() {
        PeekSeekSiegeConfiguration config = new();
        config.LoadDefaults();

        foreach (ushort scarce in new ushort[] { 1210, 1212, 1215, 1216, 1984, 378, 1332 }) {
            Assert.DoesNotContain(config.PlentifulSupplies, item => item.ItemId == scarce);
            Assert.Contains(config.RationedSupplies, item => item.ItemId == scarce);
        }
    }

    /// Attackers may not be given anything that has to be placed. They are in a different
    /// group from the site they are attacking, so the defenders' claim bubble refuses them
    /// every placement inside it — which is the whole base — and a placed charge would
    /// simply never go down. 519 is the rocket launcher and 1100 is C4, both of which are
    /// carried rather than placed.
    [Fact]
    public void RaidersCarryNothingThatMustBePlaced() {
        PeekSeekSiegeConfiguration config = new();
        config.LoadDefaults();

        ushort[] placeable = [
            1215,
            1212,
            1210,
            1216,
            1984,
            57,
            49,
            59,
            53,
            55,
            318,
            378,
            1332,
            1158,
            1234
        ];

        foreach (SiegeItem item in config.RaidOptions.SelectMany(option => option.Items)) {
            Assert.DoesNotContain(placeable, id => id == item.ItemId);
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
