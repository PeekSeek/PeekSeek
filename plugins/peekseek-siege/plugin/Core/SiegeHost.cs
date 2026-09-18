using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Rocket.Unturned.Events;
using Rocket.Unturned.Player;
using SDG.Unturned;
using Steamworks;
using UnityEngine;
using Logger = Rocket.Core.Logging.Logger;

namespace PeekSeekSiege.Core;

/// What starts a Siege match, and what feeds it.
///
/// This is the part that the main PeekSeek plugin will eventually replace. Everything
/// below the IGamemode interface belongs to the gamemode; everything here is the host
/// that decides a match exists, works out the teams, and forwards Unturned's events to
/// whatever mode is running. Today that host is a `/start` command on a server with no
/// matchmaking on it. Tomorrow it is PSManager handing over a PSMatch it got from the
/// site, and this file stops being needed without a line of Siege changing.
internal static class SiegeHost {
    private static IGamemode running;
    private static Harmony instrument;

    internal static IGamemode Running => running;
    internal static Siege Current => running as Siege;

    internal static void Init() {
        instrument = new Harmony("blaze.ps.siege");
        instrument.PatchAll(Assembly.GetExecutingAssembly());

        Provider.onServerConnected += OnConnected;
        Provider.onServerDisconnected += OnDisconnected;
        UnturnedPlayerEvents.OnPlayerDeath += OnDeath;
        UnturnedPlayerEvents.OnPlayerRevive += OnRevive;
        EffectManager.onEffectButtonClicked += OnButtonClicked;

        RemoteAPI.Init();
    }

    internal static void Shutdown() {
        running?.End();
        running = null;

        Provider.onServerConnected -= OnConnected;
        Provider.onServerDisconnected -= OnDisconnected;
        UnturnedPlayerEvents.OnPlayerDeath -= OnDeath;
        UnturnedPlayerEvents.OnPlayerRevive -= OnRevive;
        EffectManager.onEffectButtonClicked -= OnButtonClicked;

        RemoteAPI.Shutdown();
        instrument?.UnpatchAll(instrument.Id);
    }

    internal static void Tick() {
        running?.Tick();
    }

    /// Start a match from whoever is on the server right now.
    ///
    /// `solo` is how one person tests the whole loop: the two teams are still made and
    /// the two sites are still built, but the tester is put on both in turn rather than
    /// waiting for an opponent who is not coming. Returns the reason it could not
    /// start, or null when it did.
    internal static string Start(bool solo) {
        if (running != null) return "already_running";
        if (PeekSeekSiege.Config.Sites.Count < 2) return "no_sites";

        List<SteamPlayer> present = Provider.clients.Where(c => c.player != null).ToList();
        if (present.Count == 0) return "nobody_here";
        if (!solo && present.Count < 2) return "need_two";

        GamemodeMatch match = new() { Id = 0, Solo = solo };
        GroupManager.knownGroups.Clear();

        for (int team = 0; team < 2; team++) {
            string name = PeekSeekSiege.Config.Sites[team].Name;
            GroupInfo group = GroupManager.addGroup(GroupManager.generateUniqueGroupID(), name);

            match.Teams.Add(new GamemodeTeam {
                Name = name,
                GroupId = group?.groupID.m_SteamID ?? 0
            });
        }

        // Solo puts the tester on team 0 and leaves team 1 empty. Siege reads the side
        // they are playing from the round order rather than from the roster, so an
        // empty team is exactly what it expects.
        for (int i = 0; i < present.Count; i++) {
            int team = solo ? 0 : i % 2;
            ulong steam64 = present[i].playerID.steamID.m_SteamID;

            match.Teams[team].Players.Add(steam64);
            AssignGroup(present[i].player, match.Teams[team].GroupId);
            RemoteAPI.Fetch(steam64);
        }

        running = new Siege();
        running.Begin(match);

        Logger.Log($"Siege started{(solo ? " in solo" : "")} with {present.Count} player(s).");
        return null;
    }

    internal static void Stop() {
        running?.End();
        running = null;
    }

    private static void AssignGroup(Player player, ulong groupId) {
        player.quests.leaveGroup(true);
        GroupManager.alertGroupmatesLeft(player);
        player.quests.ServerAssignToGroup(new CSteamID(groupId), EPlayerGroupRank.MEMBER, true);
    }

    private static void OnConnected(CSteamID steamID) {
        RemoteAPI.Fetch(steamID.m_SteamID);

        Player player = PlayerTool.getPlayer(steamID);
        if (player == null) return;

        if (running == null) {
            Chat.Whisper(player.channel.owner, PeekSeekSiege.Instance.Translate("idle_welcome"));
            return;
        }

        Seat(player);
        running.OnPlayerJoined(player);
    }

    /// Somebody arriving mid-match joins the side with fewer people. Without a seat they
    /// would be on nobody's roster: placed as if on team 0, counted by nobody, and
    /// nowhere on the HUD. In solo there is one side, and it is the tester's.
    private static void Seat(Player player) {
        GamemodeMatch match = Current?.Match;
        ulong steam64 = player.channel.owner.playerID.steamID.m_SteamID;
        if (match == null || match.TeamOf(steam64) != null) return;

        GamemodeTeam team = match.Solo
            ? match.Teams[0]
            : match.Teams.OrderBy(t => t.Players.Count).First();

        team.Players.Add(steam64);
        AssignGroup(player, team.GroupId);
        Logger.Log($"{player.channel.owner.playerID.characterName} joined {team.Name} mid-match.");
    }

    private static void OnDisconnected(CSteamID steamID) {
        RemoteAPI.Forget(steamID.m_SteamID);
        running?.OnPlayerLeft(steamID);
    }

    private static void OnDeath(UnturnedPlayer player, EDeathCause cause, ELimb limb, CSteamID murderer) {
        running?.OnPlayerDied(player.Player, murderer);
    }

    private static void OnRevive(UnturnedPlayer player, Vector3 position, byte angle) {
        running?.OnPlayerRevived(player.Player);
    }

    /// A click on a tile of the Unity board. Chat picks come in through /pick instead
    /// and land on the same method, so the two forms cannot drift apart.
    private static void OnButtonClicked(Player player, string buttonName) {
        Current?.Board.AnswerButton(player.channel.owner, buttonName);
    }
}
