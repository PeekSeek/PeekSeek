using HarmonyLib;
using JetBrains.Annotations;
using SDG.Unturned;

namespace PeekSeekSiege.Core.Patches;

/// Anything on a site that wants electricity has it.
///
/// A sentry only scans and shoots while the server thinks it is wired, and the server
/// works that out from generators within range. There are none, and a generator
/// buried under each base would be one more thing to place, fuel and hide. The
/// question is answered here instead: on a plot, yes. The client works out its own
/// answer for the sentry's light, so that stays dark; the shooting is the server's.
[HarmonyPatch(typeof(InteractablePower), "CalculateIsConnectedToPower")]
[UsedImplicitly]
internal class PowerPatch {
    [UsedImplicitly]
    private static bool Prefix(InteractablePower __instance, ref bool __result) {
        if (SiegeWorld.SiteAt(__instance.transform.position) < 0) return true;

        __result = true;
        return false;
    }
}
