using System.Collections.Generic;
using PeekSeekLobby.Core;
using Rocket.API;
using Rocket.Unturned.Player;
using UnityEngine;

namespace PeekSeekLobby.Commands;

/// /afk steps out of the arena without leaving the server, and running it again steps
/// back in. Distinct from /noqueue on purpose: this stops you being spawned, that stops
/// you being matched, and going AFK keeps you in the queue exactly as you were.
internal class AfkCommand : IRocketCommand {
    public AllowedCaller AllowedCaller => AllowedCaller.Player;
    public string Name => "afk";
    public string Help => "Stops you being put back into the arena until you run it again.";
    public string Syntax => "";
    public List<string> Aliases => [];
    public List<string> Permissions => [];

    public void Execute(IRocketPlayer caller, string[] command) {
        UnturnedPlayer player = (UnturnedPlayer)caller;
        bool nowAfk = LobbyManager.ToggleAfk(player.CSteamID);

        LobbyManager.Whisper(player.Player.channel.owner, nowAfk
            ? PeekSeekLobby.Instance.Translate("afk_on")
            : PeekSeekLobby.Instance.Translate("afk_off",
                Mathf.RoundToInt(PeekSeekLobby.Config.RespawnDelaySeconds)));
    }
}
