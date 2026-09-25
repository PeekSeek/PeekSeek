using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using JetBrains.Annotations;
using PeekSeek.Gamemodes;
using SDG.NetTransport;
using SDG.Unturned;
using Steamworks;
using UnityEngine;
using Logger = Rocket.Core.Logging.Logger;
using Random = UnityEngine.Random;

namespace PeekSeekArena.Patches;

/// Unturned's arena loop, made PeekSeek's.
///
/// PeekSeek Match switches the native loop off for every gamemode by patching these
/// same four methods to do nothing. These prefixes run beside those, and do the work
/// only while the arena is the gamemode in play; Harmony runs every prefix on a method
/// and skips the original if any of them says so, so the two never fight.
///
/// The wait for the roster is Match's and happens before Begin. What is left for the
/// lobby state here is the moment between rounds: a side that has gone home has
/// forfeited, nobody at all releases the match, and otherwise the next round starts.
[HarmonyPatch(typeof(LevelManager), "arenaLobby")]
[UsedImplicitly]
internal class ArenaLobbyPatch {
    // First, or Match's own do-nothing prefix on the same method runs first and
    // Harmony skips every prefix after one that returns false: this never ran.
    [HarmonyPriority(Priority.First)]
    [UsedImplicitly]
    private static bool Prefix(LevelManager __instance) {
        if (PeekSeekArena.Arena is not { InPlay: true }) return false;

        __instance.findGroups();

        if (!Host.Players().Any()) {
            Host.Abandon();
            return false;
        }

        int alone = Host.TeamLeftAlone();
        if (alone >= 0) {
            Host.Forfeit(alone);
            return false;
        }

        LevelManager.arenaState = EArenaState.CLEAR;
        return false;
    }
}

