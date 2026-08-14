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
    public List<HomelessSpawn> Spawns;
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


    public void LoadDefaults() {
        LobbyTimeoutSeconds = 120;
        LobbyTimeoutBroadcastSecondsRemaining = [5, 10, 30, 60, 90];
        DelayKickAfterWin = 10;
        LobbyServerRelaySGSSteamID = 0;
        szHexDefaultChatColor = "#FFFFFF";
        szChatIconURL = "https://domain.org/image.png";
        szAPIURL = "https://api.peekseek.com";

        Spawns = new List<HomelessSpawn> {
            new HomelessSpawn {
                Position = new SerializableVector3(0, 0, 0)
            }
        };

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
    }
}
