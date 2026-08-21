using System.Collections.Generic;
using PeekSeekLobby.Core;
using Rocket.API;
using Rocket.Unturned.Player;

namespace PeekSeekLobby.Commands;

/// /powerup answers the powerup half of the loadout prompt a matched player gets.
///
/// The kit half is answered with /kit, which is the word people already type in the
/// arena. Both ends up in Answer below, so the two commands cannot drift apart in what
/// they accept or in what they say when they refuse.
internal class PickCommand : IRocketCommand {
    public AllowedCaller AllowedCaller => AllowedCaller.Player;
    public string Name => "powerup";
    public string Help => "Picks the powerup you take into the match you have been given.";
    public string Syntax => "<number|name>";
    public List<string> Aliases => ["pu", "pw"];
    public List<string> Permissions => [];

    public void Execute(IRocketPlayer caller, string[] command) {
        Answer((UnturnedPlayer)caller, powerup: true, command);
    }

    /// One answer to the loadout prompt, by number or by name.
    ///
    /// Listing the options again on a miss rather than only saying no is the point: the
    /// prompt scrolls, and somebody typing the wrong thing is somebody who wants to
    /// choose and cannot see what they are choosing between any more.
    internal static void Answer(UnturnedPlayer player, bool powerup, string[] command) {
        List<string> options = LobbyManager.MatchOptions(player.CSteamID, powerup);
        if (options == null || options.Count == 0) {
            LobbyManager.Whisper(player.Player.channel.owner,
                PeekSeekLobby.Instance.Translate("pick_nothing_to_pick"));
            return;
        }

        string choice = command.Length == 0 ? null : command[0];
        if (!LobbyManager.ChooseForMatch(player.CSteamID, powerup, choice)) {
            LobbyManager.Whisper(player.Player.channel.owner,
                PeekSeekLobby.Instance.Translate("pick_unknown", Numbered(options)));
        }
    }

    private static string Numbered(List<string> options) {
        string[] parts = new string[options.Count];
        for (int i = 0; i < options.Count; i++) {
            parts[i] = $"{i + 1} {options[i]}";
        }
        return string.Join(", ", parts);
    }
}
