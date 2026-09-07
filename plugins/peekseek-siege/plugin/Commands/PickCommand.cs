using System.Collections.Generic;
using PeekSeekSiege.Core;
using PeekSeekSiege.Core.Models;
using Rocket.API;
using Rocket.Unturned.Player;

namespace PeekSeekSiege.Commands;

/// `/pick 3`, the chat answer to the option board.
///
/// This exists whether or not the Unity board does. A player still downloading the UI
/// bundle, or one whose client is not drawing effects for any reason, still has to be
/// able to answer, and a round that waited for them would be a round nobody plays.
public class PickCommand : IRocketCommand {
    public AllowedCaller AllowedCaller => AllowedCaller.Player;
    public string Name => "pick";
    public string Help => "Take one of the five options on the board.";
    public string Syntax => "<1-5>";
    public List<string> Aliases => ["p"];
    public List<string> Permissions => [];

    public void Execute(IRocketPlayer caller, string[] command) {
        UnturnedPlayer player = (UnturnedPlayer)caller;
        Siege siege = SiegeHost.Current;

        if (siege == null || !siege.Board.IsOpen) {
            Chat.Whisper(player.Player.channel.owner, PeekSeekSiege.Instance.Translate("pick_closed"));
            return;
        }

        if (command.Length == 0 || !int.TryParse(command[0], out int chosen)) {
            Chat.Whisper(player.Player.channel.owner, PeekSeekSiege.Instance.Translate("pick_how"));
            return;
        }

        SiegeOption taken = siege.Board.Answer(player.Player.channel.owner, chosen);
        if (taken == null) {
            Chat.Whisper(player.Player.channel.owner, PeekSeekSiege.Instance.Translate("pick_no_such"));
        }
    }
}
