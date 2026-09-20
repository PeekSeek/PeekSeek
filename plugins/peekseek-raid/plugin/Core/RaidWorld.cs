using System;
using System.Collections.Generic;
using System.Linq;
using PeekSeek.Gamemodes;
using PeekSeekRaid.Core.Models;
using SDG.Unturned;
using Steamworks;
using UnityEngine;
using Logger = Rocket.Core.Logging.Logger;

namespace PeekSeekRaid.Core;

/// Everything Raid does to the world, and the gate that decides what players may do
/// to it back.
///
/// The gamemode above decides what should be true; this is where it becomes true in
/// Unturned. It is also the single place that answers "may this be placed here" and
/// "may this be taken down", because those two questions are asked from three
/// different Unturned events and a rule that lived in each of them separately would
/// drift the first time one was edited.
internal static class RaidWorld {
    private static Raid raid;

    /// Where each site's starting floors stand, by site index, set when the anchor
    /// goes down. A floor on one of these spots is the site itself: it can change
    /// material but never be taken away or shot out, whatever it is made of now.
    private static readonly Dictionary<int, List<Vector3>> anchorTiles = [];

    internal static void Hook(Raid running) {
        raid = running;
        TuneExplosives();

        StructureManager.onDeployStructureRequested += OnDeployStructure;
        BarricadeManager.onDeployBarricadeRequested += OnDeployBarricade;
        StructureManager.onStructureSpawned += OnStructureSpawned;
        BarricadeManager.onBarricadeSpawned += OnBarricadeSpawned;
        BarricadeManager.onOpenStorageRequested += OnOpenStorage;
        StructureDrop.OnSalvageRequested_Global += OnSalvageStructure;
        BarricadeDrop.OnSalvageRequested_Global += OnSalvageBarricade;
        StructureManager.onDamageStructureRequested += OnDamageStructure;
        BarricadeManager.onDamageBarricadeRequested += OnDamageBarricade;
    }

    internal static void Unhook() {
        StructureManager.onDeployStructureRequested -= OnDeployStructure;
        BarricadeManager.onDeployBarricadeRequested -= OnDeployBarricade;
        StructureManager.onStructureSpawned -= OnStructureSpawned;
        BarricadeManager.onBarricadeSpawned -= OnBarricadeSpawned;
        BarricadeManager.onOpenStorageRequested -= OnOpenStorage;
        StructureDrop.OnSalvageRequested_Global -= OnSalvageStructure;
        BarricadeDrop.OnSalvageRequested_Global -= OnSalvageBarricade;
        StructureManager.onDamageStructureRequested -= OnDamageStructure;
        BarricadeManager.onDamageBarricadeRequested -= OnDamageBarricade;

        anchorTiles.Clear();
        raid = null;
    }

    /// What a charge and a rocket do to a building, set on the game's own assets so
    /// every explosion the server works out uses these numbers. Explosions are the
    /// server's to compute, so a client's copy of the asset never matters.
    ///
    /// The point is the arithmetic a raider does at the wall: a charge is two to a
    /// metal wall and one to wood; a rocket is three to metal and two to wood, and
    /// its blast is tightened so a rocket is a hole in one wall rather than a room.
    /// Set every match rather than once, because the numbers are config. The range is
    /// read-only on the asset, so its backing field is written; the publicizer opens
    /// it up.
    private static void TuneExplosives() {
        if (Assets.find(EAssetType.ITEM, PeekSeekRaid.Config.ChargeItemId) is ItemChargeAsset charge) {
            charge.structureDamage = PeekSeekRaid.Config.ChargeStructureDamage;
        }

        if (Assets.find(EAssetType.ITEM, PeekSeekRaid.Config.RocketItemId) is ItemMagazineAsset rocket) {
            rocket.structureDamage = PeekSeekRaid.Config.RocketStructureDamage;
            rocketRange ??= rocket.range;
            rocket._range = rocketRange.Value * PeekSeekRaid.Config.RocketRangeMultiplier;
        }
    }

    /// The rocket's blast radius as the game shipped it, kept so the multiplier is
    /// applied to the original every time and not to the last result.
    private static float? rocketRange;

    // ---- the anchor -----------------------------------------------------------

