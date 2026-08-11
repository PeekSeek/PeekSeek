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

        PingConfig = new List<PingPrecentage> {
            new PingPrecentage {
                PingOrHigher = 100,
                Precentage = 50
            }
        };
    }
}
