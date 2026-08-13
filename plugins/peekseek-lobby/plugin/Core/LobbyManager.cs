using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
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

internal abstract class LobbyManager {
    internal class PlayerState {
        internal bool Queueing = true;
        internal byte Kills;
        internal byte Deaths;
        internal byte Headshots;
        internal bool IsDead;
        internal float SpawnProtectedUntil;
        internal float DeadUntil;
        internal readonly HashSet<int> AnnouncedCountdown = [];

        /// Which kits.json category (from AllowedKitCategories) this player is
        /// currently equipped with — set on join, changed with /kit.
        internal string KitCategory;

        /// The magazine EquipRealKit last handed out for the current kit, kept
        /// topped up by Tick's magazine check. Zero means the equipped gun has no
        /// mag profile in mmconfig.json, so there's nothing to keep full.
        internal ushort SpareMagItemId;
        internal byte SpareMagFullStock;

        /// Non-zero once matchmaking has paired this player up, until the match's
        /// server claims it and they get relayed over.
        internal int MatchId;

        /// The player's Elo, re-asserted as their reputation on a timer because
        /// Unturned moves reputation itself on kills. Zero until the site answers.
        internal int Elo;
    }

    private static readonly Dictionary<ulong, PlayerState> Players = [];
    private static float LastHeartbeatSent;
    private static float LastMagazineCheck;
    private static float LastReputationSync;
    private static float LastArenaReset;

    private const float MagazineCheckIntervalSeconds = 2f;
    private const float ReputationSyncIntervalSeconds = 5f;
    private const float ProtectedNoticeIntervalSeconds = 3f;
    private const int LeaderboardPlaces = 5;

    private static readonly Dictionary<ulong, float> LastProtectedNotice = [];

    private const string HarmonyId = "peekseek.lobby";
    private static Harmony Instrument;

    internal static void Init() {
        Provider.onServerConnected += OnPlayerConnected;
        Provider.onServerDisconnected += OnPlayerDisconnected;
        UnturnedPlayerEvents.OnPlayerDeath += OnPlayerDead;

        // Attribute-based Harmony patches (ArenaNeutralizePatch) are inert until
        // something actually applies them — this is that something. Without it
        // the native Arena round system, shrinking zone included, keeps running
        // completely unpatched.
        Instrument = new Harmony(HarmonyId);
        Instrument.PatchAll(Assembly.GetExecutingAssembly());

        ItemManager.onServerSpawningItemDrop += OnItemDropRequested;
        DamageTool.damagePlayerRequested += OnDamagePlayerRequested;

        LastArenaReset = Time.realtimeSinceStartup;
        RemoteAPI.Init();
    }

    /// Refusing the drop outright is cheaper and cleaner than spawning it and
    /// deleting it a frame later, and it covers every source at once — death
    /// piles, manual drops, and inventory overflow from a re-equip.
    private static void OnItemDropRequested(Item item, ref Vector3 location, ref bool shouldAllow) {
        if (PeekSeekLobby.Config.RemoveDroppedItems) shouldAllow = false;
    }

    /// Spawn protection already stops the damage; without this the shooter just
    /// sees their hits do nothing and assumes the server is broken. Throttled per
    /// shooter, since this fires once per bullet.
    private static void OnDamagePlayerRequested(ref DamagePlayerParameters parameters, ref bool shouldAllow) {
        CSteamID victimID = parameters.player.channel.owner.playerID.steamID;
        if (!Players.TryGetValue(victimID.m_SteamID, out PlayerState victimState)) return;
        if (victimState.SpawnProtectedUntil <= Time.realtimeSinceStartup) return;

        CSteamID attacker = parameters.killer;
        if (attacker == CSteamID.Nil || attacker == victimID) return;

        float now = Time.realtimeSinceStartup;
        if (LastProtectedNotice.TryGetValue(attacker.m_SteamID, out float last)
                && now - last < ProtectedNoticeIntervalSeconds) return;
        LastProtectedNotice[attacker.m_SteamID] = now;

        Player attackerPlayer = PlayerTool.getPlayer(attacker);
        if (attackerPlayer == null) return;

        Whisper(attackerPlayer.channel.owner, PeekSeekLobby.Instance.Translate(
            "target_protected", parameters.player.channel.owner.playerID.characterName));
    }

