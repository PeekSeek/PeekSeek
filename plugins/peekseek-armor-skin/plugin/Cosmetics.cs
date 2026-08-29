using HarmonyLib;
using JetBrains.Annotations;
using SDG.Unturned;

namespace PeekSeekArmorSkin;

[HarmonyPatch(typeof(Provider), nameof(Provider.accept), typeof(SteamPending))]
[UsedImplicitly]
internal static class StripCosmeticsPatch {
    [UsedImplicitly]
    private static void Prefix(SteamPending player) {
        if (!PeekSeekArmorSkin.Config.StripCosmetics) return;

        player.shirtItem = 0;
        player.pantsItem = 0;
        player.vestItem = 0;
        player.backpackItem = 0;
    }
}
