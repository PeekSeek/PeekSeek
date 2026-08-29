using System.Collections.Generic;
using PeekSeekLobby.Core;
using Rocket.API;
using Rocket.Unturned.Player;
using SDG.Unturned;
using Steamworks;

namespace PeekSeekLobby.Commands;

/// /report <player> <reason> sends staff the reported player's screen, their numbers
/// for this round, and a way into the server.
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

        if (command.Length < 2) {
            LobbyManager.Whisper(reporter.Player.channel.owner,
                PeekSeekLobby.Instance.Translate("report_usage"));
            return;
        }

        if (!PlayerTool.tryGetSteamPlayer(command[0], out SteamPlayer target) || target.player == null) {
            LobbyManager.Whisper(reporter.Player.channel.owner,
                PeekSeekLobby.Instance.Translate("report_no_player", command[0]));
            return;
        }

        if (target.playerID.steamID == reporter.CSteamID) {
            LobbyManager.Whisper(reporter.Player.channel.owner,
                PeekSeekLobby.Instance.Translate("report_self"));
            return;
        }

        string reason = string.Join(" ", command, 1, command.Length - 1);
        LobbyManager.Report(reporter.CSteamID, target, reason);

        // Answered immediately rather than when the upload lands. The screenshot is
        // requested from the reported player's client and can take seconds or never
        // arrive at all, and a reporter left staring at nothing types it again.
        LobbyManager.Whisper(reporter.Player.channel.owner,
            PeekSeekLobby.Instance.Translate("report_sent", target.playerID.characterName));
    }
}
