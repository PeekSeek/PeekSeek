using System;
using System.Collections.Generic;
using System.Linq;
using PeekSeekSiege.Core.Models;
using SDG.Unturned;
using Steamworks;
using UnityEngine;
using Logger = Rocket.Core.Logging.Logger;

namespace PeekSeekSiege.Core;

/// Everything Siege does to the world, and the gate that decides what players may do
/// to it back.
///
/// The gamemode above decides what should be true; this is where it becomes true in
/// Unturned. It is also the single place that answers "may this be placed here" and
/// "may this be taken down", because those two questions are asked from three
/// different Unturned events and a rule that lived in each of them separately would
/// drift the first time one was edited.
internal static class SiegeWorld {
    private static Siege siege;

    internal static void Hook(Siege running) {
        siege = running;

        StructureManager.onDeployStructureRequested += OnDeployStructure;
        BarricadeManager.onDeployBarricadeRequested += OnDeployBarricade;
        StructureDrop.OnSalvageRequested_Global += OnSalvageStructure;
        BarricadeDrop.OnSalvageRequested_Global += OnSalvageBarricade;
        StructureManager.onDamageStructureRequested += OnDamageStructure;
        BarricadeManager.onDamageBarricadeRequested += OnDamageBarricade;
    }

    internal static void Unhook() {
        StructureManager.onDeployStructureRequested -= OnDeployStructure;
        BarricadeManager.onDeployBarricadeRequested -= OnDeployBarricade;
        StructureDrop.OnSalvageRequested_Global -= OnSalvageStructure;
        BarricadeDrop.OnSalvageRequested_Global -= OnSalvageBarricade;
        StructureManager.onDamageStructureRequested -= OnDamageStructure;
        BarricadeManager.onDamageBarricadeRequested -= OnDamageBarricade;

        siege = null;
    }

    // ---- the anchor -----------------------------------------------------------

    /// Drop the one piece of a site that is not part of anybody's base: a metal floor
    /// with a claim flag standing on it, in the middle of the plot.
    ///
    /// Both go down owned by nobody and in no group, which in Unturned means nobody can
    /// salvage them and nobody can move them. That is the point. A team that could pick
    /// its anchor up could carry it to a corner and build a base the round system never
    /// intended; a team that cannot has to build off this floor, which is what makes
    /// "one base per site" a fact about the world rather than a rule anybody has to
    /// enforce.
    internal static void PlaceAnchor(SiegeSite site, ulong teamGroup) {
        Vector3 anchor = site.AnchorPoint;

        // Unturned's structure and barricade prefabs are authored facing forward and
        // laid down by a quarter turn on X, so an upright or flat piece is
        // Euler(-90, yaw, 0). Euler(0, yaw, 0) is what stands a floor on its edge.
        Quaternion placed = Quaternion.Euler(-90f, site.Yaw, 0f);

        if (Assets.find(EAssetType.ITEM, PeekSeekSiege.Config.AnchorFloorItemId) is ItemStructureAsset floor) {
            StructureManager.dropReplicatedStructure(
                new Structure(floor, floor.health), anchor, placed, 0, 0);
        }

        // Barricades standing on the ground are rotated a quarter turn off the
        // structure convention: an upright one is Euler(-90, yaw, 0), and Euler(0, ...)
        // lays it on its side. The lift clears the anchor floor's own thickness, which
        // is what was burying it up to the waist.
        // The marker goes down in the team's group, and that is not the same thing as
        // giving it to them.
        //
        // A claim flag registers a 32m bubble, and ClaimManager.checkCanBuild lets you
        // build inside one only if you match its owner or its group — so a bubble owned
        // by nobody blocks everybody, including the team whose site it marks. The client
        // builds that bubble itself from the replicated barricade, so no server-side
        // patch can soften it. The group is therefore the only whitelist there is, and
        // it is the one the site's own team needs.
        //
        // Salvage is a separate question with a separate answer: it is decided from the
        // barricade's data, and OnSalvage/OnDamage below refuse both by item id, for
        // everybody, in every phase. So the team can build around their flag and still
        // cannot take it down or move it, which is what the group was originally being
        // withheld to prevent.
        if (Assets.find(EAssetType.ITEM, PeekSeekSiege.Config.AnchorMarkerItemId) is ItemBarricadeAsset marker) {
            BarricadeManager.dropNonPlantedBarricade(
                new Barricade(marker, marker.health, marker.getState()),
                // `offset` on a barricade asset is how far its pivot sits above the
                // ground it is standing on — 1.12 on a claim flag, 5 on a metal flag —
                // so it is the correction that stops a marker being buried up to the
                // waist, whatever marker somebody configures. The extra clears the
                // anchor floor's own thickness.
                anchor + Vector3.up * (marker.offset + AnchorFloorClearance),
                placed,
                0,
                teamGroup);
        }
    }