[HarmonyPatch(typeof(LevelManager), "arenaPlay")]
[UsedImplicitly]
internal class ArenaPlayPatch {
    // First, or Match's own do-nothing prefix on the same method runs first and
    // Harmony skips every prefix after one that returns false: this never ran.
    [HarmonyPriority(Priority.First)]
    [UsedImplicitly]
    private static bool Prefix(LevelManager __instance) {
        if (PeekSeekArena.Arena is not { InPlay: true }) return false;

        if (LevelManager.arenaMessage != EArenaMessage.PLAY)
            LevelManager.SendArenaMessage.InvokeAndLoopback(ENetReliability.Reliable,
                Provider.GatherRemoteClientConnections(), EArenaMessage.PLAY);
        if (LevelManager.nonGroups + LevelManager.arenaGroups.Count < LevelManager.minPlayers) {
            LevelManager.arenaState = EArenaState.FINALE;
            LevelManager.lastFinaleMessage = Time.realtimeSinceStartup;
            if (LevelManager.arenaPlayers.Count > 0) {
                var playersIDs3 = new ulong[LevelManager.arenaPlayers.Count];
                for (var i = 0; i < LevelManager.arenaPlayers.Count; i++)
                    playersIDs3[i] = LevelManager.arenaPlayers[i].steamPlayer.playerID.steamID.m_SteamID;
                LevelManager.arenaMessage = EArenaMessage.LOSE;
                LevelManager.SendArenaPlayer.InvokeAndLoopback(ENetReliability.Reliable, Provider.GatherRemoteClientConnections(), LevelManager.SendArenaPlayer_Write, playersIDs3, EArenaMessage.WIN);

                PeekSeekArena.Arena.RoundWon(playersIDs3[0]);
            } else {
                LevelManager.SendArenaMessage.InvokeAndLoopback(ENetReliability.Reliable,
                    Provider.GatherRemoteClientConnections(), EArenaMessage.LOSE);
            }

            return false;
        }

        var time = Time.time;
        var deltaTime = Time.deltaTime;
        for (var num = LevelManager.arenaPlayers.Count - 1; num >= 0; num--) {
            var arenaPlayer = LevelManager.arenaPlayers[num];
            if (arenaPlayer.steamPlayer == null || arenaPlayer.steamPlayer.player == null) {
                var playersIDs2 = new ulong[1];
                if (arenaPlayer.steamPlayer != null) {
                    playersIDs2[0] = arenaPlayer.steamPlayer.playerID.steamID.m_SteamID;
                    LevelManager.SendArenaPlayer.InvokeAndLoopback(ENetReliability.Reliable, Provider.GatherRemoteClientConnections(), LevelManager.SendArenaPlayer_Write, playersIDs2, EArenaMessage.WIN);
                    LevelManager.arenaPlayers.RemoveAt(num);
                    __instance.updateGroups(arenaPlayer.steamPlayer);
                }
            } else {
                if (MathfEx.HorizontalDistanceSquared(arenaPlayer.steamPlayer.player.transform.position,
                        LevelManager.arenaCurrentCenter) > LevelManager.arenaSqrRadius ||
                    LevelManager.arenaCurrentRadius < 1f) {
                    if (time - arenaPlayer.lastAreaDamage > 1f) {
                        var num2 = Provider.modeConfigData.Events.Arena_Compactor_Extra_Damage_Per_Second *
                                   arenaPlayer.timeOutsideArea;
                        var amount =
                            MathfEx.RoundAndClampToByte(Provider.modeConfigData.Events.Arena_Compactor_Damage + num2);
                        arenaPlayer.steamPlayer.player.life.askDamage(amount, Vector3.up * 10f, EDeathCause.ARENA,
                            ELimb.SPINE, CSteamID.Nil, out _, false, ERagdollEffect.None, true, true);
                        arenaPlayer.lastAreaDamage = time;
                    }

                    arenaPlayer.timeOutsideArea += deltaTime;
                } else {
                    arenaPlayer.timeOutsideArea = 0f;
                }

                if (!arenaPlayer.hasDied) continue;
                var playersIDs = new ulong[1];
                playersIDs[0] = arenaPlayer.steamPlayer.playerID.steamID.m_SteamID;
                LevelManager.SendArenaPlayer.InvokeAndLoopback(ENetReliability.Reliable, Provider.GatherRemoteClientConnections(), LevelManager.SendArenaPlayer_Write, playersIDs, EArenaMessage.DIED);
                LevelManager.arenaPlayers.RemoveAt(num);
                __instance.updateGroups(arenaPlayer.steamPlayer);
            }

            LevelManager.SendLevelNumber.InvokeAndLoopback(ENetReliability.Reliable,
                Provider.GatherRemoteClientConnections(), (byte)LevelManager.arenaPlayers.Count);
        }

        return false;
    }
}

