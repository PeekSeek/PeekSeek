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
    public List<LobbySpawn> Spawns;
    /// Master switch for matchmaking. Off means the lobby is purely a warm-up
    /// server: no heartbeats leave the box, nobody is matched or relayed, and
    /// /noqueue has nothing to toggle.
    public bool EnableQueue;

    /// Consumables restocked to their cap on every kill. GrantOnSpawn decides
    /// which of them players also start a life with. Raise MaxCarried to let
    /// players bank more than one.
    public List<RewardItem> KillRewards;

    /// Everyone fights solo. Steam group membership is applied automatically on
    /// connect and group mates cannot damage each other, so leaving it alone
    /// would quietly make the arena unplayable for anyone sharing a group.
    public bool ForceSoloPlayers;

    /// Sprinting never costs anything here. A warm-up server is about fights,
    /// not about running out of breath crossing the map to reach one.
    public bool UnlimitedStamina;

    /// The arena HUD draws the server name across the top of the screen with the
    /// live player count beneath it, so the round timer is appended to the name
    /// to put it where players are already looking.
    public bool ShowRoundTimerInName;

    /// The name the timer is appended to. Kept separate from the server's real
    /// name because that gets rewritten every second once the timer is on, and
    /// reading the base back out of it would compound.
    public string szServerDisplayName;

    /// Dropped items are deleted the moment they hit the ground, so the arena
    /// never fills with the guns and magazines of everyone who died in it.
    public bool RemoveDroppedItems;

    /// How often the arena is wiped back to a clean slate — world items and
    /// player-placed builds cleared, session stats zeroed, everyone respawned.
    public float ResetIntervalSeconds;

    /// Mirror each player's Elo into their in-game reputation. Unturned moves
    /// reputation on its own for kills and similar, so this is re-asserted on a
    /// timer rather than set once.
    public bool ShowEloAsReputation;

    public string szDiscordURL;

    /// Where a matched player is sent to log in and pick their kit. Opening it
    /// logged out is what records it as the page to come back to, so the site
    /// returns them here after login on its own.
    public string szMatchURL;

    /// Which kits.json weapon categories players may equip in the lobby and pick
    /// between with /kit — a subset of what the site actually supports, so this
    /// can grow (or shrink) without a plugin release. The first entry is the
    /// default a player equips on join.
    public List<string> AllowedKitCategories;

    /// Handed out when the website's kit data can't be fetched, so a site outage
    /// (or a player who has never saved a kit) still leaves someone able to
    /// fight rather than standing in the arena with an empty inventory. One
    /// entry per category in AllowedKitCategories.
    public List<FallbackKit> FallbackKits;

    public void LoadDefaults() {
        szAPIURL = "https://api.peekseek.com";
        szHexDefaultChatColor = "#FFFFFF";
        szChatIconURL = "https://domain.org/image.png";
        // Bitmask selecting mmconfig gamemode 0 (Competitive) — matches the site's
        // /queue/heartbeat bit convention, not a literal gamemode array index.
        szRegion = "paris";
        GamemodeBitmask = 1;
        HeartbeatIntervalSeconds = 5f;
        SpawnProtectionSeconds = 5f;
        RespawnDelaySeconds = 10f;
        RespawnCountdownAtSecondsRemaining = [10, 5, 3, 2, 1];
        AllowedKitCategories = ["meta", "sniper"];
        EnableQueue = false;
        ForceSoloPlayers = true;
        UnlimitedStamina = true;
        ShowRoundTimerInName = true;
        szServerDisplayName = "PeekSeek.net";
        // The bounce is standard kit, the heal has to be earned. One of each at
        // a time so they stay a reward for the next fight rather than a stockpile.
        KillRewards = [
            new RewardItem { ItemID = 1838, MaxCarried = 1, GrantOnSpawn = true },
            new RewardItem { ItemID = 395, MaxCarried = 1, GrantOnSpawn = false }
        ];
        RemoveDroppedItems = true;
        ResetIntervalSeconds = 600f;
        ShowEloAsReputation = true;
        szDiscordURL = "https://discord.gg/NKgTe6RnvH";
        szMatchURL = "https://dev.peekseek.net/match";

        // The same first-option gun, attachments and outfit the website would
        // resolve for a player who has never saved a kit — see kits.json and
        // metagun.py. Kept in config rather than hardcoded so a balance change
        // doesn't need a plugin release.
        FallbackKits = [
            new FallbackKit {
                Category = "meta",
                Items = [
                    new FallbackItem { ItemID = 363, Amount = 1, State = "148,0,239,3,8,0,150,0,6,0,30,2,1,100,100,100,0,100" },
                    new FallbackItem { ItemID = 309, Amount = 1, State = "" },
                    new FallbackItem { ItemID = 310, Amount = 1, State = "" },
                    new FallbackItem { ItemID = 253, Amount = 1, State = "" },
                    new FallbackItem { ItemID = 307, Amount = 1, State = "" },
                    new FallbackItem { ItemID = 308, Amount = 1, State = "" }
                ],
                Magazine = new FallbackItem { ItemID = 6, Amount = 5, State = "" }
            },
            new FallbackKit {
                Category = "sniper",
                Items = [
                    new FallbackItem { ItemID = 1382, Amount = 1, State = "148,0,239,3,8,0,0,0,104,5,7,1,1,100,100,100,100,100" },
                    new FallbackItem { ItemID = 309, Amount = 1, State = "" },
                    new FallbackItem { ItemID = 310, Amount = 1, State = "" },
                    new FallbackItem { ItemID = 253, Amount = 1, State = "" },
                    new FallbackItem { ItemID = 307, Amount = 1, State = "" },
                    new FallbackItem { ItemID = 308, Amount = 1, State = "" }
                ],
                Magazine = new FallbackItem { ItemID = 1384, Amount = 5, State = "" }
            }
        ];

        // Placeholder — real Alpha Valley spawn points get filled in once the
        // plugin is installed on the live arena map.
        Spawns = new List<LobbySpawn> {
            new LobbySpawn { Position = new SerializableVector3(0, 0, 0) }
        };
    }
}
