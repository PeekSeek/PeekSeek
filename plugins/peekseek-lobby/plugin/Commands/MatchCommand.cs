using System.Collections.Generic;
using PeekSeekLobby.Core;
using Rocket.API;
using Rocket.Unturned.Player;

namespace PeekSeekLobby.Commands;

/// /match opens the match page for whoever asks for it.
///
/// On demand rather than on being matched. Being handed a match used to fire a browser
/// prompt at everybody, which covers the screen at the exact moment the loadout prompt
/// is asking for an answer, and which most people dismiss without reading. The choices
/// that page exists for can now be made in chat, so it is left here for the people who
/// want the full screen instead of pushed at the people who do not.
internal class MatchCommand : IRocketCommand {
    public AllowedCaller AllowedCaller => AllowedCaller.Player;
    public string Name => "match";
    public string Help => "Opens your match page on the website.";
    public string Syntax => "";
    public List<string> Aliases => [];
    public List<string> Permissions => [];

    public void Execute(IRocketPlayer caller, string[] command) {
        UnturnedPlayer player = (UnturnedPlayer)caller;
        player.Player.sendBrowserRequest(
            PeekSeekLobby.Instance.Translate("match_found_browser"),
            PeekSeekLobby.Config.szMatchURL);
    }
}
