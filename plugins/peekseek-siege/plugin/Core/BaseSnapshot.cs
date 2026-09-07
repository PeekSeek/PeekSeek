using System;
using System.Collections.Generic;
using SDG.Unturned;
using UnityEngine;

namespace PeekSeekSiege.Core;

/// A team's finished base, frozen at the end of the build phase and re-laid before
/// every round.
///
/// Rounds are meant to be fought against the same building six or more times, so the
/// base cannot simply be left standing: a wall blown open in round one has to be back
/// in round two. Snapshotting is also what lets ownership be reassigned per round,
/// which is the whole trick behind soft walls — the pieces come back owned by nobody
/// except the ones this gamemode wants a team to be able to take down.
///
/// Everything is stored relative to the site anchor rather than in world space, so a
/// snapshot stays valid if a site is ever moved in config.
internal class BaseSnapshot {
    private readonly List<Piece> pieces = [];

    private class Piece {
        internal ushort ItemId;
        internal bool IsStructure;
        internal Vector3 Offset;
        internal Quaternion Rotation;
        internal byte[] State;
    }

    internal int Count => pieces.Count;

    /// Record every structure and barricade standing inside the site.
    ///
    /// The anchor floor and its claim flag are skipped: they are placed fresh by the
    /// site itself and must never be duplicated by a restore.
    internal static BaseSnapshot Capture(Vector3 anchor, float radius, HashSet<ushort> skipItemIds) {
        BaseSnapshot snapshot = new();
        float sqrRadius = radius * radius;

        foreach (RegionCoordinate coordinate in RegionsAround(anchor)) {
            StructureRegion structures = StructureManager.regions[coordinate.x, coordinate.y];
            foreach (StructureDrop drop in structures.drops) {
                StructureData data = drop.GetServersideData();
                if (drop.asset == null || skipItemIds.Contains(drop.asset.id)) continue;
                if ((data.point - anchor).sqrMagnitude > sqrRadius) continue;

                snapshot.pieces.Add(new Piece {
                    ItemId = drop.asset.id,
                    IsStructure = true,
                    Offset = data.point - anchor,
                    Rotation = data.rotation,
                    State = []
                });
            }

            BarricadeRegion barricades = BarricadeManager.regions[coordinate.x, coordinate.y];
            foreach (BarricadeDrop drop in barricades.drops) {
                BarricadeData data = drop.GetServersideData();
                if (drop.asset == null || skipItemIds.Contains(drop.asset.id)) continue;
                if ((data.point - anchor).sqrMagnitude > sqrRadius) continue;

                snapshot.pieces.Add(new Piece {
                    ItemId = drop.asset.id,
                    IsStructure = false,
                    Offset = data.point - anchor,
                    Rotation = data.rotation,
                    State = data.barricade.state
                });
            }
        }

        return snapshot;
    }

    /// Put the base back, handing each piece to `groupFor` to decide who owns it.
    ///
    /// Ownership is not cosmetic here: in Unturned it is what decides who may salvage
    /// a piece and who may open a door, so this callback is where the soft-wall and
    /// door rules actually take effect. Returning 0 leaves a piece owned by nobody,
    /// which is how a wall is made permanent.
    internal void Restore(Vector3 anchor, Func<ItemAsset, ulong> groupFor) {
        foreach (Piece piece in pieces) {
            ItemAsset asset = Assets.find(EAssetType.ITEM, piece.ItemId) as ItemAsset;
            if (asset == null) continue;

            ulong group = groupFor(asset);
            Vector3 point = anchor + piece.Offset;

            if (piece.IsStructure && asset is ItemStructureAsset structureAsset) {
                StructureManager.dropReplicatedStructure(
                    new Structure(structureAsset, structureAsset.health),
                    point,
                    piece.Rotation,
                    0,
                    group
                );
                continue;
            }

            if (asset is ItemBarricadeAsset barricadeAsset) {
                BarricadeManager.dropNonPlantedBarricade(
                    new Barricade(barricadeAsset, barricadeAsset.health, piece.State),
                    point,
                    piece.Rotation,
                    0,
                    group
                );
            }
        }
    }

    /// Flatten the site: everything a round left behind, so the next restore starts
    /// from bare ground rather than stacking a second base on top of the first.
    internal static void Clear(Vector3 anchor, float radius, HashSet<ushort> skipItemIds) {
        float sqrRadius = radius * radius;

        foreach (RegionCoordinate coordinate in RegionsAround(anchor)) {
            StructureRegion structures = StructureManager.regions[coordinate.x, coordinate.y];
            for (int i = structures.drops.Count - 1; i >= 0; i--) {
                StructureData data = structures.drops[i].GetServersideData();
                ItemStructureAsset asset = structures.drops[i].asset;
                if (asset == null || skipItemIds.Contains(asset.id)) continue;
                if ((data.point - anchor).sqrMagnitude > sqrRadius) continue;

                StructureManager.destroyStructure(structures.drops[i], coordinate.x, coordinate.y, Vector3.zero);
            }

            BarricadeRegion barricades = BarricadeManager.regions[coordinate.x, coordinate.y];
            for (int i = barricades.drops.Count - 1; i >= 0; i--) {
                BarricadeData data = barricades.drops[i].GetServersideData();
                ItemBarricadeAsset asset = barricades.drops[i].asset;
                if (asset == null || skipItemIds.Contains(asset.id)) continue;
                if ((data.point - anchor).sqrMagnitude > sqrRadius) continue;

                BarricadeManager.destroyBarricade(barricades.drops[i], coordinate.x, coordinate.y, ushort.MaxValue);
            }
        }
    }

    /// The regions a site touches. A site is far smaller than one region, but it can
    /// still straddle a boundary, so the search always covers the neighbours too.
    private static List<RegionCoordinate> RegionsAround(Vector3 anchor) {
        List<RegionCoordinate> found = [];
        Regions.getRegionsInRadius(anchor, Regions.REGION_SIZE, found);
        return found;
    }
}