    /// Half the anchor floor's thickness, so things resting on it start at its surface
    /// rather than at its middle.
    private const float AnchorFloorClearance = 0.25f;

    /// Somewhere to stand on a site: on top of the anchor floor, facing the way the
    /// site faces. Used for build-phase spawns, the leash, and defender spawns, all of
    /// which want the same answer.
    internal static Vector3 StandingSpot(SiegeSite site) {
        return site.AnchorPoint + Vector3.up * 1.5f;
    }

    /// Drop a point onto whatever is under it.
    ///
    /// Attacker spawns are worked out as a bearing and a distance from the site anchor,
    /// which is flat arithmetic on a map that is not flat. Seventy metres out from a
    /// site on Alpha Valley can be well above or well below the anchor, and teleporting
    /// somebody into the middle of a hillside leaves them stuck in it. The ray starts
    /// high and takes the first ground it finds; if it finds none, the point is used as
    /// calculated, because falling is recoverable and being inside a hill is not.
    internal static Vector3 OnGround(Vector3 point) {
        Vector3 sky = new(point.x, point.y + 200f, point.z);

        return Physics.Raycast(sky, Vector3.down, out RaycastHit hit, 400f, RayMasks.BLOCK_STANCE)
            ? hit.point + Vector3.up * 0.5f
            : point;
    }

    // ---- may I build here -----------------------------------------------------

    /// Whether a point belongs to either site at all. The claim patch asks this first,
    /// so that everywhere else on the map keeps Unturned's own answer.
    internal static bool IsOnASite(Vector3 point) {
        return PeekSeekSiege.Config.Sites.Any(site => site.Contains(point));
    }

    /// Whether anything may be placed at a point right now.
    ///
    /// During the build phase each team may build on its own plot and nowhere else.
    /// During a round the only plot anybody may touch is the one being defended, which
    /// covers a defender putting down wire and an attacker putting a charge on a wall
    /// without either of them needing a separate rule. Between phases, nothing.
    internal static bool MayBuildAt(Vector3 point) {
        if (siege == null) return true;

        List<SiegeSite> sites = PeekSeekSiege.Config.Sites;

        if (siege.Phase == SiegePhase.Building) {
            return sites.Any(site => site.Contains(point));
        }

        if (siege.Phase == SiegePhase.Playing) {
            // Asked of the gamemode rather than worked out from the round number, so
            // that solo — which has one base and fights every round over it — opens the
            // same plot the round is actually being played on.
            return sites[siege.DefendedSite].Contains(point);
        }

        return false;
    }

    private static void OnDeployStructure(
        Structure structure, ItemStructureAsset asset, ref Vector3 point,
        ref float angleX, ref float angleY, ref float angleZ,
        ref ulong owner, ref ulong group, ref bool shouldAllow) {
        if (!MayBuildAt(point)) shouldAllow = false;
    }

    private static void OnDeployBarricade(
        Barricade barricade, ItemBarricadeAsset asset, Transform hit, ref Vector3 point,
        ref float angleX, ref float angleY, ref float angleZ,
        ref ulong owner, ref ulong group, ref bool shouldAllow) {
        if (!MayBuildAt(point)) shouldAllow = false;
    }

