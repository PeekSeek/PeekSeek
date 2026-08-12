using System.Collections.Generic;
using PeekSeekLobby.Core;
using Rocket.API;
using Rocket.Unturned.Player;
using UnityEngine;

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
        UnturnedPlayer target = command.Length == 0 ? callerPlayer : UnturnedPlayer.FromName(command[0]);

        if (target == null) {
            LobbyManager.Whisper(callerPlayer.Player.channel.owner,
                PeekSeekLobby.Instance.Translate("no_player_found", command[0]));
            return;
        }

        int ms = Mathf.RoundToInt(target.Player.channel.owner.ping * 1000f);
        string label = target.CSteamID == callerPlayer.CSteamID ? "Your" : $"{target.CharacterName}'s";
        LobbyManager.Whisper(callerPlayer.Player.channel.owner, $"{label} ping: {ms}ms");
    }
}