    /// Drop what a site is before anybody builds: the metal floors of the shape the team
    /// chose.
    ///
    /// The floors go down owned by nobody and in no group, which in Unturned means
    /// nobody can salvage them and nobody can move them. That is the point. A team that
    /// could pick its anchor up could carry it to a corner and build a base the round
    /// system never intended; a team that cannot has to build off this floor, which is
    /// what makes "one base per site" a fact about the world rather than a rule anybody
    /// has to enforce. Where each piece goes is BaseLayout's, which is the housing grid
    /// the game links on; here it is only turned to face the site and dropped.
    internal static void PlaceAnchor(int siteIndex, RaidSite site, BaseShape shape) {
        ItemStructureAsset square = Assets.find(EAssetType.ITEM, PeekSeekRaid.Config.AnchorFloorItemId) as ItemStructureAsset;
        ItemStructureAsset triangle = Assets.find(EAssetType.ITEM, PeekSeekRaid.Config.AnchorTriangleItemId) as ItemStructureAsset;

        List<Vector3> tiles = [];
        foreach (FloorPiece piece in BaseLayout.Floors(shape)) {
            ItemStructureAsset floor = piece.Triangle ? triangle : square;
            if (floor == null) continue;

            Vector3 point = OnSite(site, piece.X, 0f, piece.Z);
            tiles.Add(point);
            StructureManager.dropReplicatedStructure(
                new Structure(floor, floor.health), point, Facing(site, piece.Yaw), 0, 0);
        }

        anchorTiles[siteIndex] = tiles;
    }

    /// Two maple pillars, one on the other, on every corner of every anchor floor, once,
    /// when the build phase opens: the frame of a two-storey base. The game puts a
    /// pillar at a floor vertex half a wall above the floor's plane
    /// (HousingConnections.FindEmptyPillarSlot), and the next one a wall higher on the
    /// same vertex, and links within two centimetres, so that is exactly where these
    /// go: anything else is a pillar nothing can attach to. Unlike the floors these
    /// belong to the team: they are placed in its group, so a team whose plan wants
    /// that corner clear can salvage one, and they are captured with the base like
    /// anything else the team left standing. The one corner every triangle of a
    /// hexagon shares is the middle, and nothing stands there.
    internal static void PlacePillars(RaidSite site, ulong teamGroup, BaseShape shape) {
        if (Assets.find(EAssetType.ITEM, PeekSeekRaid.Config.AnchorPillarItemId) is not ItemStructureAsset pillar) return;

        foreach ((float x, float z, float yaw) in BaseLayout.Vertices(shape)) {
            if (Mathf.Abs(x) < BaseLayout.LinkTolerance && Mathf.Abs(z) < BaseLayout.LinkTolerance) continue;

            for (int storey = 0; storey < PillarStoreys; storey++) {
                StructureManager.dropReplicatedStructure(
                    new Structure(pillar, pillar.health),
                    OnSite(site, x, BaseLayout.HalfWallHeight + storey * BaseLayout.WallHeight, z),
                    Facing(site, yaw),
                    0,
                    teamGroup);
            }
        }
    }

    private const int PillarStoreys = 2;

    /// Whether a floor stands on one of a site's starting spots.
    internal static bool IsAnchorTile(int siteIndex, StructureDrop drop) {
        if (!anchorTiles.TryGetValue(siteIndex, out List<Vector3> tiles) || !IsFloor(drop.asset)) return false;

        Vector3 at = drop.GetServersideData().point;
        return tiles.Any(tile => (tile - at).sqrMagnitude < BaseLayout.LinkTolerance * BaseLayout.LinkTolerance);
    }

    // ---- the flag ---------------------------------------------------------------

    /// Stand the flag on a starting tile nobody has built on, when the defender who
    /// was meant to place it did not. Tiles are tried in a random order, and a tile
    /// counts as free when a sphere the size of a player just above it touches no wall,
    /// pillar or barricade and no door is near; the middle tile is used regardless if
    /// every one is busy.
    internal static void PlaceFlag(int siteIndex, RaidSite site, BaseShape shape, ulong teamGroup, string name) {
        if (Assets.find(EAssetType.ITEM, PeekSeekRaid.Config.AnchorMarkerItemId) is not ItemBarricadeAsset marker) return;
        if (!anchorTiles.TryGetValue(siteIndex, out List<Vector3> tiles)) return;

        System.Random random = new();
        Vector3 chosen = tiles.OrderBy(_ => random.Next())
            .FirstOrDefault(tile => IsClear(tile) && !NearDoor(tile));
        if (chosen == default) chosen = site.AnchorPoint;

        // `offset` on a barricade asset is how far its pivot sits above the ground it
        // stands on, so it is the correction that stops the flag being buried up to
        // the waist. The extra clears the anchor floor's own thickness.
        BarricadeManager.dropNonPlantedBarricade(
            new Barricade(marker, marker.health, marker.getState()),
            chosen + Vector3.up * (marker.offset + AnchorFloorClearance),
            Facing(site, 0f),
            0,
            teamGroup);
    }

