using HarmonyLib;
using JetBrains.Annotations;
using SDG.Unturned;

namespace PeekSeekSiege.Core.Patches;

/// Siege runs on Alpha Valley, which is an Arena-type map, and Unturned will happily
/// run its own Arena gamemode on top of whatever a plugin is doing: a lobby countdown,
/// a shrinking play zone, its own round loop and its own spawns. All of that would
/// fight this gamemode for control of the same players.
///
/// These are unconditional no-ops rather than the conditional ones the main PeekSeek
/// plugin uses. A Siege server is never doing anything else, so there is no state in
/// which the native round system is wanted.
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
