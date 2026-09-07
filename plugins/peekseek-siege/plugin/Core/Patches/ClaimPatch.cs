using HarmonyLib;
using JetBrains.Annotations;
using SDG.Unturned;
using Steamworks;
using UnityEngine;

namespace PeekSeekSiege.Core.Patches;

/// Let Siege, not the claim flag, decide who may build on a site.
///
/// Each site is marked with a claim flag standing on the anchor floor, and both go down
/// owned by nobody so neither can ever be salvaged or moved. That is the behaviour the
/// gamemode wants, but it has an obvious consequence: Unturned's own claim check asks
/// whether the builder matches the flag's owner or group, and against an unowned flag
/// nobody matches, so vanilla would refuse everybody including the team the site
/// belongs to.
///
/// Rather than compromise on the ownership, the check is answered here. SiegeWorld
/// already holds the real rule — which plot is open, to whom, in which phase — and it
/// is a better rule than a radius around a flag: it knows that a build phase is private
/// to each team and that a round opens exactly one building to both of them. Points
/// outside both sites fall through to vanilla untouched, so nothing about the rest of
/// the map changes.
[HarmonyPatch(typeof(ClaimManager))]
[HarmonyPatch(nameof(ClaimManager.checkCanBuild))]
[UsedImplicitly]
internal class ClaimPatch {
    [UsedImplicitly]
    private static bool Prefix(Vector3 point, CSteamID owner, CSteamID group, bool isClaim, ref bool __result) {
        if (!Core.SiegeWorld.IsOnASite(point)) return true;

        __result = Core.SiegeWorld.MayBuildAt(point);
        return false;
    }
}
