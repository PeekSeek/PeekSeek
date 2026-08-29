using System.Collections.Generic;
using PeekSeek.Core;
using PeekSeek.Core.Web;
using Rocket.API;
using Rocket.Unturned.Player;
using SDG.Unturned;
using Steamworks;

namespace PeekSeek.Commands;

/// /report <player> <reason> sends staff the reported player's screen, their numbers
/// for this match, and a way into the server.
///
/// The screenshot is the reason this is a command and not a Discord form: it comes from
/// asking the reported player's own client, which only the server they are connected to
/// can do. By the time a moderator reads the report the round is usually over, so the
/// evidence has to be captured at the moment somebody complains.
internal class ReportCommand : IRocketCommand {
    public AllowedCaller AllowedCaller => AllowedCaller.Player;
    public string Name => "report";
    public string Help => "Reports a player to staff, with a screenshot of their game.";
    public string Syntax => "<player> <reason>";
    public List<string> Aliases => [];
    public List<string> Permissions => [];

    public void Execute(IRocketPlayer caller, string[] command) {
        UnturnedPlayer reporter = (UnturnedPlayer)caller;

        // RocketMod splits arguments on spaces, so the reason is everything after the
        // name rather than the one word following it. Making somebody quote a sentence
        // to file a report is how reports stop being filed.
        if (command.Length < 2) {
            PSManager.Whisper(reporter.Player.channel.owner,
                "<color=white>/report <player> <reason></color> sends staff their screen and this match's stats.");
            return;
        }

        if (!PlayerTool.tryGetSteamPlayer(command[0], out SteamPlayer target) || target.player == null) {
            PSManager.Whisper(reporter.Player.channel.owner,
                $"Nobody here called <color=white>{command[0]}</color>.");
            return;
        }

        if (target.playerID.steamID == reporter.CSteamID) {
            PSManager.Whisper(reporter.Player.channel.owner, "You cannot report yourself.");
            return;
        }

        string reason = string.Join(" ", command, 1, command.Length - 1);
        PSManager.Report(reporter.CSteamID, target, reason);

        // Answered immediately rather than when the upload lands. The screenshot is
        // requested from the reported player's client and can take seconds or never
        // arrive at all, and a reporter left staring at nothing types it again.
        PSManager.Whisper(reporter.Player.channel.owner,
            $"Reported <color=white>{target.playerID.characterName}</color>. Staff have been sent their screen and this match's stats.");
    }
}
