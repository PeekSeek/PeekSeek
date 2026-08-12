using System.Collections.Generic;
using System.Xml.Serialization;
using PeekSeekLobby.Core.Models;
using Rocket.API;

namespace PeekSeekLobby;

[XmlRoot("Config")]
public class PeekSeekLobbyConfiguration : IRocketPluginConfiguration {
    public string szAPIURL;
    public string szHexDefaultChatColor;
    public string szChatIconURL;
    public string szRegion;
    public ushort GamemodeBitmask;
    public float HeartbeatIntervalSeconds;
    public float SpawnProtectionSeconds;
    public float RespawnDelaySeconds;
    public List<ushort> RespawnCountdownAtSecondsRemaining;
    public ushort BloodBagItemID;
    public List<LobbySpawn> Spawns;
    public string szDiscordURL;

    /// Which kits.json weapon categories players may equip in the lobby and pick
    /// between with /kit — a subset of what the site actually supports, so this
    /// can grow (or shrink) without a plugin release. The first entry is the
    /// default a player equips on join.
    public List<string> AllowedKitCategories;

    public void LoadDefaults() {
        szAPIURL = "https://api.peekseek.com";
        szHexDefaultChatColor = "#FFFFFF";
        szChatIconURL = "https://domain.org/image.png";
        // Bitmask selecting mmconfig gamemode 0 (Competitive) — matches the site's
        // /queue/heartbeat bit convention, not a literal gamemode array index.
        szRegion = "paris";
        GamemodeBitmask = 1;
        HeartbeatIntervalSeconds = 5f;
        SpawnProtectionSeconds = 3f;
        RespawnDelaySeconds = 10f;
        RespawnCountdownAtSecondsRemaining = [10, 5, 3, 2, 1];
        BloodBagItemID = 395;
        AllowedKitCategories = ["meta", "sniper"];
        szDiscordURL = "https://discord.gg/NKgTe6RnvH";

        // Placeholder — real Alpha Valley spawn points get filled in once the
        // plugin is installed on the live arena map.
        Spawns = new List<LobbySpawn> {
            new LobbySpawn { Position = new SerializableVector3(0, 0, 0) }
        };
    }
}
