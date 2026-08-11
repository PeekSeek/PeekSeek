using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using HarmonyLib;
using Newtonsoft.Json;
using PeekSeek.Core.Models;
using PeekSeek.Core.Models.Enums;
using PeekSeek.Core.Web;
using Rocket.Core;
using Rocket.Core.Utils;
using Rocket.Unturned.Chat;
using Rocket.Unturned.Events;
using Rocket.Unturned.Player;
using SDG.Unturned;
using Steamworks;
using UnityEngine;
using Logger = Rocket.Core.Logging.Logger;

namespace PeekSeek.Core;

internal abstract class PSManager {
    internal static EPSStatus Status;

    internal static PSData Data;

    internal static float MatchReady;
    private static List<GroupInfoEx> GroupInfos;

    private const float UpdateIntervalSeconds = 1f;
    private static float LastUpdateSent;

    private const float VisibilityIntervalSeconds = 0.15f;
    private static float LastVisibilityCheck;

    private static PSUpdateEvent LatestEvent;
    private static int EventsRecorded;

    private const int FeedLength = 5;
    private static readonly List<PSUpdateKill> RecentKills = [];

    /// Separate from EventsRecorded: this ids the durable kill log sent to /unt/kills,
    /// not the ephemeral overlay feed, and the two have no reason to share a sequence.
    private static int KillEventsRecorded;

    private static CSteamID RelaySteamID => new(PeekSeek.Config.LobbyServerRelaySGSSteamID);

    private static Harmony Instrument;

    internal static void Init() {
        Provider.onCheckValidWithExplanation += OnCheckValidWithExplanation;
        Provider.onServerConnected += OnClientConnected;
        Provider.onServerDisconnected += OnClientDisconnected;

        UnturnedPlayerEvents.OnPlayerDeath += OnPlayerDead;
        UnturnedPlayerEvents.OnPlayerRevive += OnPlayerRevive;
        PlayerLife.onPlayerLifeUpdated += OnLifeUpdated;


        Instrument = new Harmony("blaze.ps.main");
        Instrument.PatchAll(Assembly.GetExecutingAssembly());

        Level.onPostLevelLoaded += OnPostLevelLoaded;

        DamageTool.damagePlayerRequested += OnDamageRequested;
        UseableGun.onBulletSpawned += OnBulletSpawned;
        RemoteAPI.Init();
    }

    private static void OnDamageRequested(ref DamagePlayerParameters parameters, ref bool shouldAllow) {
        Player attacker = PlayerTool.getPlayer(parameters.killer);

        if (attacker != null && parameters.damage > 5) {
            float ping = attacker.channel.owner.ping * 1000f;

            foreach (var config in PeekSeek.Config.PingConfig.OrderByDescending(c => c.PingOrHigher)) {
                Logger.Log($"Checking {config.PingOrHigher} >= {ping}");
                if (!(ping >= config.PingOrHigher)) continue;
                parameters.damage *= (config.Precentage / 100f);
                break;
            }
        }

        // Recorded after the ping scaling above, so damage stats and the fight history
        // both reflect what the victim actually took rather than the pre-reduction number.
        CSteamID victimID = parameters.player.channel.owner.playerID.steamID;
        StatService.AddDamageTaken(victimID, parameters.damage);

        float? reactionTime = null;
        if (attacker != null) {
            StatService.AddDamageDealt(parameters.killer, parameters.damage);
            reactionTime = VisibilityTracker.ConsumeReactionTime(parameters.killer);
        }

        FightTracker.RecordHit(
            victimID,
            parameters.killer,
            parameters.damage,
            parameters.limb,
            attacker?.transform.position ?? default,
            parameters.player.transform.position,
            reactionTime
        );

        RoundLog.RecordDamage(parameters.killer, victimID, parameters.damage);
    }

    /// Only worth sampling in bullet-drop mode: instant-hit mode has no equivalent
    /// per-shot event, and Data is null outside a live match, so both are guarded here
    /// rather than asking every caller to remember the condition. Third-person is also
    /// excluded — PeekSeek is played first-person, but if a player ever ends up in
    /// third-person the aim direction no longer matches where their reticle actually was,
    /// so a spray-offset sample from that shot would be quietly wrong rather than missing.
    private static void OnBulletSpawned(UseableGun gun, BulletInfo bullet) {
        if (Data?.Settings?.Ballistics != true) return;
        if (gun.player.look.perspective != EPlayerPerspective.FIRST) return;

        FightTracker.RecordShot(
            gun.player.channel.owner.playerID.steamID,
            bullet.origin,
            bullet.ApproximatePlayerAimDirection
        );
    }