    // ---- may I take this down -------------------------------------------------

    /// The anchor floor and its claim flag are never salvageable, by anybody, in any
    /// phase.
    ///
    /// They already go down owned by nobody, which is enough on its own — Unturned will
    /// not let a stranger salvage an unowned piece. This is the second lock, because
    /// the cost of being wrong is a team that can relocate its own site mid-match, and
    /// the cost of the check is one comparison.
    private static void OnSalvageStructure(StructureDrop drop, SteamPlayer instigator, ref bool shouldAllow) {
        if (IsFixture(drop.asset)) shouldAllow = false;
    }

    private static void OnSalvageBarricade(BarricadeDrop drop, SteamPlayer instigator, ref bool shouldAllow) {
        if (IsFixture(drop.asset)) shouldAllow = false;
    }

    /// The anchor floor is never shootable, and the marker is shootable only while a
    /// round is being played.
    ///
    /// The marker is the objective: attackers win a round by bringing the flag down, and
    /// defenders win by keeping it up until the clock runs out. So it has to be
    /// destructible exactly when that race is on, and indestructible the rest of the
    /// time — during the build phase it is the thing every base is measured from, and a
    /// stray shot into it would move the site.
    private static bool MarkerIsDestructible => siege != null && siege.Phase == SiegePhase.Playing;

    private static void OnDamageStructure(
        CSteamID instigator, Transform structure, ref ushort damage,
        ref bool shouldAllow, EDamageOrigin origin) {
        if (IsFixture(StructureManager.FindStructureByRootTransform(structure)?.asset)) shouldAllow = false;
    }

    private static void OnDamageBarricade(
        CSteamID instigator, Transform barricade, ref ushort damage,
        ref bool shouldAllow, EDamageOrigin origin) {
        ItemAsset asset = BarricadeManager.FindBarricadeByRootTransform(barricade)?.asset;
        if (asset == null) return;

        if (asset.id == PeekSeekSiege.Config.AnchorMarkerItemId) {
            if (!MarkerIsDestructible) shouldAllow = false;
            return;
        }

        if (IsFixture(asset)) shouldAllow = false;
    }

    /// Whether a site's marker is still standing. This is the round's win condition, so
    /// it is asked once a second while a round runs.
    internal static bool MarkerStands(Vector3 anchor) {
        List<RegionCoordinate> around = [];
        Regions.getRegionsInRadius(anchor, MarkerSearchRadius, around);

        foreach (RegionCoordinate coordinate in around) {
            foreach (BarricadeDrop drop in BarricadeManager.regions[coordinate.x, coordinate.y].drops) {
                if (drop.asset == null || drop.asset.id != PeekSeekSiege.Config.AnchorMarkerItemId) continue;
                if ((drop.GetServersideData().point - anchor).sqrMagnitude > MarkerSearchRadius * MarkerSearchRadius) continue;

                return true;
            }
        }

        return false;
    }

    /// Generous enough to find a marker that settled slightly off the anchor, tight
    /// enough not to find the other site's.
    private const float MarkerSearchRadius = 8f;

    private static bool IsFixture(ItemAsset asset) {
        return asset != null
            && (asset.id == PeekSeekSiege.Config.AnchorFloorItemId
                || asset.id == PeekSeekSiege.Config.AnchorMarkerItemId);
    }

    // ---- standing on something ------------------------------------------------

    /// Whether a player is standing on something that was built rather than on the map.
    ///
    /// A short ray straight down from just above the feet. Barricades and structures
    /// both count, so a defender on a metal floor, on a birch ramp, or on top of a
    /// crate they placed is all equally "on the base" — the rule players need to
    /// internalise is "get off the dirt", and anything finer than that would be a rule
    /// they cannot see.
    internal static bool IsStandingOnBuilt(Player player) {
        Vector3 origin = player.transform.position + Vector3.up * 0.4f;

        return Physics.Raycast(
            origin, Vector3.down, 1.2f, RayMasks.BARRICADE | RayMasks.STRUCTURE);
    }