    /// Whether the flag may go here: on one of the site's starting tiles, on that tile's
    /// own level, and not within reach of a door.
    ///
    /// The tiles are the whole of it. A flag on the roof, in a corner of a wall, or on a
    /// crate is a flag the attackers cannot get a charge onto; a flag on a starting tile
    /// is somewhere in the building's own footprint. And the door rule is not about
    /// doors: stand the flag in a doorway, then shut the door on it, and the flag is
    /// inside the door, where nothing reaches it. Two and a half metres is a door and a
    /// half from its hinge.
    internal static bool MayStandFlag(int siteIndex, Vector3 point) {
        if (!anchorTiles.TryGetValue(siteIndex, out List<Vector3> tiles)) return false;

        bool onATile = tiles.Any(tile =>
            Mathf.Abs(point.x - tile.x) <= BaseLayout.HalfEdge
            && Mathf.Abs(point.z - tile.z) <= BaseLayout.HalfEdge
            && point.y - tile.y >= 0f
            && point.y - tile.y <= FlagAboveTile);

        return onATile && !NearDoor(point);
    }

    /// How far above a starting tile's pivot the flag's own pivot may be and still be on
    /// that tile: the floor's thickness plus the flag's offset and a little.
    private const float FlagAboveTile = 1f;

    private const float DoorClearance = 2.5f;

    /// Whether a door, hatch or gate stands within DoorClearance of a point.
    internal static bool NearDoor(Vector3 point) {
        List<RegionCoordinate> around = [];
        Regions.getRegionsInRadius(point, DoorClearance, around);

        foreach (RegionCoordinate coordinate in around) {
            foreach (BarricadeDrop drop in BarricadeManager.regions[coordinate.x, coordinate.y].drops) {
                if (drop.asset is not ItemBarricadeAsset barricade) continue;
                if (barricade.build != EBuild.DOOR && barricade.build != EBuild.HATCH && barricade.build != EBuild.GATE) continue;
                if ((drop.GetServersideData().point - point).sqrMagnitude <= DoorClearance * DoorClearance) return true;
            }
        }

        return false;
    }

    /// Whether a player could stand on a tile: a capsule their size just above it
    /// touches no wall, pillar, barricade or other player. The tile's own floor sits
    /// below the capsule, so it does not count.
    private static bool IsClear(Vector3 tile) {
        return !Physics.CheckCapsule(tile + Vector3.up * 0.7f, tile + Vector3.up * 1.9f, 0.45f,
            RayMasks.BARRICADE | RayMasks.STRUCTURE | RayMasks.PLAYER);
    }

    /// Somewhere on a site's starting floors a player fits: the `index`th clear tile,
    /// counted from the middle outward, so a team is spread over the base rather than
    /// stacked on one spot. Falls back to the anchor when every tile is built over.
    ///
    /// The anchor alone was wrong for a hexagonal base: its six triangles meet in the
    /// middle, so a wall on any of their inner edges runs straight through the anchor
    /// point, and a player put there was put inside it.
    internal static Vector3 FreeSpot(int siteIndex, RaidSite site, int index) {
        if (!anchorTiles.TryGetValue(siteIndex, out List<Vector3> tiles) || tiles.Count == 0) return StandingSpot(site);

        List<Vector3> clear = tiles
            .OrderBy(tile => (tile - site.AnchorPoint).sqrMagnitude)
            .Where(IsClear)
            .ToList();
        if (clear.Count == 0) return StandingSpot(site);

        return clear[Math.Max(0, index) % clear.Count] + Vector3.up * 1.5f;
    }

    /// A site's flag, wherever on the plot it stands, or null.
    internal static BarricadeDrop Marker(RaidSite site) {
        List<RegionCoordinate> around = [];
        Regions.getRegionsInRadius(site.AnchorPoint, site.BuildRadius, around);

        foreach (RegionCoordinate coordinate in around) {
            foreach (BarricadeDrop drop in BarricadeManager.regions[coordinate.x, coordinate.y].drops) {
                if (drop.asset == null || drop.asset.id != PeekSeekRaid.Config.AnchorMarkerItemId) continue;
                if (site.Contains(drop.GetServersideData().point)) return drop;
            }
        }

        return null;
    }

    /// Whether a site's flag is still standing. This is the round's win condition, so
    /// it is asked once a second while a round runs.
    internal static bool MarkerStands(RaidSite site) => Marker(site) != null;

    /// A layout offset turned to face the site and put on its anchor.
    private static Vector3 OnSite(RaidSite site, float x, float y, float z) {
        return site.AnchorPoint + Quaternion.Euler(0f, site.Yaw, 0f) * new Vector3(x, y, z);
    }