    private static void OnPostLevelLoaded(int level) {
        RemoteAPI.StartWaitingForMessages();
        Status = EPSStatus.READY;
    }

    internal static void Shutdown() {
        Provider.onCheckValidWithExplanation -= OnCheckValidWithExplanation;
        Provider.onServerConnected -= OnClientConnected;
        Provider.onServerDisconnected -= OnClientDisconnected;

        UnturnedPlayerEvents.OnPlayerDeath -= OnPlayerDead;
        UnturnedPlayerEvents.OnPlayerRevive -= OnPlayerRevive;
        PlayerLife.onPlayerLifeUpdated -= OnLifeUpdated;

        Instrument.UnpatchAll("blaze.ps.main");

        Level.onPostLevelLoaded -= OnPostLevelLoaded;

        DamageTool.damagePlayerRequested -= OnDamageRequested;
        UseableGun.onBulletSpawned -= OnBulletSpawned;

        RemoteAPI.Shutdown();
    }

    internal static void ProcessData(PSData data) {
        if (data == null) {
            Logger.LogError("Data is null!");
            return;
        }

        switch (data.Head) {
            case "start": {
                    if (data.MatchID != Data?.MatchID) {
                        KickAllClients("Match has been canceled!");
                        Status = EPSStatus.READY;
                    }

                    if (Status == EPSStatus.IN_PROGRESS) return;
                    UpdateStatus(EPSStatus.IN_PROGRESS);

                    ConfigurationManager.SetBulletdrop(data.Settings.Ballistics);
                    ConfigurationManager.SetOldMovement(data.Settings.Movement);

                    MatchReady = Time.realtimeSinceStartup;
                    KickAllClients("Match is restarting!");

                    GroupInfos = CreateGroups(
                        data.Teams
                    );

                    Data = data;
                    SendUpdateState();
                    break;
                }
            case "stop":
                UpdateStatus(EPSStatus.READY);
                StatService.InstanceStats.Clear();
                LatestEvent = null;
                EventsRecorded = 0;
                KillEventsRecorded = 0;
                FightTracker.Clear();
                RoundLog.Clear();
                LastKilledBy.Clear();
                VisibilityTracker.Clear();
                KickAllClients("Match has been forcefully!");
                Data = null;
                GroupManager.knownGroups.Clear();
                break;
        }
    }

    private static void OnClientConnected(CSteamID steamID) {
        Player player = PlayerTool.getPlayer(steamID);
        if (player == null) return;

        if (IsSpectator(steamID)) {
            UnturnedPlayer unturnedPlayer = UnturnedPlayer.FromPlayer(player);
            R.Permissions.GetGroups(unturnedPlayer, true).ForEach(r => R.Permissions.RemovePlayerFromGroup(r.Id, unturnedPlayer));
            R.Permissions.AddPlayerToGroup(PeekSeek.Config.szSpectatorGroup, unturnedPlayer);

            Vector3 originalPosition = player.transform.position;
            float yaw = player.look.yaw;
            player.teleportToLocationUnsafe(new Vector3(-100, -500, -100), yaw);

            unturnedPlayer.Features.VanishMode = true;
            unturnedPlayer.Player.look.sendFreecamAllowed(true);
            unturnedPlayer.Player.look.sendSpecStatsAllowed(true);

            player.teleportToLocationUnsafe(originalPosition, yaw);
            return;
        }

        if (player.quests.groupID != GetPlayerTeamGroupID(player)) AssignPlayerGroup(player);
        AssignPlayerKit(player, Data.Users.First(
                x => x.UserID == steamID.m_SteamID
            ).Kit
        );

        ChatManager.serverSendMessage(
            PeekSeek.Instance.Translate("welcome"),
            UnturnedChat.GetColorFromHex(PeekSeek.Config.szHexDefaultChatColor).Value,
            null,
            player.channel.owner,
            EChatMode.SAY,
            PeekSeek.Config.szChatIconURL, true
        );
    }

