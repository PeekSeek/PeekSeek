using System.Collections.Generic;
using PeekSeekLobby.Core;
using Rocket.API;
using Rocket.Unturned.Player;

namespace PeekSeekLobby.Commands;

/// Toggles whether the caller is included in the matchmaking heartbeat this
/// lobby sends on their behalf — running it again opts back in, since there's
/// no other way back into queue short of reconnecting.
internal class NoQueueCommand : IRocketCommand
{
    public AllowedCaller AllowedCaller => AllowedCaller.Player;
    public string Name => "noqueue";
    public string Help => "Toggles whether you're in the matchmaking queue while you're in the lobby.";
    public string Syntax => "";
    public List<string> Aliases => ["noq"];
    public List<string> Permissions => [];

    public void Execute(IRocketPlayer caller, string[] command)
    {
        UnturnedPlayer player = (UnturnedPlayer)caller;
        bool nowQueueing = LobbyManager.ToggleQueueing(player.CSteamID);
        LobbyManager.Whisper(player.Player.channel.owner,
            PeekSeekLobby.Instance.Translate(nowQueueing ? "queued" : "not_queued"));
    }
}
