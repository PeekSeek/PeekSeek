using System.Collections.Generic;
using PeekSeekLobby.Core;
using Rocket.API;
using Rocket.Unturned.Player;
using SDG.Unturned;
using Ping = PeekSeek.Core.Ping;

namespace PeekSeekLobby.Commands;

/// /ping (alias /ms) with no arguments shows the caller's own connection ping;
/// /ping <player> shows anyone else currently connected.
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
        SteamPlayer target = command.Length == 0 ? self : LobbyManager.FindPlayer(command[0]);

        if (target == null) {
            LobbyManager.Whisper(self, PeekSeekLobby.Instance.Translate("no_player_found", command[0]));
            return;
        }

        int ms = Ping.Milliseconds(target);
        string label = target.playerID.steamID == callerPlayer.CSteamID ? "Your" : $"{target.playerID.characterName}'s";
        LobbyManager.Whisper(self, $"{label} ping: {ms}ms");
    }
}