    private static void OnClientDisconnected(CSteamID steamid) {
        Player player = PlayerTool.getPlayer(steamid);
        if (player == null) return;

        if (!IsSpectator(steamid)) return;

        UnturnedPlayer unturnedPlayer = UnturnedPlayer.FromPlayer(player);
        R.Permissions.RemovePlayerFromGroup(PeekSeek.Config.szSpectatorGroup, unturnedPlayer);
    }

    private static void OnCheckValidWithExplanation(ValidateAuthTicketResponse_t callback, ref bool isValid, ref string explanation) {
        SteamPending pending = Provider.pending.First(
            x => x.playerID.steamID == callback.m_SteamID
        );

        ulong ulSteamID = callback.m_SteamID.m_SteamID;

        if (Data == null) {
            isValid = false;
            explanation = "This server is not hosting a match!";
            return;
        }

        if (Status != EPSStatus.IN_PROGRESS) {
            isValid = false;
            explanation = "The match has not started!";
            return;
        }

        if (Data.Teams.Any(x => x.Users.Contains(ulSteamID) || IsSpectator(ulSteamID))) return;
        isValid = false;
        explanation = "You do not have permission to join this match!";
        return;
    }

    private static List<GroupInfoEx> CreateGroups(List<PSTeam> teams) {
        List<GroupInfoEx> infos = [];

        GroupManager.knownGroups.Clear();

        foreach (var team in teams) {
            GroupInfo sdgInfo = GroupManager.addGroup(
                GroupManager.generateUniqueGroupID(),
                team.Name
            );

            if (sdgInfo == null) {
                Logger.LogError($"Failed to create group for {team.Name}");
                continue;
            }

            infos.Add(new GroupInfoEx() {
                TeamName = team.Name,
                GroupInfo = sdgInfo
            });
        }

        return infos;
    }

