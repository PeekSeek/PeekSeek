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

    /// The match being played, once the site has handed it over. Null while this server
    /// is free or still waiting on its players to finish choosing.
    internal static PSMatch Data;

    /// The match this server has told the site it is responsible for. Set the moment a
    /// match is assigned — before its data arrives — and only cleared once the site has
    /// acknowledged the result, so a match is never treated as abandoned while this
    /// server still has something to say about it.
    internal static int HeldMatchId { get; private set; }

    internal static float MatchReady;
    private static List<GroupInfoEx> GroupInfos;

    private const float UpdateIntervalSeconds = 1f;
    private static float LastUpdateSent;

    private const float VisibilityIntervalSeconds = 0.15f;
    private static float LastVisibilityCheck;

    /// Separate from UpdateIntervalSeconds: this is a console log for debugging alive/dead
    /// state solo, not the payload sent to the overlay, so it can run on its own slower
    /// cadence without touching what the overlay actually receives.
    private const float DebugStateLogIntervalSeconds = 10f;
    private static float LastDebugStateLogged;

    private static PSUpdateEvent LatestEvent;
    private static int EventsRecorded;

    private const int FeedLength = 5;
    private static readonly List<PSUpdateKill> RecentKills = [];

    /// Separate from EventsRecorded: this ids the durable kill log sent to the site,
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

        ConfigurationManager.DisablePassiveHealthRegen();
    }

    private static void OnDamageRequested(ref DamagePlayerParameters parameters, ref bool shouldAllow) {
        Player attacker = PlayerTool.getPlayer(parameters.killer);

        if (attacker != null && parameters.damage > 5) {
            float ping = attacker.channel.owner.ping * 1000f;

            foreach (var config in PeekSeek.Config.PingConfig.OrderByDescending(c => c.PingOrHigher)) {
                if (!(ping >= config.PingOrHigher)) continue;
                parameters.damage *= (config.Precentage / 100f);
                TellAboutPingReduction(attacker, config.Precentage, ping);
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
            StatService.RecordHit(parameters.killer, parameters.limb == ELimb.SKULL);
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

    private const float PingNoticeIntervalSeconds = 20f;
    private static readonly Dictionary<ulong, float> LastPingNotice = [];

    /// Ping scaling fires once per bullet and is otherwise completely invisible: the
    /// shooter sees their hits land for less than they should and concludes the server
    /// is broken. Throttled per shooter for the same reason it is worth saying at all —
    /// once is information, every bullet is noise.
    private static void TellAboutPingReduction(Player attacker, int percentage, float ping) {
        ulong steam64 = attacker.channel.owner.playerID.steamID.m_SteamID;
        float now = Time.realtimeSinceStartup;

        if (LastPingNotice.TryGetValue(steam64, out float last)
                && now - last < PingNoticeIntervalSeconds) return;
        LastPingNotice[steam64] = now;

        Whisper(attacker.channel.owner,
            PeekSeek.Instance.Translate("damage_reduced", percentage, Mathf.RoundToInt(ping)));
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
        RemoteAPI.StartSession();
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

    /// Acts on one answer from the session poll. Every state this server can be in is
    /// reached from here, and nowhere else.
    internal static void Apply(PSSessionAnswer answer) {
        if (answer == null) return;

        switch (answer.Action) {
            case PSSessionAnswer.Wait:
                HoldForMatch(answer);
                break;
            case PSSessionAnswer.Host:
                StartMatch(answer.Match);
                break;
            case PSSessionAnswer.Abort:
                AbortMatch(answer.Reason);
                break;
        }
    }

    /// This server has been given a match whose players are still choosing their
    /// loadouts. Nothing to run yet — just hold it, and refuse anyone who tries to join
    /// early rather than letting them in to a match that has not been set up.
    private static void HoldForMatch(PSSessionAnswer answer) {
        if (HeldMatchId == answer.MatchId && Status == EPSStatus.WAITING) return;

        HeldMatchId = answer.MatchId;
        UpdateStatus(EPSStatus.WAITING);
        Logger.Log($"Holding match {answer.MatchId} — waiting on {answer.WaitingOn.Count} player(s) to finish choosing.");
    }

    private static void StartMatch(PSMatch match) {
        if (match == null) {
            Logger.LogError("Told to host a match with no match data!");
            return;
        }
        // The site only ever hands a match over once, so reaching here twice would mean
        // a duplicate answer — restarting the round would kick everyone already playing.
        if (Status == EPSStatus.IN_PROGRESS && Data?.Id == match.Id) return;

        HeldMatchId = match.Id;
        UpdateStatus(EPSStatus.IN_PROGRESS);

        ConfigurationManager.SetBulletdrop(match.Settings.Ballistics);
        ConfigurationManager.SetOldMovement(match.Settings.Movement);

        MatchReady = Time.realtimeSinceStartup;
        KickAllClients("This server is starting a new match.");

        GroupInfos = CreateGroups(match.Teams);
        Data = match;

        Logger.Log($"Hosting match {match.Id} ({match.Gamemode?.Name}) on {match.Settings.Map} — "
            + $"first to {match.RoundsToWin} rounds.");
        SendUpdateState();
    }

    /// The site says this match is no longer a match. Nobody is coming, so everything
    /// about it is dropped and this server goes back to asking for work.
    private static void AbortMatch(string reason) {
        Logger.LogWarning($"Match {HeldMatchId} aborted by the site ({reason ?? "no reason given"}).");
        KickAllClients("This match was cancelled. Head back to the lobby.");
        ResetMatchState();
        RelayAllClients();
        HeldMatchId = 0;
        UpdateStatus(EPSStatus.READY);
    }

    /// The site has the result, so this server owes it nothing more and is free again.
    internal static void OnResultAcknowledged(int matchId) {
        if (HeldMatchId == matchId) HeldMatchId = 0;
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

            Whisper(player.channel.owner, PeekSeek.Instance.Translate("welcome_spectator"));
            SendWelcome(player.channel.owner);
            return;
        }

        if (player.quests.groupID != GetPlayerTeamGroupID(player)) AssignPlayerGroup(player);
        AssignPlayerKit(player, Data.KitFor(steamID.m_SteamID));

        SendWelcome(player.channel.owner);
    }

    /// Two lines: what this match is, then what is happening right now. Someone who
    /// reads only the first still knows the mode, the map and how long it runs for.
    private static void SendWelcome(SteamPlayer to) {
        Whisper(to, PeekSeek.Instance.Translate("welcome_match",
            Data.Gamemode?.Name ?? "PeekSeek",
            Data.Settings.Map,
            Data.RoundsToWin,
            Data.Settings.Ballistics ? "on" : "off",
            Data.Settings.Movement ? "old" : "new"));

        if (HaveAllPlayersJoined()) return;
        Whisper(to, PeekSeek.Instance.Translate("welcome_waiting"));
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

        if (Data.HasPlayer(ulSteamID) || IsSpectator(ulSteamID)) return;
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
            x => x.Players.Contains(steamID)
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
        return Data.Teams.SelectMany(team => team.Players).All(IsOnline);
    }

    internal static bool HasAnyPlayerJoined() {
        return Data.Teams.SelectMany(team => team.Players).Any(IsOnline);
    }

    /// The single team that still has players on the server, or null when it is not exactly one.
    internal static PSTeam OnlyTeamOnline() {
        List<PSTeam> online = Data.Teams.Where(team => team.Players.Any(IsOnline)).ToList();
        return online.Count == 1 ? online[0] : null;
    }

    private static void UpdateStatus(EPSStatus status) {
        Status = status;
        //RemoteAPI.NotifyServerStatus(status);
    }

    internal static void NotifyTeamWon(ulong playerID) {
        PSTeam team = GetPlayerTeam(playerID);

        foreach (ulong user in team.Players) {
            StatService.UpdateStatWins(new(user));
        }

        AnnounceAceIfAny(team);
        WhisperCloseCalls();
        WhisperRoundDamage();

        if (StatService.GetStatWins(new(playerID)) < Data.RoundsToWin) {
            Announce(PeekSeek.Instance.Translate(
                "round_won", team.Name, StatService.GetTotalPlayedRounds(), ScoreLine()));
            RecordEvent("roundWon", team.Name);
            SendUpdateState();
            RoundLog.Clear();
            return;
        }

        UpdateStatus(EPSStatus.DELAY);
        Announce(PeekSeek.Instance.Translate("match_won", team.Name, ScoreLine()));
        AnnounceMvp();
        RecordEvent("matchWon", team.Name);
        SendUpdateState();
        RoundLog.Clear();
        Task.Run(() => DelayEnd(PeekSeek.Config.DelayKickAfterWin, team.Name));
    }

    /// The score, in team order, as "2-1" — or with names when there are more than two
    /// teams and a bare pair of numbers would not say whose is whose.
    internal static string ScoreLine() {
        if (Data?.Teams == null || Data.Teams.Count == 0) return string.Empty;

        IEnumerable<string> parts = Data.Teams.Select(team =>
            StatService.GetStatWins(new CSteamID(team.Players.First())).ToString());
        if (Data.Teams.Count > 2) {
            parts = Data.Teams.Select(team =>
                $"{team.Name} {StatService.GetStatWins(new CSteamID(team.Players.First()))}");
        }
        return string.Join(Data.Teams.Count > 2 ? " · " : "-", parts);
    }

    /// Called as the round spawns everyone in, so the first thing a player sees on
    /// landing is which round this is and where the match stands.
    internal static void AnnounceRoundStart() {
        Announce(PeekSeek.Instance.Translate(
            "round_start", StatService.GetTotalPlayedRounds() + 1, ScoreLine()));
    }

    /// A solo ace only means something in a team format — a 1v1 win is just a win — and
    /// only generalises cleanly to two teams, so FFA/multi-team matches skip it rather
    /// than guessing at what "the opposing team" means there.
    private static void AnnounceAceIfAny(PSTeam winningTeam) {
        if (Data.Teams.Count != 2) return;

        PSTeam losingTeam = Data.Teams.First(t => t.Name != winningTeam.Name);
        if (losingTeam.Players.Count < 2) return;

        foreach (ulong user in winningTeam.Players) {
            if (RoundLog.KillsBy(new CSteamID(user)) < losingTeam.Players.Count) continue;

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
            foreach (ulong user in team.Players) {
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
            foreach (ulong user in team.Players) {
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
        for (byte round = 0; round < Data.RoundsToWin; round++) {
            foreach (ulong user in team.Players) {
                StatService.UpdateStatWins(new(user));
            }
        }

        Announce(PeekSeek.Instance.Translate("match_forfeit", team.Name));
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
        Announce(PeekSeek.Instance.Translate("match_over"));
        ResetMatchState();
        RelayAllClients();
        UpdateStatus(EPSStatus.READY);
    }

    /// Everything a finished match leaves behind. HeldMatchId deliberately survives:
    /// this server still owes the site a result, and the site keeps the match open for
    /// exactly as long as this server keeps naming it.
    private static void ResetMatchState() {
        StatService.InstanceStats.Clear();
        LatestEvent = null;
        EventsRecorded = 0;
        KillEventsRecorded = 0;
        RecentKills.Clear();
        FightTracker.Clear();
        RoundLog.Clear();
        LastKilledBy.Clear();
        LastPingNotice.Clear();
        VisibilityTracker.Clear();
        GroupManager.knownGroups.Clear();
        Data = null;
    }

    /// Reports every player on the roster, not only the ones who turned up: a player the
    /// server leaves out is a player whose Elo never moves, which is what used to make
    /// walking out of a losing match free.
    internal static void SendResults(string winningTeam, bool isPlayed = true) {
        var payload = new {
            played = isPlayed,
            winningTeam,
            players = isPlayed ? RosterResults(winningTeam) : []
        };

        RemoteAPI.QueueResult(Data.Id, JsonConvert.SerializeObject(payload));
    }

    private static object[] RosterResults(string winningTeam) {
        return Data.Teams
            .SelectMany(team => team.Players.Select(steam64 => new { team, steam64 }))
            .Select(entry => {
                CSteamID steamID = new(entry.steam64);
                return (object)new {
                    steam64 = entry.steam64.ToString(),
                    won = entry.team.Name == winningTeam,
                    kills = StatService.GetStatKills(steamID),
                    deaths = StatService.GetStatDeaths(steamID),
                    headshotKills = StatService.GetStatHeadshots(steamID),
                    hits = StatService.GetStatHits(steamID),
                    headshotHits = StatService.GetStatHeadshotHits(steamID),
                    damageDealt = (int)Math.Round(StatService.GetStatDamageDealt(steamID)),
                    damageTaken = (int)Math.Round(StatService.GetStatDamageTaken(steamID))
                };
            })
            .ToArray();
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
        Logger.Log($"[alive-check] {player.CharacterName} ({player.CSteamID}) died (cause={cause}, limb={limb}) — marked dead this round.");

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

    /// Builds the durable kill record for the site from whatever FightTracker buffered,
    /// separate from RecordKill's ephemeral overlay feed entry above.
    private static void SendKillEvent(UnturnedPlayer victim, UnturnedPlayer killer, EDeathCause cause, ELimb limb, CSteamID murderer) {
        bool ballistics = Data.Settings.Ballistics;

        RemoteAPI.SendKill(Data.Id, new PSKillEvent {
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

        // Respawning does not clear StatService's dead-this-round flag — see LiveState's
        // RoundState comment — so the overlay keeps reporting them dead until the next round.
        Logger.Log($"[alive-check] {player.CharacterName} ({player.CSteamID}) respawned into the lobby — still reported dead until the next round starts.");

        AssignPlayerKit(player.Player, Data.KitFor(player.CSteamID.m_SteamID));

        player.Heal(255);
    }

    /// Safety net: if a death is ever resolved without UnturnedPlayerEvents.OnPlayerDeath
    /// firing, this force-respawns so nobody sits on a death screen — but OnPlayerDead is
    /// also the only place that told StatService someone died. A death only this path
    /// catches would otherwise never be marked, and the overlay would keep reporting that
    /// player as alive for the rest of the round even though they just respawned.
    private static void OnLifeUpdated(Player player) {
        if (!player.life.isDead) return;

        CSteamID steamID = player.channel.owner.playerID.steamID;
        if (StatService.IsAliveThisRound(steamID)) {
            Logger.LogWarning($"[alive-check] {player.channel.owner.playerID.characterName} ({steamID}) was dead without OnPlayerDead firing — marking dead now so the overlay does not show them as alive.");
            StatService.MarkDeadThisRound(steamID);
        }

        player.life.ServerRespawn(false);
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

        if (Time.realtimeSinceStartup - LastDebugStateLogged >= DebugStateLogIntervalSeconds) {
            LastDebugStateLogged = Time.realtimeSinceStartup;
            LogPlayerStates();
        }

        if (Time.realtimeSinceStartup - LastUpdateSent < UpdateIntervalSeconds) return;

        SendUpdateState();
    }

    /// Prints exactly what LiveState.RoundState would tell the overlay for every roster
    /// player, on its own 10s cadence, so alive/dead can be watched from the server console
    /// alone — no browser, no OBS, no waiting for the next kill/round event to see a change.
    private static void LogPlayerStates() {
        foreach (PSTeam team in Data.Teams) {
            foreach (ulong user in team.Players) {
                CSteamID steamID = new(user);
                Player player = PlayerTool.getPlayer(steamID);

                if (player == null) {
                    Logger.Log($"[alive-check] {steamID} => offline");
                    continue;
                }

                string state = LiveState.RoundState(steamID, player);
                string name = player.channel.owner.playerID.characterName;
                Logger.Log($"[alive-check] {name} ({steamID}) => {state} "
                    + $"(deadThisRound={!StatService.IsAliveThisRound(steamID)}, isDead={player.life.isDead}, isSafe={player.movement.isSafe})");
            }
        }
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
        RemoteAPI.SendState(Data.Id, LiveState.Build(Data, LatestEvent, RecentKills));
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
