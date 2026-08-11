using System.Collections.Generic;
using PeekSeek.Core;
using Rocket.API;
using Rocket.Unturned.Player;
using Steamworks;

namespace PeekSeek.Commands;

/// /stats with no arguments shows the caller's own stats for the match in progress.
/// /stats <name or steam64> shows anyone else's — StatService keeps every player who has
/// appeared in this match, not just who is currently online, so a target can be looked up
/// by steam64 even after they disconnect.
internal class StatsCommand : IRocketCommand {
    public AllowedCaller AllowedCaller => AllowedCaller.Player;
    public string Name => "stats";
    public string Help => "Shows your stats for the current match, or another player's.";
    public string Syntax => "[player]";
    public List<string> Aliases => [];
    public List<string> Permissions => [];

    public void Execute(IRocketPlayer caller, string[] command) {
        UnturnedPlayer callerPlayer = (UnturnedPlayer)caller;
        CSteamID? target = command.Length == 0 ? callerPlayer.CSteamID : Resolve(command[0]);

        if (target == null) {
            Reply(callerPlayer, $"No player found matching \"{command[0]}\".");
            return;
        }

        if (!StatService.HasStats(target.Value)) {
            Reply(callerPlayer, target == callerPlayer.CSteamID
                ? "You have no stats recorded for this match yet."
                : "That player has no stats recorded for this match.");
            return;
        }

        Reply(callerPlayer, Format(target.Value));
    }

    /// Tries a connected player's name first, then falls back to a raw steam64 so
    /// disconnected players who already have stats can still be looked up.
    private static CSteamID? Resolve(string query) {
        UnturnedPlayer byName = UnturnedPlayer.FromName(query);
        if (byName != null) return byName.CSteamID;

        return ulong.TryParse(query, out ulong steam64) && StatService.HasStats(new CSteamID(steam64))
            ? new CSteamID(steam64)
            : null;
    }

    private static string Format(CSteamID target) {
        string name = UnturnedPlayer.FromCSteamID(target)?.CharacterName ?? target.ToString();
        return FormatStats(
            name,
            StatService.GetStatKills(target),
            StatService.GetStatDeaths(target),
            StatService.GetStatHeadshots(target),
            StatService.GetStatWins(target)
        );
    }

    /// Pure formatting, kept separate from CSteamID/UnturnedPlayer lookups so it can run
    /// without a live Unturned host in a unit test.
    internal static string FormatStats(string name, byte kills, byte deaths, byte headshots, byte wins) {
        int hsPercent = kills > 0 ? headshots * 100 / kills : 0;
        return $"{name} — {kills}K {deaths}D ({hsPercent}% HS) — {wins} round(s) won this match";
    }

    private static void Reply(UnturnedPlayer to, string message) {
        PSManager.Whisper(to.Player.channel.owner, message);
    }
}