    /// Unturned's structure prefabs are authored facing forward and laid down by a
    /// quarter turn on X, so an upright or flat piece is Euler(-90, yaw, 0).
    private static Quaternion Facing(RaidSite site, float yaw) {
        return Quaternion.Euler(-90f, site.Yaw + yaw, 0f);
    }

    /// Half the anchor floor's thickness, so things resting on it start at its surface
    /// rather than at its middle.
    private const float AnchorFloorClearance = 0.25f;

    /// Somewhere to stand on a site: on top of the anchor floor, facing the way the
    /// site faces. Used for build-phase spawns, the leash, and defender spawns, all of
    /// which want the same answer.
    internal static Vector3 StandingSpot(RaidSite site) {
        return site.AnchorPoint + Vector3.up * 1.5f;
    }

    /// Where a player waits while a round is set up: one of the map's own lobby spawns,
    /// half a metre up as the game itself puts a player there. False on a map that has
    /// no lobby.
    ///
    /// Nothing here is Raid's: on an Arena map the regular player spawns are the lobby.
    /// They are where the game puts a player on join and on respawn
    /// (Provider.onLoginSpawning and PlayerLife, both LevelPlayers.getSpawn(false) in
    /// the U3-SDK), and LevelManager takes the alt spawns for the arena drop itself.
    /// Alpha Valley's are the building in the south-west corner, hundreds of metres
    /// from either site, which is the point: there is nothing to see from there.
    internal static bool LobbySpot(int index, out Vector3 point, out float angle) {
        List<PlayerSpawnpoint> lobby = LevelPlayers.getRegSpawns();
        if (lobby.Count == 0) {
            point = default;
            angle = 0f;
            return false;
        }

        PlayerSpawnpoint spawn = lobby[index % lobby.Count];
        point = spawn.point + Vector3.up * 0.5f;
        angle = spawn.angle;
        return true;
    }

    /// Drop a point onto whatever is under it.
    ///
    /// A spawn that was walked out by hand still gets this, because the spread round it
    /// is flat arithmetic on a map that is not flat, and teleporting somebody into the
    /// side of a hill leaves them stuck in it. The ray starts high and takes the first
    /// ground it finds; if it finds none, the point is used as given, because falling
    /// is recoverable and being inside a hill is not.
    internal static Vector3 OnGround(Vector3 point) {
        Vector3 sky = new(point.x, point.y + 200f, point.z);

        return Physics.Raycast(sky, Vector3.down, out RaycastHit hit, 400f, RayMasks.BLOCK_STANCE)
            ? hit.point + Vector3.up * 0.5f
            : point;
    }

    // ---- may I build here -----------------------------------------------------

    /// Which site a point is on, or -1.
    internal static int SiteAt(Vector3 point) {
        return PeekSeekRaid.Config.Sites.FindIndex(site => site.Contains(point));
    }

    /// Whether anything may be placed at a point right now.
    ///
    /// During the build phase each team may build on its own plot and nowhere else.
    /// During a round the only plot anybody may touch is the one being defended, which
    /// covers a defender putting down wire and an attacker putting a charge on a wall
    /// equally. Between phases, nothing.
    internal static bool MayBuildAt(Vector3 point, ulong owner) {
        if (raid == null) return true;

        int site = SiteAt(point);
        if (site < 0) return false;

        if (raid.Phase == RaidPhase.Building) return raid.SideOf(owner) == site;

        // Asked of the gamemode rather than worked out from the round number, so that
        // solo, which has one base and fights every round over it, opens the same plot
        // the round is actually being played on.
        return raid.Phase == RaidPhase.Playing && site == raid.DefendedSite;
    }

    private static void OnDeployStructure(
        Structure structure, ItemStructureAsset asset, ref Vector3 point,
        ref float angleX, ref float angleY, ref float angleZ,
        ref ulong owner, ref ulong group, ref bool shouldAllow) {
        if (!MayBuildAt(point, owner)) shouldAllow = false;
        else if (IsFloor(asset) && !TouchesAFloor(point)) shouldAllow = false;
        else if (raid.Phase == RaidPhase.Building && !raid.BudgetFor(owner).Counts.Has(asset.id)) shouldAllow = false;
    }

    /// Set while the plugin itself is putting a piece down, so the spend that follows a
    /// player's placement does not follow a swap or a restore.
    private static bool placingOurselves;

