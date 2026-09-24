using System.Collections.Generic;
using PeekSeekLobby.Core;
using Rocket.API;
using Rocket.Unturned.Player;

namespace PeekSeekLobby.Commands;

/// /kit with no arguments lists the categories AllowedKitCategories currently
/// allows and which one the caller has equipped; /kit <category> switches to it
/// and re-equips immediately.
internal class KitCommand : IRocketCommand {
    public AllowedCaller AllowedCaller => AllowedCaller.Player;
    public string Name => "kit";
    public string Help => "Switches which of your kits you're equipped with.";
    public string Syntax => "[meta|sniper]";
    public List<string> Aliases => [];
    public List<string> Permissions => [];

    public void Execute(IRocketPlayer caller, string[] command) {
        UnturnedPlayer player = (UnturnedPlayer)caller;

        List<string> allowed = PeekSeekLobby.Config.AllowedKitCategories;

        if (command.Length == 0) {
            LobbyManager.Whisper(player.Player.channel.owner,
                PeekSeekLobby.Instance.Translate("kit_options", string.Join(", ", allowed)));
            return;
        }

        string category = command[0].ToLowerInvariant();
        if (!LobbyManager.SwitchKit(player.CSteamID, category)) {
            LobbyManager.Whisper(player.Player.channel.owner,
                PeekSeekLobby.Instance.Translate("kit_invalid", category, string.Join(", ", allowed)));
            return;
        }

        LobbyManager.Whisper(player.Player.channel.owner,
            PeekSeekLobby.Instance.Translate("kit_switched", category));
    }
}
