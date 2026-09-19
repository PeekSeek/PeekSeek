using System;
using System.Collections.Generic;
using PeekSeekSiege.Core;
using Rocket.API;
using Rocket.Unturned.Player;

namespace PeekSeekSiege.Commands;

/// `/pick 3`, the chat answer to the option board, and `/pick spawn 2`, an attacker's
/// answer to the map.
///
/// This exists whether or not the Unity board does. A player still downloading the UI
/// bundle, or one whose client is not drawing effects for any reason, still has to be
/// able to answer, and a round that waited for them would be a round nobody plays.
public class PickCommand : IRocketCommand {
    public AllowedCaller AllowedCaller => AllowedCaller.Player;
    public string Name => "pick";
    public string Help => "Take one of the five options on the board, or a spawn on the map.";
    public string Syntax => "<1-5> | spawn <1-3>";
    public List<string> Aliases => [];
    public List<string> Permissions => [];

    public void Execute(IRocketPlayer caller, string[] command) {
        UnturnedPlayer player = (UnturnedPlayer)caller;
        Siege siege = SiegeHost.Current;

        if (siege == null || !siege.Board.IsOpen) {
            Chat.Whisper(player.Player.channel.owner, PeekSeekSiege.Instance.Translate("pick_closed"));
            return;
        }

        bool spawn = command.Length > 1 && command[0].Equals("spawn", StringComparison.OrdinalIgnoreCase);
        string number = command.Length == 0 ? "" : spawn ? command[1] : command[0];
        if (!int.TryParse(number, out int chosen)) {
            Chat.Whisper(player.Player.channel.owner, PeekSeekSiege.Instance.Translate("pick_how"));
            return;
        }

        bool taken = spawn
            ? siege.Board.AnswerSpawn(player.Player.channel.owner, chosen)
            : siege.Board.Answer(player.Player.channel.owner, chosen) != null;
        if (!taken) {
            Chat.Whisper(player.Player.channel.owner, PeekSeekSiege.Instance.Translate("pick_no_such"));
        }
    }
}