/// Everybody comes down at once, each team on its own spawn, the spawns rotating one
/// step per round so nobody keeps the good end of the map.
[HarmonyPatch(typeof(LevelManager))]
[HarmonyPatch(nameof(LevelManager.arenaSpawn))]
[UsedImplicitly]
internal class ArenaSpawnPatch {
    // First, or Match's own do-nothing prefix on the same method runs first and
    // Harmony skips every prefix after one that returns false: this never ran.
    [HarmonyPriority(Priority.First)]
    [UsedImplicitly]
    private static bool Prefix(LevelManager __instance) {
        if (PeekSeekArena.Arena is not { InPlay: true }) return false;

        List<SerializableVector3> spawns = [.. PeekSeekArena.Config.Spawns ?? []];
        if (spawns.Count == 0) {
            Logger.LogError("PeekSeekArena has no Spawns configured, so nobody can be put anywhere. Write them into PeekSeekArena.configuration.xml.");
            return false;
        }
        Host.BeginRound();
        int roundNumber = Host.RoundsPlayed + 1;
        Logger.Log("[alive-check] Round starting, everyone marked alive again.");

        List<SteamPlayer> list = [.. Provider.clients];
        list.RemoveAll(client => Host.IsSpectator(client.playerID.steamID.m_SteamID));

        while (spawns.Count > 0 && list.Count != 0) {
            var index = Random.Range(0, list.Count);
            if (list[index] == null) {
                list.RemoveAtFast(index);
                continue;
            }

            var steamPlayer = list[index];
            list.RemoveAtFast(index);
            if (steamPlayer == null || steamPlayer.player == null || steamPlayer.player.life.isDead) continue;

            // The spawn comes from the roster, not the player's group. Placing by group
            // skipped anyone who had left theirs: never moved, never in the round, and
            // still counted as a side, so the round could not end and the match ran on
            // for an hour and a half. They are put back in their group as well, or they
            // would count as a side of their own.
            int team = Host.Match.TeamIndexOf(steamPlayer.playerID.steamID.m_SteamID);
            if (team < 0) {
                Logger.LogWarning($"Player {steamPlayer.playerID.steamID} is on this server but not on the match roster.");
                continue;
            }
            Host.Regroup(steamPlayer.player);
            SerializableVector3 spawn = spawns[(roundNumber - 1 + team) % spawns.Count];

            steamPlayer.player.life.sendRevive();
            steamPlayer.player.teleportToLocationUnsafe(spawn.ToVector3(), steamPlayer.player.look.yaw);
            LevelManager.arenaPlayers.Add(new ArenaPlayer(steamPlayer));
        }

        // The sides still in are counted off who is actually in the round. The lobby's
        // count took everyone standing on the server, and a player it counted who never
        // made it into the round is never taken off again, because only a death in the
        // round takes anybody off.
        LevelManager.nonGroups = 0;
        LevelManager.arenaGroups.Clear();
        foreach (ArenaPlayer inRound in LevelManager.arenaPlayers) {
            PlayerQuests quests = inRound.steamPlayer.player.quests;
            if (!quests.isMemberOfAGroup) LevelManager.nonGroups++;
            else if (!LevelManager.arenaGroups.Contains(quests.groupID)) LevelManager.arenaGroups.Add(quests.groupID);
        }
        StandIns.CountTheirSides();

        LevelManager.arenaState = EArenaState.PLAY;
        LevelManager.SendLevelNumber.InvokeAndLoopback(ENetReliability.Reliable,
            Provider.GatherRemoteClientConnections(), (byte)LevelManager.arenaPlayers.Count);
        return false;
    }
}

/// The game's own count of who is still in, minus spectators, who are on the server
/// but not in the round.
[HarmonyPatch(typeof(LevelManager))]
[HarmonyPatch(nameof(LevelManager.findGroups))]
[UsedImplicitly]
public class FindGroupsPatch {
    // First, or Match's own do-nothing prefix on the same method runs first and
    // Harmony skips every prefix after one that returns false: this never ran.
    [HarmonyPriority(Priority.First)]
    [UsedImplicitly]
    private static bool Prefix() {
        if (PeekSeekArena.Arena is not { InPlay: true }) return false;

        LevelManager.nonGroups = 0;
        LevelManager.arenaGroups.Clear();
        foreach (var client in Provider.clients) {
            if (client == null || client.player == null ||
                client.player.life.isDead || Host.IsSpectator(client.playerID.steamID.m_SteamID)) continue;
            if (!client.player.quests.isMemberOfAGroup)
                ++LevelManager.nonGroups;
            else if (!LevelManager.arenaGroups.Contains(client.player.quests.groupID))
                LevelManager.arenaGroups.Add(client.player.quests.groupID);
        }
        StandIns.CountTheirSides();
        return false;
    }
}

/// The players `/ssolo` stands in for are never on the server, so the game never counts
/// their side. Alone, a tester was one side short of the two a round needs: the warmup
/// counted, fell back to the lobby, and counted again, forever. A team with a stand-in on
/// it is counted as a side still in, for as long as the stand-in is.
internal static class StandIns {
    internal static void CountTheirSides() {
        foreach (GamemodeTeam team in Host.Match?.Teams ?? []) {
            if (team.GroupId == 0 || !team.Players.Any(Host.IsPhantom)) continue;

            CSteamID group = new(team.GroupId);
            if (!LevelManager.arenaGroups.Contains(group)) LevelManager.arenaGroups.Add(group);
        }
    }
}
