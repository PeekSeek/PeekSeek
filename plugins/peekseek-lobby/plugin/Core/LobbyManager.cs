using System.Collections.Generic;
using PeekSeekLobby.Core.Models;
using PeekSeekLobby.Core.Web;
using Rocket.Core.Utils;
using Rocket.Unturned.Chat;
using Rocket.Unturned.Events;
using Rocket.Unturned.Player;
using SDG.Unturned;
using Steamworks;
using UnityEngine;
using Logger = Rocket.Core.Logging.Logger;
using Random = UnityEngine.Random;

namespace PeekSeekLobby.Core;

internal abstract class LobbyManager
{
    private const string EquipKitCategory = "meta";

    internal class PlayerState
    {
        internal bool Queueing = true;
        internal byte Kills;
        internal byte Deaths;
        internal byte Headshots;
        internal bool IsDead;
        internal float SpawnProtectedUntil;
        internal float DeadUntil;
        internal readonly HashSet<int> AnnouncedCountdown = [];
    }

    private static readonly Dictionary<ulong, PlayerState> Players = [];
    private static float LastHeartbeatSent;

    internal static void Init()
    {
        Provider.onServerConnected += OnPlayerConnected;
        Provider.onServerDisconnected += OnPlayerDisconnected;
        UnturnedPlayerEvents.OnPlayerDeath += OnPlayerDead;

        RemoteAPI.Init();
    }

    internal static void Shutdown()
    {
        Provider.onServerConnected -= OnPlayerConnected;
        Provider.onServerDisconnected -= OnPlayerDisconnected;
        UnturnedPlayerEvents.OnPlayerDeath -= OnPlayerDead;

        RemoteAPI.Shutdown();
    }

    private static void OnPlayerConnected(CSteamID steamID)
    {
        Player player = PlayerTool.getPlayer(steamID);
        if (player == null) return;

        PlayerState state = new();
        Players[steamID.m_SteamID] = state;

        SpawnPlayer(steamID, state);
        RemoteAPI.SendHeartbeat(steamID.m_SteamID, PeekSeekLobby.Config.GamemodeBitmask, PeekSeekLobby.Config.szRegion);

        ChatManager.serverSendMessage(
            PeekSeekLobby.Instance.Translate("welcome"),
            UnturnedChat.GetColorFromHex(PeekSeekLobby.Config.szHexDefaultChatColor).Value,
            null, player.channel.owner, EChatMode.SAY, PeekSeekLobby.Config.szChatIconURL, true
        );
    }

    private static void OnPlayerDisconnected(CSteamID steamID)
    {
        if (Players.Remove(steamID.m_SteamID))
        {
            RemoteAPI.SendQuit(steamID.m_SteamID);
        }
    }

    /// Ordinary lobby kills are public — there's no stream overlay here to carry a
    /// kill feed the way a real match's does, so chat is the only kill feed there is.
    private static void OnPlayerDead(UnturnedPlayer victim, EDeathCause cause, ELimb limb, CSteamID murderer)
    {
        if (cause == EDeathCause.ARENA) return;
        if (!Players.TryGetValue(victim.CSteamID.m_SteamID, out PlayerState victimState)) return;

        victimState.Deaths++;
        victimState.IsDead = true;
        victimState.DeadUntil = Time.realtimeSinceStartup + PeekSeekLobby.Config.RespawnDelaySeconds;
        victimState.AnnouncedCountdown.Clear();

        bool isPlayerKill = murderer != CSteamID.Nil
            && murderer != SteamGameServer.GetSteamID()
            && murderer != victim.CSteamID;
        if (!isPlayerKill) return;

        UnturnedPlayer killer = UnturnedPlayer.FromCSteamID(murderer);
        if (killer == null) return;

        if (Players.TryGetValue(murderer.m_SteamID, out PlayerState killerState))
        {
            killerState.Kills++;
            if (limb == ELimb.SKULL) killerState.Headshots++;
        }

        AwardBloodBag(killer);
        AnnounceKill(killer, victim, limb);
    }

    private static void AwardBloodBag(UnturnedPlayer killer)
    {
        killer.Player.inventory.forceAddItem(new Item(PeekSeekLobby.Config.BloodBagItemID, true), true);
    }

    private static void AnnounceKill(UnturnedPlayer killer, UnturnedPlayer victim, ELimb limb)
    {
        string weapon = WeaponName(killer.Player.equipment.itemID);
        string key = limb == ELimb.SKULL ? "kill_feed_headshot" : "kill_feed";
        Announce(PeekSeekLobby.Instance.Translate(key, killer.CharacterName, victim.CharacterName, weapon));
    }

    private static string WeaponName(ushort itemID)
    {
        return Assets.find(EAssetType.ITEM, itemID) is ItemAsset asset ? asset.itemName : "something";
    }

    private static void SpawnPlayer(CSteamID steamID, PlayerState state)
    {
        Player player = PlayerTool.getPlayer(steamID);
        if (player == null) return;

        List<LobbySpawn> spawns = PeekSeekLobby.Config.Spawns;
        if (spawns is { Count: > 0 })
        {
            LobbySpawn spawn = spawns[Random.Range(0, spawns.Count)];
            player.teleportToLocationUnsafe(spawn.Position.ToVector3(), player.look.yaw);
        }

        state.SpawnProtectedUntil = Time.realtimeSinceStartup + PeekSeekLobby.Config.SpawnProtectionSeconds;
        SetGodMode(steamID, true);

        EquipRealKit(player, steamID);
    }