    internal static void Shutdown() {
        Provider.onServerConnected -= OnPlayerConnected;
        Provider.onServerDisconnected -= OnPlayerDisconnected;
        UnturnedPlayerEvents.OnPlayerDeath -= OnPlayerDead;

        ItemManager.onServerSpawningItemDrop -= OnItemDropRequested;
        DamageTool.damagePlayerRequested -= OnDamagePlayerRequested;
        Instrument.UnpatchAll(HarmonyId);

        RemoteAPI.Shutdown();
    }

    private static void OnPlayerConnected(CSteamID steamID) {
        Player player = PlayerTool.getPlayer(steamID);
        if (player == null) return;

        bool queueing = PeekSeekLobby.Config.EnableQueue;
        PlayerState state = new() { KitCategory = DefaultKitCategory(), Queueing = queueing };
        Players[steamID.m_SteamID] = state;

        SpawnPlayer(steamID, state);
        RemoteAPI.FetchElo(steamID.m_SteamID, elo => state.Elo = elo);
        if (queueing) {
            RemoteAPI.SendHeartbeat(steamID.m_SteamID, PeekSeekLobby.Config.GamemodeBitmask, PeekSeekLobby.Config.szRegion);
        }

        SendWelcome(player, queueing);

        player.sendBrowserRequest(PeekSeekLobby.Instance.Translate("discord_prompt"), PeekSeekLobby.Config.szDiscordURL);
    }

    /// Three short lines instead of one long one: what this server is and the
    /// loop you are already in, then the controls, then what can wait. Someone
    /// who reads only the first line still knows how the place works.
    private static void SendWelcome(Player player, bool queueing) {
        PeekSeekLobbyConfiguration config = PeekSeekLobby.Config;

        Whisper(player.channel.owner, PeekSeekLobby.Instance.Translate(
            queueing ? "welcome_what_queue" : "welcome_what",
            Mathf.RoundToInt(config.RespawnDelaySeconds), Mathf.RoundToInt(config.SpawnProtectionSeconds)));

        string kits = string.Join("|", config.AllowedKitCategories ?? []);
        string queueHint = queueing ? " <color=white>/noqueue</color> leave the queue." : string.Empty;
        Whisper(player.channel.owner,
            PeekSeekLobby.Instance.Translate("welcome_commands", kits, queueHint));

        Whisper(player.channel.owner, PeekSeekLobby.Instance.Translate(
            "welcome_extra", Mathf.RoundToInt(config.ResetIntervalSeconds / 60f)));
    }

    private static void OnPlayerDisconnected(CSteamID steamID) {
        if (!PeekSeekLobby.Config.EnableQueue) {
            Players.Remove(steamID.m_SteamID);
            return;
        }
        if (Players.Remove(steamID.m_SteamID)) {
            RemoteAPI.SendQuit(steamID.m_SteamID);
        }
    }

    /// Ordinary lobby kills are public — there's no stream overlay here to carry a
    /// kill feed the way a real match's does, so chat is the only kill feed there is.
    private static void OnPlayerDead(UnturnedPlayer victim, EDeathCause cause, ELimb limb, CSteamID murderer) {
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

        if (Players.TryGetValue(murderer.m_SteamID, out PlayerState killerState)) {
            killerState.Kills++;
            if (limb == ELimb.SKULL) killerState.Headshots++;
        }

        GrantRewards(killer.Player, spawning: false);
        AnnounceKill(killer, victim, limb, killerState?.Kills ?? 0);
    }

    /// Tops each reward back up to its cap rather than adding one blindly, so a
    /// player who never spent theirs doesn't accumulate a stack across a killstreak.
    /// A spawn only hands out what GrantOnSpawn allows; a kill restocks everything.
    private static void GrantRewards(Player player, bool spawning) {
        List<RewardItem> rewards = PeekSeekLobby.Config.KillRewards;
        if (rewards is not { Count: > 0 }) return;

        foreach (RewardItem reward in rewards) {
            if (spawning && !reward.GrantOnSpawn) continue;

            int missing = reward.MaxCarried - CountItem(player, reward.ItemID);
            for (int i = 0; i < missing; i++) {
                player.inventory.forceAddItem(new Item(reward.ItemID, true), true);
            }
        }
    }

