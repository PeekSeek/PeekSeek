using System.Collections.Generic;
using PeekSeekLobby.Core;
using Rocket.API;
using Rocket.Unturned.Player;
using Steamworks;

namespace PeekSeekLobby.Commands;

/// /stats with no arguments shows the caller's own kills/deaths for this lobby
/// session; /stats <name or steam64> shows anyone else currently connected.
/// Unlike the main PeekSeek plugin's /stats, this is session-scoped to the
/// lobby (not match-scoped) and only covers players still online — there's no
/// match to persist a disconnected player's numbers against.
internal class StatsCommand : IRocketCommand {
    public AllowedCaller AllowedCaller => AllowedCaller.Player;
    public string Name => "stats";
    public string Help => "Shows your kills/deaths for this lobby session, or another player's.";
    public string Syntax => "[player]";
    public List<string> Aliases => [];
    public List<string> Permissions => [];

    public void Execute(IRocketPlayer caller, string[] command) {
        UnturnedPlayer callerPlayer = (UnturnedPlayer)caller;
        CSteamID? target = command.Length == 0 ? callerPlayer.CSteamID : Resolve(command[0]);

        if (target == null) {
            Reply(callerPlayer, PeekSeekLobby.Instance.Translate("no_player_found", command[0]));
            return;
        }

        if (!LobbyManager.TryGetStats(target.Value, out LobbyManager.PlayerState state)) {
            Reply(callerPlayer, PeekSeekLobby.Instance.Translate("no_stats"));
            return;
        }

        string name = UnturnedPlayer.FromCSteamID(target.Value)?.CharacterName ?? target.ToString();
        Reply(callerPlayer, Format(name, state.Kills, state.Deaths, state.Headshots));
    }

    private static CSteamID? Resolve(string query) {
        UnturnedPlayer byName = UnturnedPlayer.FromName(query);
        if (byName != null) return byName.CSteamID;

        return ulong.TryParse(query, out ulong steam64) ? new CSteamID(steam64) : null;
    }

    /// Pure formatting, kept separate from CSteamID/UnturnedPlayer lookups so it
    /// can run without a live Unturned host in a unit test.
    internal static string Format(string name, byte kills, byte deaths, byte headshots) {
        int hsPercent = kills > 0 ? headshots * 100 / kills : 0;
        return $"{name} — {kills}K {deaths}D ({hsPercent}% HS)";
    }

    private static void Reply(UnturnedPlayer to, string message) {
        LobbyManager.Whisper(to.Player.channel.owner, message);
    }
}