    /// Give a player hands big enough to carry a building.
    ///
    /// Unturned does replicate a page resize, contrary to what the shape of the API
    /// suggests: Items.resize fires PlayerInventory.onItemsResized, which sends SendSize
    /// to that player's own client, so the server can set a page to whatever it likes and
    /// the client will draw it. The hands page is the one worth growing — it is always
    /// there, unlike the backpack and vest pages, which the game resizes back to the worn
    /// asset every time somebody changes clothes.
    ///
    /// Re-applied whenever supplies are, because equipping clothing rebuilds the pages.
    internal static void ExpandHands(Player player, byte width, byte height) {
        Items hands = player.inventory.items[HandsPage];
        if (hands == null || (hands.width == width && hands.height == height)) return;

        hands.resize(width, height);
    }

    /// Pages 0 and 1 are the two weapon slots, so the general-purpose page a player
    /// carries things in is the one straight after them.
    private static readonly byte HandsPage = PlayerInventory.SLOTS;

    // ---- kit ------------------------------------------------------------------

    /// Everything a player carries into a round: their saved kit, the gamemode's
    /// default items, and the one option they picked off the board.
    internal static void Kit(Player player, SiegeOption option) {
        ClearInventory(player);

        // Clothing before anything else, and before the site's kit gets a say. A kit
        // that has no outfit in it — or one that never arrived because the site was
        // unreachable — would otherwise leave somebody naked with no inventory space,
        // which is what "my gear is gone" looks like from the inside.
        GiveGear(player, PeekSeekSiege.Config.BuildGear, clothingOnly: true);

        ulong steam64 = player.channel.owner.playerID.steamID.m_SteamID;
        foreach (SiegeItem item in RemoteAPI.KitFor(steam64)) Give(player, item);

        Give(player, PeekSeekSiege.Config.DefaultItems);
        if (option != null) Give(player, option.Items);

        ExpandHands(player, PeekSeekSiege.Config.HandsWidth, PeekSeekSiege.Config.HandsHeight);

        // Passive regeneration is off on PeekSeek servers, so a round is the only
        // moment anybody gets health back. Kitting is what "a round is starting" means
        // here, so it is where the top-up belongs.
        player.life.serverModifyHealth(100);
    }

    internal static void Give(Player player, IEnumerable<SiegeItem> items) {
        // Clothing first, always. Inventory space in Unturned comes from what you are
        // wearing, so handing somebody forty walls before their backpack puts most of it
        // on the floor. The kit the site returns lists a gun and an outfit in no
        // particular order, so the ordering is imposed here rather than assumed.
        foreach (SiegeItem item in items.OrderBy(item => IsClothing(item.ItemId) ? 0 : 1)) {
            Give(player, item);
        }
    }

    /// Top a builder back up to the working set they are meant to be carrying.
    ///
    /// This counts what they already hold and adds only the difference, rather than
    /// handing over another full set. Inventory space in Unturned comes from the worn
    /// clothing asset and cannot be enlarged from a plugin — the client sizes each page
    /// from the backpack it was told to wear, so a server-side resize only desyncs — and
    /// an Alicepack does not hold forty walls. Keeping a small set continuously topped
    /// up gives the same "never run out" feeling as a huge inventory would, without
    /// needing one, and without pouring the overflow onto the floor.
    ///
    /// So `Amount` on a build supply means "how many you keep on you", not "how many you
    /// are given".
    internal static void TopUp(Player player, IEnumerable<SiegeItem> wanted) {
        foreach (SiegeItem item in wanted) {
            int held = CountHeld(player, item.ItemId);

            for (int i = held; i < item.Amount; i++) {
                player.inventory.forceAddItemAuto(new Item(item.ItemId, true), false, false, false);
            }
        }
    }