    private static int CountItem(Player player, ushort itemId) {
        int count = 0;
        for (byte page = 0; page < PlayerInventory.PAGES - 1; page++) {
            byte itemCount = player.inventory.getItemCount(page);
            for (byte i = 0; i < itemCount; i++) {
                ItemJar jar = player.inventory.getItem(page, i);
                if (jar?.item != null && jar.item.id == itemId) count++;
            }
        }
        return count;
    }

    private static void AnnounceKill(UnturnedPlayer killer, UnturnedPlayer victim, ELimb limb, byte killerKills) {
        string weapon = WeaponName(killer.Player.equipment.itemID);
        string key = limb == ELimb.SKULL ? "kill_feed_headshot" : "kill_feed";
        Announce(PeekSeekLobby.Instance.Translate(
            key, killer.CharacterName, victim.CharacterName, weapon, killerKills));
    }

    private static string WeaponName(ushort itemID) {
        return Assets.find(EAssetType.ITEM, itemID) is ItemAsset asset ? asset.itemName : "something";
    }

    private static void SpawnPlayer(CSteamID steamID, PlayerState state) {
        Player player = PlayerTool.getPlayer(steamID);
        if (player == null) return;

        TeleportToSpawn(player);

        state.SpawnProtectedUntil = Time.realtimeSinceStartup + PeekSeekLobby.Config.SpawnProtectionSeconds;
        SetGodMode(steamID, true);

        EquipRealKit(player, steamID, state);
    }

    private static void TeleportToSpawn(Player player) {
        List<LobbySpawn> spawns = PeekSeekLobby.Config.Spawns;
        if (spawns is not { Count: > 0 }) return;

        LobbySpawn spawn = spawns[Random.Range(0, spawns.Count)];
        player.teleportToLocationUnsafe(spawn.Position.ToVector3(), player.look.yaw);
    }

    private static void Respawn(CSteamID steamID, PlayerState state) {
        state.IsDead = false;

        Player player = PlayerTool.getPlayer(steamID);
        if (player == null) return;

        player.life.ServerRespawn(false);
        SpawnPlayer(steamID, state);
    }

    private static void SetGodMode(CSteamID steamID, bool enabled) {
        UnturnedPlayer player = UnturnedPlayer.FromCSteamID(steamID);
        if (player != null) player.Features.GodMode = enabled;
    }

    private static string DefaultKitCategory() {
        List<string> allowed = PeekSeekLobby.Config.AllowedKitCategories;
        return allowed is { Count: > 0 } ? allowed[0] : "meta";
    }

    /// The configured stand-in for a category when the website can't be reached.
    /// Falls back to the first configured kit of any category rather than
    /// nothing, since an armed player in the wrong kit still beats an unarmed one.
    private static KitLoadout FallbackLoadout(string category) {
        List<FallbackKit> kits = PeekSeekLobby.Config.FallbackKits;
        if (kits is not { Count: > 0 }) return null;

        FallbackKit match = kits.FirstOrDefault(kit => kit.Category == category) ?? kits[0];
        return match.ToLoadout();
    }

    /// Re-fetched and re-applied on every spawn (join and respawn alike) — each
    /// life starts with the player's real, currently-saved kit, same as a ranked
    /// match would give them, rather than whatever they picked up mid-fight.
    private static void EquipRealKit(Player player, CSteamID steamID, PlayerState state) {
        string category = state.KitCategory;
        System.Threading.Tasks.Task.Run(async () => {
            KitLoadout loadout = await RemoteAPI.FetchKit(steamID.m_SteamID, category) ?? FallbackLoadout(category);
            if (loadout == null) {
                Logger.LogWarning($"No kit for {steamID} ({category}) and no fallback configured — they spawn empty-handed.");
                return;
            }

            TaskDispatcher.QueueOnMainThread(() => {
                if (player == null) return;
                ClearInventory(player);
                GiveItems(player, loadout.Items);

                state.SpareMagItemId = loadout.Magazine?.ItemID ?? 0;
                state.SpareMagFullStock = loadout.Magazine?.Amount ?? 0;
                if (loadout.Magazine != null) GiveItem(player, loadout.Magazine);

                // After the kit, never before: EquipRealKit clears the inventory
                // first, so rewards granted earlier would be thrown away.
                GrantRewards(player, spawning: true);
            });
        });
    }

