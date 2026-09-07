using System.Collections.Generic;
using System.Linq;
using PeekSeekSiege.Core.Models;
using SDG.Unturned;
using Steamworks;
using UnityEngine;

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
    }

    internal static void Unhook() {
        StructureManager.onDeployStructureRequested -= OnDeployStructure;
        BarricadeManager.onDeployBarricadeRequested -= OnDeployBarricade;
        StructureDrop.OnSalvageRequested_Global -= OnSalvageStructure;
        BarricadeDrop.OnSalvageRequested_Global -= OnSalvageBarricade;

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
    internal static void PlaceAnchor(SiegeSite site) {
        Vector3 anchor = site.AnchorPoint;
        Quaternion flat = Quaternion.Euler(0f, site.Yaw, 0f);

        if (Assets.find(EAssetType.ITEM, PeekSeekSiege.Config.AnchorFloorItemId) is ItemStructureAsset floor) {
            StructureManager.dropReplicatedStructure(
                new Structure(floor, floor.health), anchor, flat, 0, 0);
        }

        if (Assets.find(EAssetType.ITEM, PeekSeekSiege.Config.AnchorClaimFlagItemId) is ItemBarricadeAsset flag) {
            BarricadeManager.dropNonPlantedBarricade(
                new Barricade(flag, flag.health, flag.getState()),
                anchor + Vector3.up * 0.1f,
                flat,
                0,
                0);
        }
    }

    /// Somewhere to stand on a site: on top of the anchor floor, facing the way the
    /// site faces. Used for build-phase spawns, the leash, and defender spawns, all of
    /// which want the same answer.
    internal static Vector3 StandingSpot(SiegeSite site) {
        return site.AnchorPoint + Vector3.up * 1.5f;
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
            SiegeSite defended = sites[RoundOrder.Defender(siege.Round)];
            return defended.Contains(point);
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

    private static bool IsFixture(ItemAsset asset) {
        return asset != null
            && (asset.id == PeekSeekSiege.Config.AnchorFloorItemId
                || asset.id == PeekSeekSiege.Config.AnchorClaimFlagItemId);
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

    // ---- kit ------------------------------------------------------------------

    /// Everything a player carries into a round: their saved kit, the gamemode's
    /// default items, and the one option they picked off the board.
    internal static void Kit(Player player, SiegeOption option) {
        ClearInventory(player);

        ulong steam64 = player.channel.owner.playerID.steamID.m_SteamID;
        foreach (SiegeItem item in RemoteAPI.KitFor(steam64)) Give(player, item);

        Give(player, PeekSeekSiege.Config.DefaultItems);
        if (option != null) Give(player, option.Items);

        // Passive regeneration is off on PeekSeek servers, so a round is the only
        // moment anybody gets health back. Kitting is what "a round is starting" means
        // here, so it is where the top-up belongs.
        player.life.serverModifyHealth(100);
    }

    internal static void Give(Player player, IEnumerable<SiegeItem> items) {
        foreach (SiegeItem item in items) Give(player, item);
    }

    private static void Give(Player player, SiegeItem item) {
        byte[] state = item.StateBytes();

        for (byte i = 0; i < item.Amount; i++) {
            Item created = new(item.ItemId, true);
            if (state.Length > 0) created.state = state;

            player.inventory.forceAddItem(created, true);
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
