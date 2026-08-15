using System.Collections.Generic;
using PeekSeek.Core;
using Rocket.API;
using Rocket.Unturned.Player;
using SDG.Unturned;

namespace PeekSeek.Commands;

/// /ping (alias /ms) with no arguments shows the caller's own connection ping;
/// /ping <player> shows anyone else currently connected.
///
/// The same command the warm-up lobby has, because ping is the one number that decides
/// how much damage a shot does here: PSManager scales it, and a player who wants to know
/// why should not have to leave the match to find out.
internal class PingCommand : IRocketCommand {
    public AllowedCaller AllowedCaller => AllowedCaller.Player;
    public string Name => "ping";
    public string Help => "Shows your connection ping, or another player's.";
    public string Syntax => "[player]";
    public List<string> Aliases => ["ms"];
    public List<string> Permissions => [];

    public void Execute(IRocketPlayer caller, string[] command) {
        UnturnedPlayer callerPlayer = (UnturnedPlayer)caller;
        SteamPlayer self = callerPlayer.Player.channel.owner;
        SteamPlayer target = command.Length == 0 ? self : Resolve(command[0]);

        if (target == null) {
            PSManager.Whisper(self, $"No player found matching \"{command[0]}\".");
            return;
        }

        string label = target.playerID.steamID == callerPlayer.CSteamID ? "Your" : $"{target.playerID.characterName}'s";
        PSManager.Whisper(self, $"{label} ping: {Ping.Milliseconds(target)}ms");
    }

    /// Connected players only, unlike /stats: a ping is a live connection, so there is
    /// nothing to report for someone who has left.
    private static SteamPlayer Resolve(string query) {
        UnturnedPlayer byName = UnturnedPlayer.FromName(query);
        if (byName != null) return byName.Player.channel.owner;

        if (!ulong.TryParse(query, out ulong steam64)) return null;
        foreach (SteamPlayer client in Provider.clients) {
            if (client?.playerID.steamID.m_SteamID == steam64) return client;
        }
        return null;
    }
}
