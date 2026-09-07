using System.Collections.Generic;
using PeekSeekSiege.Core;
using Rocket.API;
using Rocket.Unturned.Player;

namespace PeekSeekSiege.Commands;

/// `/start` and `/start solo`, the only way a Siege match begins today.
///
/// There is no matchmaking on a Siege server yet, and no control plugin to hand it a
/// match, so somebody has to say go. Solo is the important half: it runs the same
/// gamemode with one person, building both sites in turn, so the whole loop can be
/// tested without finding a second player for a five-minute build phase.
public class StartCommand : IRocketCommand {
    public AllowedCaller AllowedCaller => AllowedCaller.Both;
    public string Name => "start";
    public string Help => "Start a Siege match. Add 'solo' to play both sides yourself.";
    public string Syntax => "[solo]";
    public List<string> Aliases => ["siegestart"];
    public List<string> Permissions => ["peekseek.siege.start"];

    public void Execute(IRocketPlayer caller, string[] command) {
        bool solo = command.Length > 0 && command[0].Equals("solo", System.StringComparison.OrdinalIgnoreCase);

        string refusal = SiegeHost.Start(solo);
        if (refusal == null) return;

        Reply(caller, PeekSeekSiege.Instance.Translate("start_" + refusal));
    }

    internal static void Reply(IRocketPlayer caller, string message) {
        if (caller is UnturnedPlayer player) {
            Chat.Whisper(player.Player.channel.owner, message);
            return;
        }

        Rocket.Core.Logging.Logger.Log(message);
    }
}

/// `/stop`, for when a test run has told you what you needed and the five-minute build
/// phase has not finished being five minutes.
public class StopCommand : IRocketCommand {
    public AllowedCaller AllowedCaller => AllowedCaller.Both;
    public string Name => "stop";
    public string Help => "End the running Siege match.";
    public string Syntax => "";
    public List<string> Aliases => ["siegestop"];
    public List<string> Permissions => ["peekseek.siege.start"];

    public void Execute(IRocketPlayer caller, string[] command) {
        if (SiegeHost.Running == null) {
            StartCommand.Reply(caller, PeekSeekSiege.Instance.Translate("start_nothing_running"));
            return;
        }

        SiegeHost.Stop();
        Chat.Announce(PeekSeekSiege.Instance.Translate("match_stopped"));
    }
}