    private static void Respawn(CSteamID steamID, PlayerState state)
    {
        state.IsDead = false;

        Player player = PlayerTool.getPlayer(steamID);
        if (player == null) return;

        player.life.ServerRespawn(false);
        SpawnPlayer(steamID, state);
    }

    private static void SetGodMode(CSteamID steamID, bool enabled)
    {
        UnturnedPlayer player = UnturnedPlayer.FromCSteamID(steamID);
        if (player != null) player.Features.GodMode = enabled;
    }

    /// Re-fetched and re-applied on every spawn (join and respawn alike) — each
    /// life starts with the player's real, currently-saved kit, same as a ranked
    /// match would give them, rather than whatever they picked up mid-fight.
    private static void EquipRealKit(Player player, CSteamID steamID)
    {
        System.Threading.Tasks.Task.Run(async () =>
        {
            List<PSItem> items = await RemoteAPI.FetchKit(steamID.m_SteamID, EquipKitCategory);
            if (items == null) return;

            TaskDispatcher.QueueOnMainThread(() =>
            {
                if (player == null) return;
                ClearInventory(player);
                foreach (PSItem item in items)
                {
                    for (byte i = 0; i < item.Amount; i++)
                    {
                        player.inventory.forceAddItem(new Item(item.ItemID, true) { state = item.State }, true);
                    }
                }
            });
        });
    }

    private static void ClearInventory(Player player)
    {
        for (byte page = 0; page < PlayerInventory.PAGES - 1; page++)
        {
            byte count = player.inventory.getItemCount(page);
            for (byte j = 0; j < count; j++)
            {
                player.inventory.removeItem(page, 0);
            }
        }

        player.clothing.updateClothes(
            0, 0, [],
            0, 0, [],
            0, 0, [],
            0, 0, [],
            0, 0, [],
            0, 0, [],
            0, 0, []
        );
    }

    internal static bool ToggleQueueing(CSteamID steamID)
    {
        if (!Players.TryGetValue(steamID.m_SteamID, out PlayerState state)) return false;

        state.Queueing = !state.Queueing;
        if (state.Queueing)
        {
            RemoteAPI.SendHeartbeat(steamID.m_SteamID, PeekSeekLobby.Config.GamemodeBitmask, PeekSeekLobby.Config.szRegion);
        }
        else
        {
            RemoteAPI.SendQuit(steamID.m_SteamID);
        }
        return state.Queueing;
    }

    internal static bool TryGetStats(CSteamID steamID, out PlayerState state)
    {
        return Players.TryGetValue(steamID.m_SteamID, out state);
    }

    internal static void Announce(string message)
    {
        ChatManager.serverSendMessage(
            message,
            UnturnedChat.GetColorFromHex(PeekSeekLobby.Config.szHexDefaultChatColor).Value,
            null, null, EChatMode.SAY, PeekSeekLobby.Config.szChatIconURL, true
        );
    }

    internal static void Whisper(SteamPlayer to, string message)
    {
        ChatManager.serverSendMessage(
            message,
            UnturnedChat.GetColorFromHex(PeekSeekLobby.Config.szHexDefaultChatColor).Value,
            null, to, EChatMode.SAY, PeekSeekLobby.Config.szChatIconURL, true
        );
    }

    /// Everyone still queueing gets pinged on a timer, spawn protection expires on
    /// its own clock, and dead players get whispered a countdown at the specific
    /// seconds-remaining checkpoints configured — same "throttle to fixed
    /// checkpoints" idea the main plugin's lobby-timeout broadcast uses.
    internal static void Tick()
    {
        float now = Time.realtimeSinceStartup;

        foreach (KeyValuePair<ulong, PlayerState> entry in Players)
        {
            CSteamID steamID = new(entry.Key);
            PlayerState state = entry.Value;

            if (state.SpawnProtectedUntil > 0 && now >= state.SpawnProtectedUntil)
            {
                state.SpawnProtectedUntil = 0;
                SetGodMode(steamID, false);
            }

            if (!state.IsDead) continue;

            float secondsLeft = state.DeadUntil - now;
            if (secondsLeft <= 0)
            {
                Respawn(steamID, state);
                continue;
            }

            int rounded = Mathf.RoundToInt(secondsLeft);
            if (!PeekSeekLobby.Config.RespawnCountdownAtSecondsRemaining.Contains((ushort)rounded)) continue;
            if (!state.AnnouncedCountdown.Add(rounded)) continue;

            Player player = PlayerTool.getPlayer(steamID);
            if (player != null)
            {
                Whisper(player.channel.owner, PeekSeekLobby.Instance.Translate("respawn_in", rounded));
            }
        }

        if (now - LastHeartbeatSent < PeekSeekLobby.Config.HeartbeatIntervalSeconds) return;
        LastHeartbeatSent = now;
        SendHeartbeats();
    }

    private static void SendHeartbeats()
    {
        foreach (KeyValuePair<ulong, PlayerState> entry in Players)
        {
            if (!entry.Value.Queueing) continue;
            RemoteAPI.SendHeartbeat(entry.Key, PeekSeekLobby.Config.GamemodeBitmask, PeekSeekLobby.Config.szRegion);
        }
    }
}
