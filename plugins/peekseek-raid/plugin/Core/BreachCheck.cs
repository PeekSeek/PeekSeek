using System;
using System.Collections.Generic;
using System.Linq;
using PeekSeekRaid.Core.Models;
using SDG.Unturned;
using UnityEngine;

namespace PeekSeekRaid.Core;

/// The guarantee that attackers can always get in, measured on the world: how many
/// demolition charges stand between a spot and the outside, along the cheapest way out.
///
/// Rays go out from just above the spot, BreachRays of them round the horizon and one
/// straight up, as far as BreachReach, and every structure and door or pane each one
/// crosses is priced from the charge's row of the hits table. Floors and pillars are
/// left out, as the table leaves them out: a charge goes on a wall, not on the post
/// beside it. The pure arithmetic is Breach's; this is only the walk.
internal static class BreachCheck {
    /// The charge's row: what one piece costs.
    private static BreakRule Charge => BreakTable.For(PeekSeekRaid.Config.BreakRules, PeekSeekRaid.Config.ChargeItemId);

    /// Every ray from a spot, nearest piece first, and the drop each piece is, so a
    /// ray can be acted on as well as priced.
    internal static List<List<(BreachPiece Piece, StructureDrop Structure)>> Rays(Vector3 spot) {
        Vector3 from = spot + Vector3.up * 1.2f;
        List<Vector3> directions = [Vector3.up];
        int count = Math.Max(1, PeekSeekRaid.Config.BreachRays);
        for (int i = 0; i < count; i++) directions.Add(Quaternion.Euler(0f, i * 360f / count, 0f) * Vector3.forward);

        return directions.Select(direction => Walk(from, direction)).ToList();
    }

    private static List<(BreachPiece, StructureDrop)> Walk(Vector3 from, Vector3 direction) {
        RaycastHit[] hits = Physics.RaycastAll(from, direction, PeekSeekRaid.Config.BreachReach, RayMasks.STRUCTURE | RayMasks.BARRICADE,
                                               QueryTriggerInteraction.Ignore);
        HashSet<Transform> seen = [];
        List<(BreachPiece, StructureDrop)> ray = [];
        BreakRule charge = Charge;
        if (charge == null) return ray;

        foreach (RaycastHit hit in hits.OrderBy(hit => hit.distance)) {
            Transform root = hit.transform.root;
            if (!seen.Add(root)) continue;

            if (StructureManager.FindStructureByRootTransform(root) is { } structure) {
                TargetKind kind = Damage.KindOf(structure.asset, structure.GetServersideData().group);
                if (kind == TargetKind.Untabled) continue;

                float? asMaple = MapleOf(structure.asset) is { } maple ? charge.For(Damage.KindOf(maple, 0)) : null;
                ray.Add((new BreachPiece(charge.For(kind), asMaple), structure));
                continue;
            }

            if (BarricadeManager.FindBarricadeByRootTransform(root) is { } barricade) {
                TargetKind kind = Damage.KindOf(barricade.asset);
                if (kind is TargetKind.Door or TargetKind.MetalWall) ray.Add((new BreachPiece(charge.For(kind)), null));
            }
        }

        return ray;
    }

    /// The breach cost at a spot: the cheapest ray.
    internal static float Cost(Vector3 spot) {
        return Breach.Cheapest(Rays(spot).Select(ray => ray.Select(step => step.Piece).ToList()).ToList()).Cost;
    }

    /// The dearest of a site's starting tiles, which is what the flag could cost
    /// wherever it goes, and what the build phase shows.
    internal static float WorstTile(IEnumerable<Vector3> tiles) {
        return tiles.Select(Cost).DefaultIfEmpty(0f).Max();
    }

    /// Bring every starting tile to the limit: on each one over it, the outermost metal
    /// on its cheapest ray becomes the same piece in maple until the ray is cheap
    /// enough. Returns the names of what changed, for telling the team.
    internal static List<string> Enforce(IEnumerable<Vector3> tiles) {
        List<string> changed = [];
        float limit = PeekSeekRaid.Config.BreachLimit;

        foreach (Vector3 tile in tiles) {
            List<List<(BreachPiece Piece, StructureDrop Structure)>> rays = Rays(tile);
            (int best, float cost) = Breach.Cheapest(rays.Select(ray => ray.Select(step => step.Piece).ToList()).ToList());
            if (best < 0 || cost <= limit) continue;

            List<(BreachPiece Piece, StructureDrop Structure)> ray = rays[best];
            foreach (int i in Breach.Downgrades(ray.Select(step => step.Piece).ToList(), limit)) {
                StructureDrop drop = ray[i].Structure;
                ItemStructureAsset maple = drop == null ? null : MapleOf(drop.asset);
                if (maple == null) continue;

                changed.Add(drop.asset.itemName);
                RaidWorld.Swap(drop, maple);
            }
        }

        return changed;
    }

    private static bool IsMetal(ItemStructureAsset asset) => Materials.Split(asset.itemName)?.Material == "Metal";

    /// The same piece in maple, for a metal one; null for anything else.
    internal static ItemStructureAsset MapleOf(ItemStructureAsset asset) {
        if (!IsMetal(asset)) return null;

        string kind = Materials.Split(asset.itemName)?.Kind;
        List<ItemAsset> all = [];
        Assets.find(all);
        return all.OfType<ItemStructureAsset>().FirstOrDefault(other =>
            Materials.Split(other.itemName) is { Material: "Maple" } split && split.Kind == kind);
    }
}
