using HarmonyLib;
using JetBrains.Annotations;
using SDG.Unturned;

namespace PeekSeekArmorSkin;

internal static class MilitaryArmor {
    internal static float For(ELimb limb, PeekSeekArmorSkinConfiguration config) => limb switch {
        ELimb.LEFT_FOOT or ELimb.LEFT_LEG or ELimb.RIGHT_FOOT or ELimb.RIGHT_LEG => config.LegsArmor,
        ELimb.LEFT_HAND or ELimb.LEFT_ARM or ELimb.RIGHT_HAND or ELimb.RIGHT_ARM => config.ArmsArmor,
        ELimb.SPINE => config.SpineArmor,
        ELimb.SKULL => config.SkullArmor,
        _ => 1f,
    };
}

[HarmonyPatch(typeof(DamageTool), nameof(DamageTool.getPlayerArmor))]
[UsedImplicitly]
internal static class EqualizeArmorPatch {
    [UsedImplicitly]
    private static bool Prefix(ELimb limb, ref float __result) {
        if (!PeekSeekArmorSkin.Config.EqualizeArmor) return true;

        __result = MilitaryArmor.For(limb, PeekSeekArmorSkin.Config);
        return false;
    }
}

[HarmonyPatch(typeof(DamageTool), nameof(DamageTool.getPlayerExplosionArmor))]
[UsedImplicitly]
internal static class EqualizeExplosionArmorPatch {
    [UsedImplicitly]
    private static bool Prefix(ref float __result) {
        if (!PeekSeekArmorSkin.Config.EqualizeArmor) return true;

        __result = PeekSeekArmorSkin.Config.ExplosionArmor;
        return false;
    }
}