    /// A piece a builder put down is one off the pile: spent here, once it exists,
    /// rather than at the request, so a placement the game then refuses costs nothing.
    private static void OnStructureSpawned(StructureRegion region, StructureDrop drop) {
        if (placingOurselves || raid == null || raid.Phase != RaidPhase.Building) return;

        StructureData data = drop.GetServersideData();
        if (data.owner == 0 || SiteAt(data.point) != raid.SideOf(data.owner)) return;

        raid.Placed(data.owner, drop.asset.id);
    }

    internal static bool IsFloor(ItemStructureAsset asset) {
        return asset != null && (asset.construct == EConstruct.FLOOR || asset.construct == EConstruct.FLOOR_POLY);
    }

    /// Whether a floor at `point` would sit beside, or one storey above, a floor that is
    /// already standing. A base has to grow out of its anchor: a floor that touches
    /// nothing is a second base, or a bridge to one, and the whole design rests on there
    /// being exactly one.
    private static bool TouchesAFloor(Vector3 point) {
        List<RegionCoordinate> around = [];
        Regions.getRegionsInRadius(point, BaseLayout.Edge * 2f, around);

        foreach (RegionCoordinate coordinate in around) {
            foreach (StructureDrop drop in StructureManager.regions[coordinate.x, coordinate.y].drops) {
                if (drop.asset == null || !IsFloor(drop.asset)) continue;

                Vector3 gap = drop.GetServersideData().point - point;
                float flat = Mathf.Sqrt(gap.x * gap.x + gap.z * gap.z);
                if (flat <= BaseLayout.Edge + 0.5f && Mathf.Abs(gap.y) <= BaseLayout.HalfWallHeight * 2f + 0.5f) return true;
            }
        }

        return false;
    }

    /// The flag goes down by the one defender chosen to place it, during the moment set
    /// aside for that, on their own building. Everything else follows the phase rule,
    /// and during the build phase a door or hatch has to be on the pile like a wall.
    private static void OnDeployBarricade(
        Barricade barricade, ItemBarricadeAsset asset, Transform hit, ref Vector3 point,
        ref float angleX, ref float angleY, ref float angleZ,
        ref ulong owner, ref ulong group, ref bool shouldAllow) {
        if (asset.id == PeekSeekRaid.Config.AnchorMarkerItemId) {
            shouldAllow = raid != null
                && raid.Phase == RaidPhase.Picking
                && owner == raid.FlagBearer
                && SiteAt(point) == raid.DefendedSite
                && MayStandFlag(raid.DefendedSite, point);

            // The bearer is the one person this rule bites, so they are told why.
            if (!shouldAllow && raid != null && owner == raid.FlagBearer && PlayerTool.getSteamPlayer(new CSteamID(owner)) is { } bearer) {
                Host.Whisper(bearer, PeekSeekRaid.Instance.Translate("flag_where"));
            }
            return;
        }

        if (!MayBuildAt(point, owner)) shouldAllow = false;
        else if (raid.Phase == RaidPhase.Building && raid.BudgetFor(owner).Counts.Ids.Contains(asset.id)
                 && !raid.BudgetFor(owner).Counts.Has(asset.id)) shouldAllow = false;
    }

    /// A sentry goes down armed. The gun is put in the moment the sentry spawns and the
    /// storage is set to delete its contents when the sentry goes, so salvaging one and
    /// putting it back never leaves a Bluntforce on the floor; placing it again arms it
    /// again. A door a builder put down during the build phase is one off the pile.
    private static void OnBarricadeSpawned(BarricadeRegion region, BarricadeDrop drop) {
        if (raid == null || drop.asset == null) return;

        BarricadeData data = drop.GetServersideData();
        if (!placingOurselves && raid.Phase == RaidPhase.Building && data.owner != 0
            && SiteAt(data.point) == raid.SideOf(data.owner)) {
            raid.Placed(data.owner, drop.asset.id);
        }

        if (drop.asset is not ItemSentryAsset || drop.interactable is not InteractableSentry sentry) return;

        RaidItem gun = PeekSeekRaid.Config.SentryFirearm;
        if (gun == null || gun.ItemId == 0) return;

        Item armed = new(gun.ItemId, true);
        byte[] state = gun.StateBytes();
        if (state.Length > 0) armed.state = state;

        sentry.despawnWhenDestroyed = true;
        sentry.items.tryAddItem(armed);
    }

    /// Nobody opens a sentry: the gun in it is the kit, not loot.
    private static void OnOpenStorage(CSteamID instigator, InteractableStorage storage, ref bool shouldAllow) {
        if (raid != null && storage is InteractableSentry) shouldAllow = false;
    }

    // ---- may I take this down -------------------------------------------------

