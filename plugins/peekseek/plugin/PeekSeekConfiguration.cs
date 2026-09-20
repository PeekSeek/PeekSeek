using System.Collections.Generic;
using System.Xml.Serialization;
using PeekSeek.Core.Models;
using Rocket.API;

namespace PeekSeek;

[XmlRoot("Config")]
public class PeekSeekConfiguration : IRocketPluginConfiguration {
    public ushort LobbyTimeoutSeconds;
    public List<ushort> LobbyTimeoutBroadcastSecondsRemaining;
    public ushort DelayKickAfterWin;
    public ulong LobbyServerRelaySGSSteamID;
    public string szHexDefaultChatColor;
    public string szChatIconURL;
    public string szAPIURL;
    public string szSpectatorGroup;
    public bool UseTestingJSON;
    public List<PingPrecentage> PingConfig;

    /// Whether this server takes itself down once it is safely idle, so it comes back
    /// on the current game build and the current plugins.
    ///
    /// Off by default, and it must stay off until the host is known to bring the
    /// server back up: an in-game shutdown is a process exit, and on FangHosting the
    /// container simply stays stopped afterwards. Turning this on without something
    /// that starts the server again takes it offline permanently after its next
    /// match. See the repo README for the schedule that pairs with it.
    public bool RestartWhenIdle;

    /// How long this server may run before it wants to go down. Finishing a match
    /// also triggers one, so this is really the ceiling for a server nobody has used.
    public int MaxUptimeMinutes;

    /// How long to wait between deciding to restart and actually going, with polling
    /// already stopped. This is the window that catches a poll which was already in
    /// flight when the decision was made and came back holding a match.
    public int DrainSeconds;

    /// The screens, as GUIDs of the effects in the PeekSeek UI workshop item
    /// (`ui/Assets/PeekSeek/Bundles/Effects/*/*.dat`) and the channel each is sent
    /// on. Unturned cannot draw a custom UI from plugin code, only from a bundle the
    /// client already has, so a server without the item logs a line per screen and
    /// draws nothing; the boards then answer themselves. A key is any small number no
    /// other plugin on the server is using, kept stable so a screen can be updated
    /// and cleared after it is sent.
    public string MatchHudEffectGUID;
    public short MatchHudKey;
    public string PickUIEffectGUID;
    public short PickUIKey;
    public string BuildPanelEffectGUID;
    public short BuildPanelKey;

    /// The kill feed, from the PeekSeek Kill Feed workshop item, which is its own item
    /// so the lobby can carry the feed without the rest.
    public string KillFeedEffectGUID;
    public short KillFeedKey;
    public int KillFeedRows;
    public float KillFeedRowSeconds;
    public int KillFeedNameLength;

    /// Where a picture of an item comes from, with `{0}` standing for the item id. The
    /// site serves every item for its loadout editor, so the screens show the same
    /// pictures a player picked their kit from.
    public string ItemIconURL;

    /// The picture on a face for a player the site has no picture for yet: Steam's
    /// own placeholder, the same one the site falls back to.
    public string DefaultAvatarURL;

    public void LoadDefaults() {
        LobbyTimeoutSeconds = 120;
        LobbyTimeoutBroadcastSecondsRemaining = [5, 10, 30, 60, 90];
        DelayKickAfterWin = 10;
        LobbyServerRelaySGSSteamID = 0;
        szHexDefaultChatColor = "#FFFFFF";
        szChatIconURL = "https://domain.org/image.png";
        szAPIURL = "https://api.peekseek.com";

        szSpectatorGroup = "spectator";

        RestartWhenIdle = false;
        MaxUptimeMinutes = 60;
        DrainSeconds = 10;

        PingConfig = new List<PingPrecentage> {
            new PingPrecentage {
                PingOrHigher = 100,
                Precentage = 50
            }
        };

        MatchHudEffectGUID = "5398e1499dcf4fda9e4f8a6c4d73faea";
        MatchHudKey = 27311;
        PickUIEffectGUID = "69fcf241396b44dbb536cf4fc08abbe3";
        PickUIKey = 27310;
        BuildPanelEffectGUID = "7a3d9c2e5f8b4b1c9d0e1f2a3b4c5d6e";
        BuildPanelKey = 27312;

        KillFeedEffectGUID = "c4d5e6f708192a3b4c5d6e7f8091a2b3";
        KillFeedKey = 27320;
        KillFeedRows = 6;
        KillFeedRowSeconds = 7f;
        KillFeedNameLength = 14;

        ItemIconURL = "https://peekseek.net/static/unturned/{0}.png";
        DefaultAvatarURL = "https://avatars.steamstatic.com/fef49e7fa7e1997310d705b2a6158ff8dc1cdfeb_full.jpg";
    }
}