    private static void AssignPlayerKit(Player player, List<PSItem> kit) {
        for (byte i = 0; i < PlayerInventory.PAGES - 1; i++) {
            byte count = player.inventory.getItemCount(i);

            for (byte j = 0; j < count; j++) {
                player.inventory.removeItem(i, 0);
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


        foreach (PSItem item in kit) {
            for (byte i = 0; i < item.Amount; i++) {
                player.inventory.forceAddItem(new Item(item.ItemID, true) { state = item.State }, true);
            }
        }
    }

    private static void AssignPlayerGroup(Player player) {
        player.quests.leaveGroup(true);
        GroupManager.alertGroupmatesLeft(player);

        player.quests.ServerAssignToGroup(
            GroupInfos.First(
                x => x.TeamName == GetPlayerTeam(player.channel.owner.playerID.steamID.m_SteamID).Name
            ).GroupInfo.groupID,
            EPlayerGroupRank.MEMBER,
            true
        );
    }

    private static void KickAllClients(string msg = "You have been kicked!") {
        foreach (var client in Provider.clients.ToList()) {
            Provider.kick(
                client.playerID.steamID,
                msg
            );
        }
    }

    /// Hands every client over to the lobby server before dropping them, when one is configured.
    private static void RelayAllClients() {
        foreach (var client in Provider.clients.ToList()) {
            if (RelaySteamID == CSteamID.Nil || client.player == null) continue;
            client.player.sendRelayToServer(RelaySteamID, string.Empty, true);
        }

        KickAllClients("Relay");
    }

    internal static void Announce(string message) {
        ChatManager.serverSendMessage(
            message,
            UnturnedChat.GetColorFromHex(PeekSeek.Config.szHexDefaultChatColor).Value,
            null,
            null,
            EChatMode.SAY,
            PeekSeek.Config.szChatIconURL,
            true
        );
    }

    /// Same as Announce but to one player only — StatsCommand also uses this rather than
    /// building its own ChatManager call.
    internal static void Whisper(SteamPlayer to, string message) {
        ChatManager.serverSendMessage(
            message,
            UnturnedChat.GetColorFromHex(PeekSeek.Config.szHexDefaultChatColor).Value,
            null,
            to,
            EChatMode.SAY,
            PeekSeek.Config.szChatIconURL,
            true
        );
    }

    private static PSTeam GetPlayerTeam(ulong steamID) {
        return Data.Teams.First(
            x => x.Users.Contains(steamID)
        );
    }

    private static CSteamID GetPlayerTeamGroupID(Player player) {
        string teamID = GetPlayerTeam(player.channel.owner.playerID.steamID.m_SteamID).Name;

        GroupInfoEx info = GroupInfos.FirstOrDefault(
            x => x.TeamName == teamID
        );

        return info != null ? info.GroupInfo.groupID : CSteamID.Nil;
    }

    internal static CSteamID GetPlayerTeamGroupID(CSteamID steamID) {
        string teamID = GetPlayerTeam(steamID.m_SteamID).Name;

        GroupInfoEx info = GroupInfos.FirstOrDefault(
            x => x.TeamName == teamID
        );

        return info != null ? info.GroupInfo.groupID : CSteamID.Nil;
    }

    internal static CSteamID GetTeamGroupID(PSTeam team) {
        GroupInfoEx info = GroupInfos.FirstOrDefault(
            x => x.TeamName == team.Name
        );
        return info != null ? info.GroupInfo.groupID : CSteamID.Nil;
    }

    internal static string GetTeamGroupName(CSteamID groupID) {
        GroupInfoEx info = GroupInfos.FirstOrDefault(
            x => x.GroupInfo.groupID == groupID
        );

        return info != null ? info.TeamName : "NONE_TEAM_NULL";
    }

    private static bool IsOnline(ulong steam64) {
        return Provider.clients.Any(client => client.playerID.steamID.m_SteamID == steam64);
    }

    internal static bool HaveAllPlayersJoined() {
        return Data.Teams.SelectMany(team => team.Users).All(IsOnline);
    }

    internal static bool HasAnyPlayerJoined() {
        return Data.Teams.SelectMany(team => team.Users).Any(IsOnline);
    }

    /// The single team that still has players on the server, or null when it is not exactly one.
    internal static PSTeam OnlyTeamOnline() {
        List<PSTeam> online = Data.Teams.Where(team => team.Users.Any(IsOnline)).ToList();
        return online.Count == 1 ? online[0] : null;
    }

    private static void UpdateStatus(EPSStatus status) {
        Status = status;
        //RemoteAPI.NotifyServerStatus(status);
    }

    internal static void NotifyTeamWon(ulong playerID) {
        PSTeam team = GetPlayerTeam(playerID);

        foreach (ulong user in team.Users) {
            StatService.UpdateStatWins(new(user));
        }

        Announce(PeekSeek.Instance.Translate("win", team.Name));
        AnnounceAceIfAny(team);
        WhisperCloseCalls();
        WhisperRoundDamage();

        if (StatService.GetStatWins(new(playerID)) < Data.Meta.Rounds) {
            RecordEvent("roundWon", team.Name);
            SendUpdateState();
            RoundLog.Clear();
            return;
        }

        UpdateStatus(EPSStatus.DELAY);
        Announce(PeekSeek.Instance.Translate("match_won", team.Name));
        AnnounceMvp();
        RecordEvent("matchWon", team.Name);
        SendUpdateState();
        RoundLog.Clear();
        Task.Run(() => DelayEnd(PeekSeek.Config.DelayKickAfterWin, team.Name));
    }

    /// A solo ace only means something in a team format — a 1v1 win is just a win — and
    /// only generalises cleanly to two teams, so FFA/multi-team matches skip it rather
    /// than guessing at what "the opposing team" means there.
    private static void AnnounceAceIfAny(PSTeam winningTeam) {
        if (Data.Teams.Count != 2) return;

        PSTeam losingTeam = Data.Teams.First(t => t.Name != winningTeam.Name);
        if (losingTeam.Users.Count < 2) return;

        foreach (ulong user in winningTeam.Users) {
            if (RoundLog.KillsBy(new CSteamID(user)) < losingTeam.Users.Count) continue;

            string name = UnturnedPlayer.FromCSteamID(new CSteamID(user))?.CharacterName ?? user.ToString();
            Announce(PeekSeek.Instance.Translate("ace", name));
            return;
        }
    }

    private const byte CloseCallHealthThreshold = 15;

    /// Private, not public — celebrates surviving the round to whoever actually did it,
    /// without turning "I nearly died" into public information for everyone else.
    private static void WhisperCloseCalls() {
        foreach (PSTeam team in Data.Teams) {
            foreach (ulong user in team.Users) {
                Player p = PlayerTool.getPlayer(new CSteamID(user));
                if (p == null || p.life.isDead || p.life.health > CloseCallHealthThreshold) continue;

                Whisper(p.channel.owner, PeekSeek.Instance.Translate("close_call", p.life.health));
            }
        }
    }

    /// One whisper per player who dealt damage this round, broken down by who they were
    /// fighting — built from RoundLog rather than FightTracker, since this covers the
    /// whole round rather than only the fight that ended in a kill.
    private static void WhisperRoundDamage() {
        foreach (PSTeam team in Data.Teams) {
            foreach (ulong user in team.Users) {
                List<RoundDamageEntry> entries = RoundLog.DamageBy(new CSteamID(user));
                if (entries.Count == 0) continue;

                Player p = PlayerTool.getPlayer(new CSteamID(user));
                if (p == null) continue;

                string breakdown = string.Join(" · ", entries.Select(entry => {
                    string name = UnturnedPlayer.FromCSteamID(new CSteamID(entry.Victim))?.CharacterName
                        ?? entry.Victim.ToString();
                    return $"{name}: {Mathf.Round(entry.Damage)} dmg ({entry.Hits} hits)";
                }));

                Whisper(p.channel.owner, PeekSeek.Instance.Translate("round_damage", breakdown));
            }
        }
    }

    /// Most kills, headshots as the tiebreak. Says nothing for a round that ended without
    /// a single kill rather than naming an arbitrary "MVP" who did nothing.
    private static void AnnounceMvp() {
        if (StatService.InstanceStats.Count == 0) return;

        PSPlayerStat mvp = StatService.InstanceStats
            .OrderByDescending(s => s.Kills)
            .ThenByDescending(s => s.Headshots)
            .First();
        if (mvp.Kills == 0) return;

        string name = UnturnedPlayer.FromCSteamID(new CSteamID(mvp.SteamID))?.CharacterName ?? mvp.SteamID.ToString();
        int hsPercent = mvp.Headshots * 100 / mvp.Kills;
        Announce(PeekSeek.Instance.Translate("mvp", name, mvp.Kills, hsPercent));
    }

    /// Awards the whole match to the only team that showed up before the lobby timed out.
    internal static void NotifyTeamForcedWin(PSTeam team) {
        for (byte round = 0; round < Data.Meta.Rounds; round++) {
            foreach (ulong user in team.Users) {
                StatService.UpdateStatWins(new(user));
            }
        }

        EndMatch(team.Name);
    }

    /// Reports a match nobody turned up for, so the backend can release it.
    internal static void AbandonMatch() {
        SendResults(string.Empty, isPlayed: false);
        ClearMatch();
    }

    private static async Task DelayEnd(float seconds, string winningTeam) {
        await Task.Delay(TimeSpan.FromSeconds(seconds));

        TaskDispatcher.QueueOnMainThread(() => {
            if (Status != EPSStatus.DELAY) {
                Logger.LogError($"DelayEnd() ran with status {Status}, expected {EPSStatus.DELAY}");
                return;
            }

            EndMatch(winningTeam);
        });
    }

    private static void EndMatch(string winningTeam) {
        SendResults(winningTeam);
        ClearMatch();
    }

    private static void ClearMatch() {
        StatService.InstanceStats.Clear();
        LatestEvent = null;
        EventsRecorded = 0;
        KillEventsRecorded = 0;
        RecentKills.Clear();
        FightTracker.Clear();
        RoundLog.Clear();
        LastKilledBy.Clear();
        VisibilityTracker.Clear();
        GroupManager.knownGroups.Clear();
        RelayAllClients();
        UpdateStatus(EPSStatus.READY);
        Data = null;
    }

    internal static void SendResults(string winningTeam, bool isPlayed = true) {
        List<PSPlayerStat> stats = isPlayed ? StatService.InstanceStats : [];

        var payload = new {
            status = "finished",
            played = isPlayed,
            data = stats.Select(stat => new {
                steam64 = stat.SteamID.ToString(),
                win = GetPlayerTeam(stat.SteamID).Name == winningTeam,
                kills = stat.Kills,
                deaths = stat.Deaths,
                headshots = stat.Headshots,
                damageDealt = (int)Math.Round(stat.DamageDealt),
                damageTaken = (int)Math.Round(stat.DamageTaken)
            })
        };

        RemoteAPI.SendResults(
            JsonConvert.SerializeObject(payload),
            Data.MatchID
        );
    }

    /// Who last killed each player this match, for the revenge whisper. Steam64 to steam64,
    /// cleared with the rest of the match's state.
    private static readonly Dictionary<ulong, ulong> LastKilledBy = [];

    /// Ordinary kills say nothing publicly — the stream overlay already carries the kill
    /// feed for spectators, and the round/match win announcements below still mark the
    /// moments that actually matter. Two things replace the old public line: a revenge
    /// whisper to the killer, and a flavoured private line to anyone who died without a
    /// killer at all, so silence isn't the only thing an ordinary run of the mill death
    /// produces for the person on the wrong end of it.
    private static void OnPlayerDead(UnturnedPlayer player, EDeathCause cause, ELimb limb, CSteamID murderer) {
        if (cause == EDeathCause.ARENA) return;

        StatService.UpdateStatDeaths(player.CSteamID);
        StatService.MarkDeadThisRound(player.CSteamID);

        UnturnedPlayer unturnedInstigator = null;
        if (murderer != CSteamID.Nil && murderer != SteamGameServer.GetSteamID()) {
            unturnedInstigator = UnturnedPlayer.FromCSteamID(murderer);
            StatService.UpdateStatKills(murderer);
            if (limb == ELimb.SKULL) StatService.UpdateStatHeadshots(murderer);
            RoundLog.RecordKill(murderer);

            if (LastKilledBy.TryGetValue(murderer.m_SteamID, out ulong murderersLastKiller)
                    && murderersLastKiller == player.CSteamID.m_SteamID) {
                Whisper(unturnedInstigator.Player.channel.owner,
                    PeekSeek.Instance.Translate("revenge", player.CharacterName));
            }

            LastKilledBy[player.CSteamID.m_SteamID] = murderer.m_SteamID;
        } else {
            string key = DeathCauseTranslationKey(cause);
            if (key != null) Whisper(player.Player.channel.owner, PeekSeek.Instance.Translate(key));
        }

        RecordKill(player, unturnedInstigator, limb);
        SendKillEvent(player, unturnedInstigator, cause, limb, murderer);

        player.Player.life.ServerRespawn(false);
        SendUpdateState();
    }

    /// Only the causes realistic in an arena match get a line — the rest (zombies,
    /// animals, infection, ...) can't happen here, so they fall back to the generic one
    /// rather than growing this list for causes that will never fire.
    internal static string DeathCauseTranslationKey(EDeathCause cause) {
        return cause switch {
            EDeathCause.BONES => "death_fall",
            EDeathCause.WATER => "death_drown",
            EDeathCause.BURNING => "death_burn",
            EDeathCause.FREEZING => "death_freeze",
            EDeathCause.BLEEDING => "death_bleed",
            EDeathCause.SUICIDE => "death_suicide",
            EDeathCause.GRENADE or EDeathCause.LANDMINE or EDeathCause.MISSILE or EDeathCause.CHARGE => "death_explosive",
            _ => "death_generic"
        };
    }

    /// Builds the durable kill record for /unt/kills from whatever FightTracker buffered,
    /// separate from RecordKill's ephemeral overlay feed entry above.
    private static void SendKillEvent(UnturnedPlayer victim, UnturnedPlayer killer, EDeathCause cause, ELimb limb, CSteamID murderer) {
        bool ballistics = Data.Settings.Ballistics;

        RemoteAPI.SendKillEvent(new PSKillEvent {
            Id = ++KillEventsRecorded,
            Time = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            Map = Data.Settings.Map,
            Round = StatService.GetTotalPlayedRounds() + 1,
            Killer = killer?.CSteamID.ToString(),
            KillerName = killer?.CharacterName,
            Victim = victim.CSteamID.ToString(),
            VictimName = victim.CharacterName,
            Cause = cause.ToString().ToLowerInvariant(),
            Limb = limb.ToString().ToLowerInvariant(),
            Weapon = killer?.Player.equipment.itemID ?? 0,
            KillerPosition = killer != null ? PSVector3.From(killer.Player.transform.position) : null,
            VictimPosition = PSVector3.From(victim.Player.transform.position),
            Hits = FightTracker.TakeFight(victim.CSteamID),
            ReciprocalHits = murderer != CSteamID.Nil
                ? FightTracker.TakeHitsFrom(murderer, victim.CSteamID)
                : [],
            Shots = murderer != CSteamID.Nil && ballistics ? FightTracker.RecentShotsBy(murderer) : []
        });
    }

    private static void OnPlayerRevive(UnturnedPlayer player, Vector3 position, byte angle) {
        if (IsSpectator(Player.LocalPlayer)) return;

        AssignPlayerKit(player.Player, Data.Users.First(
                x => x.UserID == player.CSteamID.m_SteamID
            ).Kit
        );

        player.Heal(255);
    }

    private static void OnLifeUpdated(Player player) {
        if (player.life.isDead) player.life.ServerRespawn(false);
    }

    /// Pushes a snapshot on a timer as well as on events, so health, held weapon and
    /// remaining powerup items move on the overlay between kills rather than freezing
    /// until somebody dies. Also drives the visibility scan reaction time reads from, on
    /// its own shorter interval — waiting a full second to notice someone came into view
    /// would make the numbers meaningless.
    internal static void Tick() {
        if (Data == null) return;

        if (Time.realtimeSinceStartup - LastVisibilityCheck >= VisibilityIntervalSeconds) {
            LastVisibilityCheck = Time.realtimeSinceStartup;
            VisibilityTracker.Tick(Data.Teams);
        }

        if (Time.realtimeSinceStartup - LastUpdateSent < UpdateIntervalSeconds) return;

        SendUpdateState();
    }

    /// Appends to the rolling kill feed, keeping only what the overlay draws. The killer
    /// is null for a death nobody caused, which the overlay renders without an attacker.
    private static void RecordKill(UnturnedPlayer victim, UnturnedPlayer killer, ELimb limb) {
        RecentKills.Add(new PSUpdateKill {
            Id = ++EventsRecorded,
            Killer = killer?.CharacterName,
            Victim = victim.CharacterName,
            Weapon = killer?.Player.equipment.itemID ?? 0,
            Limb = limb.ToString().ToLowerInvariant()
        });

        if (RecentKills.Count > FeedLength) RecentKills.RemoveAt(0);
    }

    /// Records a result for the overlay to announce. The id increases so the overlay can
    /// tell a new result from the same one being repeated by every later tick.
    private static void RecordEvent(string kind, string teamName) {
        LatestEvent = new PSUpdateEvent {
            Id = ++EventsRecorded,
            Kind = kind,
            Team = teamName,
            Round = StatService.GetTotalPlayedRounds()
        };
    }

    private static void SendUpdateState() {
        if (Data?.Teams == null) {
            Logger.LogError("SendUpdateState() has no match data to report!");
            return;
        }

        LastUpdateSent = Time.realtimeSinceStartup;
        RemoteAPI.NotifyServerUpdate(LiveState.Build(Data, LatestEvent, RecentKills));
    }

    public static bool IsSpectator(ulong steamID) {
        if (Data == null) {
            Logger.LogError("IsSpectator() Data is null!");
            return false;
        }
        if (Data.Spectators == null) {
            Logger.LogError("IsSpectator() Data.Spectators is null!");
            return false;
        }
        return Data.Spectators.Contains(steamID);
    }

    public static bool IsSpectator(CSteamID steamID) {
        if (Data == null) {
            Logger.LogError("IsSpectator() Data is null!");
            return false;
        }
        if (Data.Spectators == null) {
            Logger.LogError("IsSpectator() Data.Spectators is null!");
            return false;
        }
        return Data.Spectators.Contains(steamID.m_SteamID);
    }

    public static bool IsSpectator(Player player) {
        if (Data == null) {
            Logger.LogError("IsSpectator() Data is null!");
            return false;
        }
        if (Data.Spectators == null) {
            Logger.LogError("IsSpectator() Data.Spectators is null!");
            return false;
        }
        return player != null && Data.Spectators.Contains(player.channel.owner.playerID.steamID.m_SteamID);
    }

    public static bool IsSpectator(SteamPlayer player) {
        if (Data == null) {
            Logger.LogError("IsSpectator() Data is null!");
            return false;
        }
        if (Data.Spectators == null) {
            Logger.LogError("IsSpectator() Data.Spectators is null!");
            return false;
        }
        return player != null && Data.Spectators.Contains(player.playerID.steamID.m_SteamID);
    }
}