    /// Hand over a fixed allowance, in full, without counting what is already held.
    /// Used for the rationed half of the supplies, which is issued once and is meant to
    /// run out.
    internal static void Issue(Player player, IEnumerable<SiegeItem> allowance) {
        foreach (SiegeItem item in allowance) {
            for (byte i = 0; i < item.Amount; i++) {
                player.inventory.forceAddItemAuto(new Item(item.ItemId, true), false, false, false);
            }
        }
    }

    private static int CountHeld(Player player, ushort itemId) {
        int held = 0;

        for (byte page = 0; page < PlayerInventory.PAGES - 1; page++) {
            Items items = player.inventory.items[page];
            if (items == null) continue;

            for (byte i = 0; i < items.getItemCount(); i++) {
                if (items.getItem(i)?.item?.id == itemId) held++;
            }
        }

        return held;
    }

    /// Hand over what a builder wears and holds, named rather than numbered.
    ///
    /// These are looked up by the name a player sees on the item, because they are
    /// incidental to the gamemode and their ids are not worth anybody maintaining: a
    /// backpack is "whatever holds the most", not a specific number. A name that matches
    /// nothing is logged once and skipped, so a typo is visible instead of silent.
    internal static void GiveGear(Player player, List<string> names, bool clothingOnly = false) {
        foreach (string name in names) {
            ItemAsset asset = ByName(name);
            if (asset == null) continue;
            if (clothingOnly && asset is not ItemClothingAsset) continue;

            // Only what they are missing. This runs whenever somebody is placed, and a
            // builder who already has their pack does not want a second one on the floor.
            if (CountHeld(player, asset.id) > 0 || IsWorn(player, asset.id)) continue;

            player.inventory.forceAddItemAuto(new Item(asset.id, true), true, true, true);
        }
    }

    /// Clothing that has been equipped is no longer in the inventory, so counting slots
    /// alone would hand out a second backpack to somebody already wearing the first.
    private static bool IsWorn(Player player, ushort itemId) {
        PlayerClothing worn = player.clothing;

        return worn.backpack == itemId || worn.vest == itemId || worn.shirt == itemId
            || worn.pants == itemId || worn.hat == itemId || worn.mask == itemId
            || worn.glasses == itemId;
    }

    private static readonly Dictionary<string, ItemAsset> ByNameCache = [];

    private static ItemAsset ByName(string name) {
        if (ByNameCache.TryGetValue(name, out ItemAsset cached)) return cached;

        List<ItemAsset> all = [];
        Assets.find(all);

        ItemAsset found = all.Find(
            item => string.Equals(item.itemName, name, StringComparison.OrdinalIgnoreCase));

        if (found == null) {
            Logger.LogWarning($"No item is called \"{name}\". Check BuildGear in the config.");
        }

        ByNameCache[name] = found;
        return found;
    }

    private static bool IsClothing(ushort itemId) {
        return Assets.find(EAssetType.ITEM, itemId) is ItemClothingAsset;
    }

    private static void Give(Player player, SiegeItem item) {
        byte[] state = item.StateBytes();

        for (byte i = 0; i < item.Amount; i++) {
            Item created = new(item.ItemId, true);
            if (state.Length > 0) created.state = state;

            // Auto-equip rather than just adding: a kit's outfit has to end up worn for
            // the player to have any inventory space at all, and a gun in the bag is not
            // a gun in your hands.
            player.inventory.forceAddItemAuto(created, true, true, true);
        }
    }

    /// Strip a player back to nothing, clothes included.
    ///
    /// Clothing goes too because a kit sets it, and a player who kept a backpack from
    /// the last round would carry last round's option into this one — which is exactly
    /// the thing the board is supposed to make a decision.
    internal static void ClearInventory(Player player) {
        for (byte page = 0; page < PlayerInventory.PAGES - 1; page++) {
            byte count = player.inventory.getItemCount(page);
            for (byte i = 0; i < count; i++) player.inventory.removeItem(page, 0);
        }

        player.clothing.updateClothes(
            0, 0, [], 0, 0, [], 0, 0, [], 0, 0, [], 0, 0, [], 0, 0, [], 0, 0, []);
    }
}
