using HarmonyLib;
using JetBrains.Annotations;
using SDG.Unturned;

namespace PeekSeekLobby.Core.Patches;

/// The lobby runs on an Arena-type map (same as a real PeekSeek match) purely for
/// its geometry and spawn layout, but must never let Unturned's own Arena
/// gamemode take over, no lobby countdown, no shrinking play zone, no native
/// round loop. Unlike the main PeekSeek plugin's equivalent patches (which only
/// skip vanilla logic while Status == IN_PROGRESS, because a real match still
/// needs it in between), these are unconditional no-ops: the lobby never wants
/// the native round system to run at all.
[HarmonyPatch(typeof(LevelManager), "arenaLobby")]
[UsedImplicitly]
internal class ArenaLobbyPatch {
    [UsedImplicitly]
    private static bool Prefix() => false;
}

[HarmonyPatch(typeof(LevelManager), "arenaPlay")]
[UsedImplicitly]
internal class ArenaPlayPatch {
    [UsedImplicitly]
    private static bool Prefix() => false;
}

[HarmonyPatch(typeof(LevelManager))]
[HarmonyPatch(nameof(LevelManager.arenaSpawn))]
[UsedImplicitly]
internal class ArenaSpawnPatch {
    [UsedImplicitly]
    private static bool Prefix() => false;
}

[HarmonyPatch(typeof(LevelManager))]
[HarmonyPatch(nameof(LevelManager.findGroups))]
[UsedImplicitly]
internal class FindGroupsPatch {
    [UsedImplicitly]
    private static bool Prefix() => false;
}
