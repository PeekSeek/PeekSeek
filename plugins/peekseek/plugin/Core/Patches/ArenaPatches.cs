using HarmonyLib;
using JetBrains.Annotations;
using SDG.Unturned;

namespace PeekSeek.Core.Patches;

/// Every PeekSeek map is an Arena map, and Unturned will happily run its own Arena
/// gamemode on top of whatever a plugin is doing: a lobby countdown, a shrinking play
/// zone, its own round loop and its own spawns. All of that would fight a gamemode for
/// control of the same players.
///
/// So the native loop is switched off here, unconditionally. A gamemode that wants
/// it, which is PeekSeek ARENA, patches the same methods from its own plugin with what
/// it wants them to do instead; Harmony runs every prefix and skips the original if
/// any of them says so, so the two patches on one method never fight.
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
