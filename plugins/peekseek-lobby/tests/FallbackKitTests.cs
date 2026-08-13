using System.Collections.Generic;
using PeekSeekLobby.Core.Models;
using Xunit;

namespace PeekSeekLobby.Tests;

public class FallbackKitTests {
    [Fact]
    public void ToLoadout_ParsesTheCommaSeparatedStateTheWebsiteEncodes() {
        var kit = new FallbackKit {
            Category = "meta",
            Items = [new FallbackItem { ItemID = 363, Amount = 1, State = "148,0,239,3" }]
        };

        KitLoadout loadout = kit.ToLoadout();

        Assert.Single(loadout.Items);
        Assert.Equal((ushort)363, loadout.Items[0].ItemID);
        Assert.Equal(new byte[] { 148, 0, 239, 3 }, loadout.Items[0].State);
    }

    [Fact]
    public void ToLoadout_EmptyStateYieldsEmptyByteArray() {
        var kit = new FallbackKit {
            Items = [new FallbackItem { ItemID = 309, Amount = 1, State = "" }]
        };

        Assert.Empty(kit.ToLoadout().Items[0].State);
    }

    [Fact]
    public void ToLoadout_NullMagazineStaysNull() {
        var kit = new FallbackKit { Items = [], Magazine = null };

        Assert.Null(kit.ToLoadout().Magazine);
    }

    [Fact]
    public void ToLoadout_CarriesTheMagazineAmountThroughForRestocking() {
        var kit = new FallbackKit {
            Items = [],
            Magazine = new FallbackItem { ItemID = 6, Amount = 5, State = "" }
        };

        PSItem magazine = kit.ToLoadout().Magazine;

        Assert.Equal((ushort)6, magazine.ItemID);
        Assert.Equal((byte)5, magazine.Amount);
    }

    /// XmlSerializer leaves a list unset when the config file predates the field,
    /// so a null Items must not throw on a server running an older config.
    [Fact]
    public void ToLoadout_NullItemsYieldsAnEmptyListRatherThanThrowing() {
        var kit = new FallbackKit { Category = "meta", Items = null };

        Assert.Empty(kit.ToLoadout().Items);
    }

    [Fact]
    public void LoadDefaults_ShipsAWorkingMetaFallback() {
        var config = new PeekSeekLobbyConfiguration();

        config.LoadDefaults();

        Assert.NotNull(config.FallbackKits);
        FallbackKit meta = config.FallbackKits.Find(kit => kit.Category == "meta");
        Assert.NotNull(meta);
        Assert.NotEmpty(meta.Items);
        Assert.NotNull(meta.Magazine);
        // A gun and every outfit slot, or players spawn half-dressed.
        Assert.Equal(6, meta.Items.Count);
    }

    /// Every fallback gun needs its full attachment/ammo state. The sniper
    /// fallback originally shipped with an empty one, which spawned the rifle in
    /// safety mode with no sight and no ammo — visibly broken and unshootable.
    [Fact]
    public void LoadDefaults_EveryFallbackGunCarriesFullWeaponState() {
        var config = new PeekSeekLobbyConfiguration();

        config.LoadDefaults();

        foreach (FallbackKit kit in config.FallbackKits) {
            string[] state = kit.Items[0].State.Split(',');
            Assert.True(state.Length == 18,
                $"{kit.Category} fallback gun has {state.Length} state bytes, expected 18");
            Assert.NotNull(kit.Magazine);
        }
    }

    [Fact]
    public void LoadDefaults_CoversEveryAllowedKitCategory() {
        var config = new PeekSeekLobbyConfiguration();

        config.LoadDefaults();

        foreach (string category in config.AllowedKitCategories) {
            Assert.Contains(config.FallbackKits, kit => kit.Category == category);
        }
    }
}
