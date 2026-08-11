using System;
using System.Collections.Generic;
using HarmonyLib;
using JetBrains.Annotations;
using PeekSeek.Core.Models;
using PeekSeek.Core.Models.Enums;
using SDG.NetTransport;
using SDG.Unturned;
using Steamworks;
using UnityEngine;
using Logger = Rocket.Core.Logging.Logger;
using Random = UnityEngine.Random;

namespace PeekSeek.Core.Patches;

[HarmonyPatch(typeof(LevelManager), "arenaLobby")]
[UsedImplicitly]
internal class LevelManagerPatch {
    private static float LastCountdownBroadcast;

    [UsedImplicitly]
    private static bool Prefix(LevelManager __instance) {
        if (PSManager.Status != EPSStatus.IN_PROGRESS) return false;

        __instance.findGroups();

        float secondsLeft = PeekSeek.Config.LobbyTimeoutSeconds - (Time.realtimeSinceStartup - PSManager.MatchReady);

        if (secondsLeft <= 0) {
            ResolveTimedOutLobby();
            return false;
        }

        if (PSManager.HaveAllPlayersJoined()) {
            LevelManager.arenaState = EArenaState.CLEAR;
            return false;
        }

        if (LevelManager.arenaMessage != 0)
            LevelManager.SendArenaMessage.InvokeAndLoopback(ENetReliability.Reliable,
                Provider.GatherRemoteClientConnections(), EArenaMessage.LOBBY);

        BroadcastCountdown(secondsLeft);
        return false;
    }

    /// Nobody left to play for gets the match released; one team present wins it by default;
    /// otherwise the match starts short-handed.
    private static void ResolveTimedOutLobby() {
        if (!PSManager.HasAnyPlayerJoined()) {
            PSManager.AbandonMatch();
            return;
        }

        PSTeam onlyTeam = PSManager.OnlyTeamOnline();
        if (onlyTeam != null) {
            PSManager.NotifyTeamForcedWin(onlyTeam);
            return;
        }

        LevelManager.arenaState = EArenaState.CLEAR;
    }

    private static void BroadcastCountdown(float secondsLeft) {
        if (Time.realtimeSinceStartup - LastCountdownBroadcast < 1f) return;
        LastCountdownBroadcast = Time.realtimeSinceStartup;

        // Null when the config file predates this field: XmlSerializer leaves it unset rather than defaulted.
        List<ushort> broadcastAt = PeekSeek.Config.LobbyTimeoutBroadcastSecondsRemaining;

        ushort rounded = (ushort)Mathf.Round(secondsLeft);
        if (broadcastAt == null || !broadcastAt.Contains(rounded)) return;

        PSManager.Announce(PeekSeek.Instance.Translate("lobby_timeout_warning", rounded));
    }
}

[HarmonyPatch(typeof(LevelManager), "arenaPlay")]
[UsedImplicitly]
internal class LevelManagerArenaPlayPatch {
    [UsedImplicitly]
    private static bool Prefix(LevelManager __instance) {
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

                PSManager.NotifyTeamWon(playersIDs3[0]);
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

[HarmonyPatch(typeof(LevelManager))]
[HarmonyPatch(nameof(LevelManager.arenaSpawn))]
[UsedImplicitly]
internal class LevelManagerArenaSpawnPatch {
    [UsedImplicitly]
    private static bool Prefix(LevelManager __instance) {
        List<HomelessSpawn> altSpawns = [.. PeekSeek.Config.Spawns];
        Dictionary<CSteamID, HomelessSpawn> assignedGroupSpawns = [];

        StatService.BeginRound();

        var roundNumber = StatService.GetTotalPlayedRounds() + 1;
        var teamIndex = 0;

        var totalSpawns = altSpawns.Count;

        foreach (var team in PSManager.Data.Teams) {
            var spawnIndex = (roundNumber - 1 + teamIndex) % totalSpawns;
            assignedGroupSpawns[PSManager.GetTeamGroupID(team)] = altSpawns[spawnIndex];
            teamIndex++;
        }

        List<SteamPlayer> list = [.. Provider.clients];
        list.RemoveAll(
            PSManager.IsSpectator
        );

        while (altSpawns.Count > 0 && list.Count != 0) {
            var index = Random.Range(0, list.Count);
            if (list[index] == null) {
                list.RemoveAtFast(index);
                continue;
            }

            var steamPlayer = list[index];
            list.RemoveAtFast(index);
            if (steamPlayer == null || steamPlayer.player == null || steamPlayer.player.life.isDead) continue;

            if (!assignedGroupSpawns.TryGetValue(steamPlayer.player.quests.groupID, out var spawn)) {
                Logger.LogWarning($"Player {steamPlayer.playerID.steamID} is not assigned to a group!");
                continue;
            }

            steamPlayer.player.life.sendRevive();
            steamPlayer.player.teleportToLocationUnsafe(spawn.Position.ToVector3(), steamPlayer.player.look.yaw);
            LevelManager.arenaPlayers.Add(new ArenaPlayer(steamPlayer));
        }

        LevelManager.arenaState = EArenaState.PLAY;
        LevelManager.SendLevelNumber.InvokeAndLoopback(ENetReliability.Reliable,
            Provider.GatherRemoteClientConnections(), (byte)LevelManager.arenaPlayers.Count);
        return false;
    }
}

[HarmonyPatch(typeof(LevelManager))]
[HarmonyPatch(nameof(LevelManager.findGroups))]
[UsedImplicitly]
public class LevelManagerFindGroupsPatch {
    [UsedImplicitly]
    private static bool Prefix() {
        LevelManager.nonGroups = 0;
        LevelManager.arenaGroups.Clear();
        foreach (var client in Provider.clients) {
            if (client == null || client.player == null ||
                client.player.life.isDead || PSManager.IsSpectator(client)) continue;
            if (!client.player.quests.isMemberOfAGroup)
                ++LevelManager.nonGroups;
            else if (!LevelManager.arenaGroups.Contains(client.player.quests.groupID))
                LevelManager.arenaGroups.Add(client.player.quests.groupID);
        }
        return false;
    }
}