    /// A starting floor is never salvageable, by anybody, in any phase, and neither is
    /// the flag.
    ///
    /// The floors already go down owned by nobody, which is enough on its own: Unturned
    /// will not let a stranger salvage an unowned piece. This is the second lock,
    /// because the cost of being wrong is a team that can relocate its own site
    /// mid-match, and the cost of the check is one comparison.
    private static void OnSalvageStructure(StructureDrop drop, SteamPlayer instigator, ref bool shouldAllow) {
        int site = SiteAt(drop.GetServersideData().point);
        if (site >= 0 && IsAnchorTile(site, drop)) {
            shouldAllow = false;
            if (raid != null && raid.Phase == RaidPhase.Building) raid.OfferSwap(instigator, drop, site);
            return;
        }

        // During the build a salvage is a question, not an action: the piece stays
        // and a board asks what it should be instead, take it down being one answer.
        // Holding a key on a wall is the one gesture every player already knows.
        if (raid != null && raid.Phase == RaidPhase.Building && site == raid.SideOf(instigator.playerID.steamID.m_SteamID)) {
            shouldAllow = false;
            raid.OfferSwap(instigator, drop, site);
            return;
        }

        Salvaged(instigator, drop.asset, site);
    }

    /// How long a salvage takes for this player from now on. The client counts the
    /// hold itself and the game's default is eight seconds; the server can only set
    /// the number (PlayerInteract.sendSalvageTimeOverride), so it is set per phase.
    internal static void SalvageTime(Player player, float seconds) {
        player.interact.sendSalvageTimeOverride(seconds);
    }

    private static void OnSalvageBarricade(BarricadeDrop drop, SteamPlayer instigator, ref bool shouldAllow) {
        if (drop.asset?.id == PeekSeekRaid.Config.AnchorMarkerItemId) {
            shouldAllow = false;
            return;
        }

        Salvaged(instigator, drop.asset, SiteAt(drop.GetServersideData().point));
    }

    /// A piece a builder took down during the build phase goes back on the pile. The
    /// copy the game hands them is folded away by the budget's next sync.
    private static void Salvaged(SteamPlayer by, ItemAsset asset, int site) {
        if (raid == null || raid.Phase != RaidPhase.Building || asset == null || by == null) return;

        ulong steam64 = by.playerID.steamID.m_SteamID;
        if (site < 0 || site != raid.SideOf(steam64)) return;

        BuildBudget budget = raid.BudgetFor(steam64);
        if (budget.Counts.Ids.Contains(asset.id)) budget.Refund(asset.id);
    }

    /// A starting floor is never shootable, and the flag is shootable only while a
    /// round is being played.
    ///
    /// The flag is the objective: attackers win a round by bringing it down, and
    /// defenders win by keeping it up until the clock runs out. So it has to be
    /// destructible exactly when that race is on, and indestructible the rest of the
    /// time.
    private static bool MarkerIsDestructible => raid != null && raid.Phase == RaidPhase.Playing;

    private static void OnDamageStructure(
        CSteamID instigator, Transform structure, ref ushort damage,
        ref bool shouldAllow, EDamageOrigin origin) {
        StructureDrop drop = StructureManager.FindStructureByRootTransform(structure);
        if (drop == null) return;

        int site = SiteAt(drop.GetServersideData().point);
        if (site >= 0 && IsAnchorTile(site, drop)) shouldAllow = false;
    }

    private static void OnDamageBarricade(
        CSteamID instigator, Transform barricade, ref ushort damage,
        ref bool shouldAllow, EDamageOrigin origin) {
        ItemAsset asset = BarricadeManager.FindBarricadeByRootTransform(barricade)?.asset;
        if (asset?.id == PeekSeekRaid.Config.AnchorMarkerItemId && !MarkerIsDestructible) shouldAllow = false;
    }

    // ---- swapping a piece --------------------------------------------------------

    /// The piece a player is looking at, within reach, or null.
    internal static StructureDrop Aimed(Player player) {
        Transform aim = player.look.aim;
        if (!Physics.Raycast(aim.position, aim.forward, out RaycastHit hit, 12f, RayMasks.STRUCTURE)) return null;

        return StructureManager.FindStructureByRootTransform(hit.transform.root);
    }

    /// Put a piece of another material exactly where this one stands, keeping who it
    /// belongs to. The old one is destroyed rather than salvaged, so nothing is handed
    /// to anybody: the budget is the only ledger.
    internal static void Swap(StructureDrop drop, ItemStructureAsset to) {
        StructureData data = drop.GetServersideData();
        Vector3 point = data.point;
        Quaternion rotation = drop.model.rotation;
        ulong owner = data.owner, group = data.group;

        Remove(drop);
        placingOurselves = true;
        try {
            StructureManager.dropReplicatedStructure(new Structure(to, to.health), point, rotation, owner, group);
        } finally {
            placingOurselves = false;
        }
    }

