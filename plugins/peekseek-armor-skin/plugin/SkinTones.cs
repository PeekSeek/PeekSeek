using System.Collections.Generic;
using HarmonyLib;
using JetBrains.Annotations;
using SDG.Unturned;
using UnityEngine;

namespace PeekSeekArmorSkin;

internal static class SkinTones {
    private const float PresetTolerance = 0.01f;

    internal static int PresetFor(ulong steamId, IReadOnlyList<int> presets) =>
        presets[(int)(steamId % (ulong)presets.Count)];

    internal static Color For(ulong steamId, IReadOnlyList<int> presets) =>
        Customization.SKINS[PresetFor(steamId, presets)];

    internal static bool IsInRange(Color skin, IReadOnlyList<int> presets) {
        int lightest = int.MaxValue;
        int darkest = int.MinValue;
        foreach (int preset in presets) {
            if (preset < lightest) lightest = preset;
            if (preset > darkest) darkest = preset;
        }

        for (int preset = lightest; preset <= darkest; preset++) {
            if (MatchesPreset(Customization.SKINS[preset], skin)) return true;
        }
        return false;
    }

    private static bool MatchesPreset(Color preset, Color skin) =>
        Mathf.Abs(preset.r - skin.r) < PresetTolerance
        && Mathf.Abs(preset.g - skin.g) < PresetTolerance
        && Mathf.Abs(preset.b - skin.b) < PresetTolerance;
}

[HarmonyPatch(typeof(Provider), nameof(Provider.accept), typeof(SteamPending))]
[UsedImplicitly]
internal static class AssignSkinTonePatch {
    [UsedImplicitly]
    private static void Prefix(SteamPending player) {
        if (!PeekSeekArmorSkin.Config.AssignSkinTones) return;

        List<int> presets = PeekSeekArmorSkin.Config.SkinTonePresets;
        if (SkinTones.IsInRange(player.skin, presets)) return;

        player._skin = SkinTones.For(player.playerID.steamID.m_SteamID, presets);
    }
}
