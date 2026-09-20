using System;
using System.Collections.Generic;
using System.Linq;
using PeekSeek.Core;
using Rocket.API;
using Rocket.Unturned.Player;

namespace PeekSeek.Commands;

/// `/start <gamemode> [solo]`: a match from whoever is on the server, without the site.
///
/// This is how a gamemode is tested. The site makes real matches; this makes one out
/// of the people present, two teams and their saved kits, and runs it through the same
/// plugin the site's matches go through. Nothing about it is reported anywhere. Solo
/// puts everybody on one team, for a mode that can be walked through alone.
public class StartCommand : IRocketCommand {
    public AllowedCaller AllowedCaller => AllowedCaller.Both;
    public string Name => "start";
    public string Help => "Start a local match of a gamemode with everyone here.";
    public string Syntax => "<gamemode> [solo]";
    public List<string> Aliases => [];
    public List<string> Permissions => ["peekseek.start"];

    public void Execute(IRocketPlayer caller, string[] command) {
        if (command.Length == 0) {
            Reply(caller, PeekSeek.Instance.Translate("start_usage", string.Join(", ", Gamemodes.Gamemodes.Names)));
            return;
        }

        bool solo = command.Skip(1).Any(word => word.Equals("solo", StringComparison.OrdinalIgnoreCase));

        string refusal = PSManager.StartLocal(command[0], solo);
        if (refusal == null) return;

        Reply(caller, PeekSeek.Instance.Translate("start_" + refusal, string.Join(", ", Gamemodes.Gamemodes.Names)));
    }

    internal static void Reply(IRocketPlayer caller, string message) {
        if (caller is UnturnedPlayer player) {
            PSManager.Whisper(player.Player.channel.owner, message);
            return;
        }

        Rocket.Core.Logging.Logger.Log(message);
    }
}

/// `/stop`, for when a test has told you what you needed. Ends the running match
/// without a result, local or not.
public class StopCommand : IRocketCommand {
    public AllowedCaller AllowedCaller => AllowedCaller.Both;
    public string Name => "stop";
    public string Help => "End the running match without a result.";
    public string Syntax => "";
    public List<string> Aliases => [];
    public List<string> Permissions => ["peekseek.start"];

    public void Execute(IRocketPlayer caller, string[] command) {
        if (PSManager.Data == null) {
            StartCommand.Reply(caller, PeekSeek.Instance.Translate("start_nothing_running"));
            return;
        }

        PSManager.StopMatch();
    }
}