    internal static void Remove(StructureDrop drop) {
        if (!StructureManager.tryGetRegion(drop.model, out byte x, out byte y, out StructureRegion _)) return;

        StructureManager.destroyStructure(drop, x, y, Vector3.zero);
    }

    /// The piece standing at exactly this point, or null.
    internal static StructureDrop StructureAt(Vector3 point) {
        List<RegionCoordinate> around = [];
        Regions.getRegionsInRadius(point, 1f, around);

        foreach (RegionCoordinate coordinate in around) {
            foreach (StructureDrop drop in StructureManager.regions[coordinate.x, coordinate.y].drops) {
                if ((drop.GetServersideData().point - point).sqrMagnitude < BaseLayout.LinkTolerance * BaseLayout.LinkTolerance) return drop;
            }
        }

        return null;
    }

    /// Every piece of the same kind as this one in another material: the swap board.
    /// A pillar has none: the frame is maple and stays maple.
    internal static List<ItemStructureAsset> Variants(ItemStructureAsset of) {
        if (of.construct == EConstruct.PILLAR || of.construct == EConstruct.POST) return [];

        List<ItemAsset> all = [];
        Assets.find(all);

        return all.OfType<ItemStructureAsset>()
            .Where(asset => asset.id != of.id && Materials.SameKind(asset.itemName, of.itemName))
            .OrderBy(asset => Array.IndexOf(Materials.Names, Materials.Split(asset.itemName)?.Material))
            .ToList();
    }

    // ---- the player -----------------------------------------------------------------

    /// Whether a player is standing on something that was built rather than on the map.
    ///
    /// A short ray straight down from just above the feet. Barricades and structures
    /// both count, so a defender on a metal floor, on a birch ramp, or on top of a
    /// crate they placed is all equally "on the base"; the rule players need to
    /// internalise is "get off the dirt", and anything finer than that would be a rule
    /// they cannot see.
    internal static bool IsStandingOnBuilt(Player player) {
        Vector3 origin = player.transform.position + Vector3.up * 0.4f;

        return Physics.Raycast(
            origin, Vector3.down, 1.2f, RayMasks.BARRICADE | RayMasks.STRUCTURE);
    }

    /// Put a point on a player's map and compass, or take it off. The game's own quest
    /// marker, so it is drawn the way every player already reads one, and it shows to
    /// their group too. An attacker carries the base they are taking for the whole
    /// round: with both bases on one map and the spawns a hundred metres out, "which
    /// way" is the first question, and this is the answer that is always on screen.
    internal static void Mark(Player player, Vector3? at, string label) {
        if (at == null) {
            if (player.quests.isMarkerPlaced) player.quests.replicateSetMarker(false, Vector3.zero);
            return;
        }

        player.quests.replicateSetMarker(true, at.Value, label ?? "");
    }

    /// Root a player to the spot, or let them go. Speed and jump both, because a
    /// standing jump is a way to move.
    internal static void Freeze(Player player, bool frozen) {
        player.movement.sendPluginSpeedMultiplier(frozen ? 0f : 1f);
        player.movement.sendPluginJumpMultiplier(frozen ? 0f : 1f);
    }

    /// Give a player hands big enough to carry one of everything.
    ///
    /// Unturned does replicate a page resize, contrary to what the shape of the API
    /// suggests: Items.resize fires PlayerInventory.onItemsResized, which sends SendSize
    /// to that player's own client, so the server can set a page to whatever it likes and
    /// the client will draw it. The hands page is the one worth growing; it is always
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

    /// Their own clothes and nothing in their hands: what everybody wears while they
    /// wait for a round, and what a builder starts from.
    internal static void DressPlain(Player player) {
        ClearInventory(player);

        ulong steam64 = player.channel.owner.playerID.steamID.m_SteamID;
        foreach (GamemodeItem item in raid.Match.KitFor(steam64)) {
            if (IsClothing(item.ItemId)) Give(player, item.ItemId, item.Amount, item.State);
        }
        if (player.clothing.backpack == 0) {
            GiveGear(player, PeekSeekRaid.Config.FallbackOutfit, clothingOnly: true);
        }

        ExpandHands(player, PeekSeekRaid.Config.HandsWidth, PeekSeekRaid.Config.HandsHeight);
        Heal(player);
    }

