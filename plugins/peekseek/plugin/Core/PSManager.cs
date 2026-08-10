using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using HarmonyLib;
using PeekSeek.Core.Models;
using PeekSeek.Core.Models.Enums;
using PeekSeek.Core.Web;
using Rocket.Core;
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
        RemoteAPI.Init();
    }

    private static void OnDamageRequested(ref DamagePlayerParameters parameters, ref bool shouldAllow) {
        Player player = PlayerTool.getPlayer(parameters.killer);

        if (player == null) return;
        float ping = player.channel.owner.ping * 1000f;

        if (!(parameters.damage > 5)) return;

        foreach (var config in PeekSeek.Config.PingConfig.OrderByDescending(c => c.PingOrHigher)) {
            Logger.Log($"Checking {config.PingOrHigher} >= {ping}");
            if (!(ping >= config.PingOrHigher)) continue;
            parameters.damage *= (config.Precentage / 100f);
            break;
        }
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

    internal static bool HaveAllPlayersJoined() {
        var allPlayersJoined = Data.Teams
            .SelectMany(x => x.Users)
            .All(player => Provider.clients.Any(client => client.playerID.steamID.m_SteamID == player));


        return allPlayersJoined;
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

        ChatManager.serverSendMessage(
            PeekSeek.Instance.Translate("win", team.Name),
            UnturnedChat.GetColorFromHex(PeekSeek.Config.szHexDefaultChatColor).Value,
            null,
            null,
            EChatMode.SAY,
            PeekSeek.Config.szChatIconURL,
            true
        );

        if (StatService.GetStatWins(new(playerID)) >= Data.Meta.Rounds) {
            SendResults(team.Name);
            StatService.InstanceStats.Clear();
            KickAllClients($"Match has finished, \"{team.Name}\" has won!");
            UpdateStatus(EPSStatus.READY);
            Data = null;
            GroupManager.knownGroups.Clear();
        } else SendUpdateState();
    }

    internal static void SendResults(string winningTeam) {
        var jsonBuilder = new StringBuilder("{\"status\":\"finished\",\"data\":[");

        bool first = true;
        foreach (var stat in StatService.InstanceStats) {
            if (!first) jsonBuilder.Append(",");
            first = false;

            jsonBuilder.AppendFormat("{{\"steam64\":\"{0}\",\"win\":{1},\"kills\":{2},\"deaths\":{3}}}",
                stat.SteamID, (GetPlayerTeam(stat.SteamID).Name == winningTeam).ToString().ToLower(), stat.Kills, stat.Deaths);
        }

        jsonBuilder.Append("]}");

        RemoteAPI.SendResults(
            jsonBuilder.ToString()
        );
    }

    private static void OnPlayerDead(UnturnedPlayer player, EDeathCause cause, ELimb limb, CSteamID murderer) {
        if (cause == EDeathCause.ARENA) return;

        StatService.UpdateStatDeaths(player.CSteamID);

        if (murderer != CSteamID.Nil && murderer != SteamGameServer.GetSteamID()) {
            UnturnedPlayer unturnedInstigator = UnturnedPlayer.FromCSteamID(murderer);
            StatService.UpdateStatKills(murderer);
            ChatManager.serverSendMessage(
                PeekSeek.Instance.Translate(
                    "kill",
                    player.CharacterName,
                    unturnedInstigator.CharacterName,
                    limb.ToString().ToLower().Replace("_", " ")
                ),
                UnturnedChat.GetColorFromHex(PeekSeek.Config.szHexDefaultChatColor).Value,
                null,
                null,
                EChatMode.SAY,
                PeekSeek.Config.szChatIconURL,
                true
            );
        }

        player.Player.life.ServerRespawn(false);
        SendUpdateState();
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

    private static void SendUpdateState() {
        PSUpdateState state = new();
        byte totalRoundsPlayed = (byte)Data.Teams
                                 .SelectMany(team => team.Users)
                                 .Sum(user => StatService.GetStatWins(new CSteamID(user)));
        if (Data.Teams == null) {
            Logger.LogError("SendUpdateState() Teams is null!");
            return;
        }

        foreach (var _team in Data.Teams) {
            byte wins = StatService.GetStatWins(new(_team.Users.First()));
            state.Teams.Add(new() {
                TeamName = _team.Name,
                Wins = wins,
                Losses = totalRoundsPlayed - wins
            });

            foreach (ulong user in _team.Users) {
                Player player = PlayerTool.getPlayer(new CSteamID(user));
                if (player == null) {
                    state.Teams.Last().Users.Add(user.ToString(), new() {
                        Kills = 0,
                        Deaths = 0,
                        IsAlive = false
                    }); continue;
                }


                state.Teams.Last().Users.Add(user.ToString(), new() {
                    Kills = StatService.GetStatKills(new(user)),
                    Deaths = StatService.GetStatDeaths(new(user)),
                    IsAlive = player != null && !(player.life.isDead || player.movement.isSafe)
                });
            }
        }

        RemoteAPI.NotifyServerUpdate(state);
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