    private static void GiveItems(Player player, List<PSItem> items) {
        foreach (PSItem item in items) GiveItem(player, item);
    }

    private static void GiveItem(Player player, PSItem item) {
        for (byte i = 0; i < item.Amount; i++) {
            player.inventory.forceAddItem(new Item(item.ItemID, true) { state = item.State }, true);
        }
    }

    private static void ClearInventory(Player player) {
        for (byte page = 0; page < PlayerInventory.PAGES - 1; page++) {
            byte count = player.inventory.getItemCount(page);
            for (byte j = 0; j < count; j++) {
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

    internal static bool QueueEnabled => PeekSeekLobby.Config.EnableQueue;

    internal static bool ToggleQueueing(CSteamID steamID) {
        if (!QueueEnabled) return false;
        if (!Players.TryGetValue(steamID.m_SteamID, out PlayerState state)) return false;

        state.Queueing = !state.Queueing;
        if (state.Queueing) {
            RemoteAPI.SendHeartbeat(steamID.m_SteamID, PeekSeekLobby.Config.GamemodeBitmask, PeekSeekLobby.Config.szRegion);
        } else {
            RemoteAPI.SendQuit(steamID.m_SteamID);
        }
        return state.Queueing;
    }

    internal static bool TryGetStats(CSteamID steamID, out PlayerState state) {
        return Players.TryGetValue(steamID.m_SteamID, out state);
    }

    /// A matched player stops fighting: back to a spawn point, out of the queue
    /// so the lobby stops heartbeating for them, and handed the link that logs
    /// them in and lands them on their match page. Hitting /match while logged
    /// out is itself what records it as the page to return to, so the site sends
    /// them back there after login without needing a redirect parameter.
    internal static void SendToMatch(ulong steam64, int matchId) {
        if (!Players.TryGetValue(steam64, out PlayerState state)) return;
        // A heartbeat already in flight when the first one matched would
        // otherwise teleport them a second time.
        if (!state.Queueing) return;
        state.Queueing = false;
        state.MatchId = matchId;

        CSteamID steamID = new(steam64);
        Player player = PlayerTool.getPlayer(steamID);
        if (player == null) return;

        TeleportToSpawn(player);
        state.SpawnProtectedUntil = Time.realtimeSinceStartup + PeekSeekLobby.Config.SpawnProtectionSeconds;
        SetGodMode(steamID, true);

        Whisper(player.channel.owner, PeekSeekLobby.Instance.Translate("match_found"));
        player.sendBrowserRequest(
            PeekSeekLobby.Instance.Translate("match_found_browser"), PeekSeekLobby.Config.szMatchURL);
    }

    /// Once a game server has claimed the match it's ready to be played, so the
    /// waiting player is handed over to it — the same relay the main plugin uses
    /// to send everyone back here when a match ends. "0" is what a server too old
    /// to report its own code claims with, and relaying there would drop them.
    internal static void RelayToMatch(ulong steam64, string serverCode) {
        if (!Players.TryGetValue(steam64, out PlayerState state) || state.MatchId == 0) return;
        if (string.IsNullOrEmpty(serverCode) || serverCode == "0") return;
        if (!ulong.TryParse(serverCode, out ulong serverId)) return;

        Player player = PlayerTool.getPlayer(new CSteamID(steam64));
        if (player == null) return;

        state.MatchId = 0;
        Whisper(player.channel.owner, PeekSeekLobby.Instance.Translate("match_started"));
        player.sendRelayToServer(new CSteamID(serverId), string.Empty, true);
    }

    /// Switches the caller's kit category and re-equips them immediately (no
    /// teleport, no fresh spawn protection — just what they're holding). False for
    /// an unrecognised category or a player Tick doesn't know about.
    internal static bool SwitchKit(CSteamID steamID, string category) {
        if (!Players.TryGetValue(steamID.m_SteamID, out PlayerState state)) return false;
        if (!PeekSeekLobby.Config.AllowedKitCategories.Contains(category)) return false;

        state.KitCategory = category;
        Player player = PlayerTool.getPlayer(steamID);
        if (player != null) EquipRealKit(player, steamID, state);
        return true;
    }

    internal static void Announce(string message) {
        ChatManager.serverSendMessage(
            message,
            UnturnedChat.GetColorFromHex(PeekSeekLobby.Config.szHexDefaultChatColor).Value,
            null, null, EChatMode.SAY, PeekSeekLobby.Config.szChatIconURL, true
        );
    }

    internal static void Whisper(SteamPlayer to, string message) {
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
    internal static void Tick() {
        float now = Time.realtimeSinceStartup;

        foreach (KeyValuePair<ulong, PlayerState> entry in Players) {
            CSteamID steamID = new(entry.Key);
            PlayerState state = entry.Value;

            if (state.SpawnProtectedUntil > 0 && now >= state.SpawnProtectedUntil) {
                state.SpawnProtectedUntil = 0;
                SetGodMode(steamID, false);

                Player protectedPlayer = PlayerTool.getPlayer(steamID);
                if (protectedPlayer != null) {
                    Whisper(protectedPlayer.channel.owner,
                        PeekSeekLobby.Instance.Translate("spawn_protection_over"));
                }
            }

            if (!state.IsDead) continue;

            float secondsLeft = state.DeadUntil - now;
            if (secondsLeft <= 0) {
                Respawn(steamID, state);
                continue;
            }

            int rounded = Mathf.RoundToInt(secondsLeft);
            if (!PeekSeekLobby.Config.RespawnCountdownAtSecondsRemaining.Contains((ushort)rounded)) continue;
            if (!state.AnnouncedCountdown.Add(rounded)) continue;

            Player player = PlayerTool.getPlayer(steamID);
            if (player != null) {
                Whisper(player.channel.owner, PeekSeekLobby.Instance.Translate("respawn_in", rounded));
            }
        }

        if (now - LastReputationSync >= ReputationSyncIntervalSeconds) {
            LastReputationSync = now;
            SyncReputations();
        }

        if (now - LastArenaReset >= PeekSeekLobby.Config.ResetIntervalSeconds) {
            LastArenaReset = now;
            ResetArena();
        }

        if (now - LastMagazineCheck >= MagazineCheckIntervalSeconds) {
            LastMagazineCheck = now;
            TopUpMagazines();
        }

        if (now - LastHeartbeatSent < PeekSeekLobby.Config.HeartbeatIntervalSeconds) return;
        LastHeartbeatSent = now;
        SendHeartbeats();
    }

    private static void TopUpMagazines() {
        foreach (KeyValuePair<ulong, PlayerState> entry in Players) {
            PlayerState state = entry.Value;
            if (state.SpareMagItemId == 0) continue;

            Player player = PlayerTool.getPlayer(new CSteamID(entry.Key));
            if (player == null) continue;

            RestockMagazines(player, state);
        }
    }

    /// Reloading doesn't consume the spare — it swaps the spent magazine back into
    /// the inventory — so the magazine *count* never drops and counting alone sees
    /// nothing wrong while every spare sits empty. Partly-used magazines are
    /// dropped and replaced instead, which also self-heals a spare that was
    /// thrown away rather than fired.
    private static void RestockMagazines(Player player, PlayerState state) {
        byte capacity = MagazineCapacity(state.SpareMagItemId);
        int full = 0;

        for (byte page = 0; page < PlayerInventory.PAGES - 1; page++) {
            // Backwards: removing an item shifts everything after it down an index.
            for (int index = player.inventory.getItemCount(page) - 1; index >= 0; index--) {
                ItemJar jar = player.inventory.getItem(page, (byte)index);
                if (jar?.item == null || jar.item.id != state.SpareMagItemId) continue;

                if (capacity > 0 && jar.item.amount < capacity) {
                    player.inventory.removeItem(page, (byte)index);
                    continue;
                }
                full++;
            }
        }

        for (int i = full; i < state.SpareMagFullStock; i++) {
            player.inventory.forceAddItem(new Item(state.SpareMagItemId, true), true);
        }
    }

    /// Rounds the magazine holds when full. Zero when the id isn't a magazine
    /// asset, which makes RestockMagazines keep every one it finds rather than
    /// deleting items it can't reason about.
    private static byte MagazineCapacity(ushort itemId) {
        return Assets.find(EAssetType.ITEM, itemId) is ItemMagazineAsset asset ? asset.amount : (byte)0;
    }

    /// Unturned adjusts reputation itself when players kill each other, so this
    /// keeps putting it back — the number over someone's head is their Elo and
    /// nothing else.
    private static void SyncReputations() {
        if (!PeekSeekLobby.Config.ShowEloAsReputation) return;

        foreach (KeyValuePair<ulong, PlayerState> entry in Players) {
            if (entry.Value.Elo == 0) continue;

            Player player = PlayerTool.getPlayer(new CSteamID(entry.Key));
            if (player == null) continue;

            int drift = entry.Value.Elo - player.skills.reputation;
            if (drift != 0) player.skills.askRep(drift);
        }
    }

    /// Wipes the arena back to how it started: nothing left on the ground,
    /// nothing built, every session score back to zero and everyone respawned
    /// with a fresh kit. Elo is deliberately untouched — it isn't a session stat.
    private static void ResetArena() {
        AnnounceLeaderboard();

        ItemManager.askClearAllItems();
        BarricadeManager.askClearAllBarricades();
        StructureManager.askClearAllStructures();

        foreach (KeyValuePair<ulong, PlayerState> entry in Players) {
            PlayerState state = entry.Value;
            state.Kills = 0;
            state.Deaths = 0;
            state.Headshots = 0;
            state.IsDead = false;
            state.AnnouncedCountdown.Clear();

            CSteamID steamID = new(entry.Key);
            Player player = PlayerTool.getPlayer(steamID);
            if (player == null) continue;

            if (player.life.isDead) player.life.ServerRespawn(false);
            SpawnPlayer(steamID, state);
        }

        Announce(PeekSeekLobby.Instance.Translate("arena_reset"));
    }

    /// Read out before the wipe, since the scores it reports are exactly what is
    /// about to be cleared.
    private static void AnnounceLeaderboard() {
        List<KeyValuePair<ulong, PlayerState>> ranked = Players
            .Where(entry => entry.Value.Kills > 0)
            .OrderByDescending(entry => entry.Value.Kills)
            .ThenBy(entry => entry.Value.Deaths)
            .Take(LeaderboardPlaces)
            .ToList();

        int minutes = Mathf.RoundToInt(PeekSeekLobby.Config.ResetIntervalSeconds / 60f);
        if (ranked.Count == 0) {
            Announce(PeekSeekLobby.Instance.Translate("leaderboard_empty"));
            return;
        }

        Announce(PeekSeekLobby.Instance.Translate("leaderboard_header", minutes));
        for (int place = 0; place < ranked.Count; place++) {
            PlayerState state = ranked[place].Value;
            string name = UnturnedPlayer.FromCSteamID(new CSteamID(ranked[place].Key))?.CharacterName
                ?? ranked[place].Key.ToString();
            int headshotPercent = state.Kills > 0 ? state.Headshots * 100 / state.Kills : 0;

            Announce(PeekSeekLobby.Instance.Translate(
                "leaderboard_entry", place + 1, name, state.Kills, state.Deaths, headshotPercent));
        }
    }

    private static void SendHeartbeats() {
        if (!PeekSeekLobby.Config.EnableQueue) return;

        foreach (KeyValuePair<ulong, PlayerState> entry in Players) {
            // Already matched: stop asking for a match and start watching for the
            // server that will host it, so they get relayed the moment it claims.
            if (entry.Value.MatchId != 0) {
                RemoteAPI.PollMatchStart(entry.Key, entry.Value.MatchId);
                continue;
            }
            if (!entry.Value.Queueing) continue;
            RemoteAPI.SendHeartbeat(entry.Key, PeekSeekLobby.Config.GamemodeBitmask, PeekSeekLobby.Config.szRegion);
        }
    }
}