    /// Dress a builder: their own clothes, the planner in their hands, and no gun. A
    /// build phase is for building, and a rifle in the first slot is what got equipped
    /// instead of the planner. The pieces themselves come from the budget.
    internal static void DressForBuilding(Player player) {
        DressPlain(player);

        // Added with the useable auto-equip on and nothing else in hand, which is what
        // puts the planner in their hands rather than in the bag.
        foreach (RaidItem tool in PeekSeekRaid.Config.BuildTools) {
            for (byte i = 0; i < tool.Amount; i++) {
                player.inventory.forceAddItemAuto(new Item(tool.ItemId, true), false, true, false);
            }
        }
    }

    /// The flag, and nothing else, in the hands of the defender who places it.
    internal static void HandFlag(Player player) {
        DressPlain(player);
        player.inventory.forceAddItemAuto(new Item(PeekSeekRaid.Config.AnchorMarkerItemId, true), false, true, false);
    }

    /// Dress a player in their own loadout and nothing else.
    ///
    /// This is what a builder gets: the gun and outfit they configured on the site, with
    /// none of the gamemode's default items. A dressing and a painkiller are things you
    /// want going into a fight, not going into a build phase, and turning up holding
    /// them is confusing rather than generous.
    internal static void Outfit(Player player) {
        ClearInventory(player);

        ulong steam64 = player.channel.owner.playerID.steamID.m_SteamID;
        foreach (GamemodeItem item in raid.Match.KitFor(steam64).OrderBy(item => IsClothing(item.ItemId) ? 0 : 1)) {
            Give(player, item.ItemId, item.Amount, item.State);
        }

        // A kit with no outfit in it, or one that never arrived because the site was
        // unreachable, leaves somebody naked with almost no inventory space, which is
        // what "my gear is gone" looks like from the inside. Only then does the fallback
        // outfit appear, so a player who has a loadout keeps wearing their own.
        if (player.clothing.backpack == 0) {
            GiveGear(player, PeekSeekRaid.Config.FallbackOutfit, clothingOnly: true);
        }

        ExpandHands(player, PeekSeekRaid.Config.HandsWidth, PeekSeekRaid.Config.HandsHeight);

        Heal(player);
    }

    /// Passive regeneration is off on PeekSeek servers, so this is the only moment
    /// anybody gets health back. Bleeding and broken legs go with it: a wound from the
    /// last round is not something to carry into this one.
    private static void Heal(Player player) {
        player.life.serverModifyHealth(100);
        player.life.serverSetBleeding(false);
        player.life.serverSetLegsBroken(false);
    }

    /// Everything a player carries into a round: their saved kit, the gamemode's
    /// default items, and the one option they picked off the board.
    internal static void Kit(Player player, RaidOption option) {
        Outfit(player);

        Give(player, PeekSeekRaid.Config.DefaultItems);
        if (option != null) Give(player, option.Items);
    }

    internal static void Give(Player player, IEnumerable<RaidItem> items) {
        // Clothing first, always. Inventory space in Unturned comes from what you are
        // wearing, so handing somebody forty walls before their backpack puts most of it
        // on the floor. The kit the site returns lists a gun and an outfit in no
        // particular order, so the ordering is imposed here rather than assumed.
        foreach (RaidItem item in items.OrderBy(item => IsClothing(item.ItemId) ? 0 : 1)) {
            Give(player, item);
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

    /// Every item whose name contains `text`, for working out what something is called
    /// without digging through the bundles.
    internal static List<ItemAsset> Search(string text) {
        List<ItemAsset> all = [];
        Assets.find(all);

        return all.FindAll(item =>
            item.itemName != null
            && item.itemName.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0);
    }

    private static ItemAsset ByName(string name) {
        if (ByNameCache.TryGetValue(name, out ItemAsset cached)) return cached;

        List<ItemAsset> all = [];
        Assets.find(all);

        ItemAsset found = all.Find(
            item => string.Equals(item.itemName, name, StringComparison.OrdinalIgnoreCase));

        if (found == null) {
            Logger.LogWarning($"No item is called \"{name}\". Check BuildTools and FallbackOutfit in the config.");
        }

        ByNameCache[name] = found;
        return found;
    }

    private static bool IsClothing(ushort itemId) {
        return Assets.find(EAssetType.ITEM, itemId) is ItemClothingAsset;
    }

    private static void Give(Player player, RaidItem item) {
        Give(player, item.ItemId, item.Amount, item.StateBytes());
    }

    private static void Give(Player player, ushort itemId, byte amount, byte[] state) {
        for (byte i = 0; i < amount; i++) {
            Item created = new(itemId, true);
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
    /// the last round would carry last round's option into this one, which is exactly
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
