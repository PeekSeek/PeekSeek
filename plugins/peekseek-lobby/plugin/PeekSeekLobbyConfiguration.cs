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

    /// This server's Unturned connect code, so an in-game /report can link staff
    /// straight into it. Empty leaves the join button off the report rather than
    /// posting one that goes nowhere.
    public string szServerCode;

    /// Where reports from this server land. Empty uses whatever channel the site is
    /// configured with, which is the normal case; setting it routes one server's
    /// reports somewhere else without redeploying the site.
    public string szReportChannel;

    /// The Discord role a report pings. Empty uses the site's. The ping is sent in
    /// the message content rather than the embed, because a mention inside an embed
    /// renders as one but never notifies anybody.
    public string szStaffRole;
    public ushort GamemodeBitmask;
    public float HeartbeatIntervalSeconds;
    public float SpawnProtectionSeconds;

    /// How long a player waits at the standard spawn before being deployed to a
    /// lobby spawn point. They are alive and protected for the whole wait — the
    /// pause is a breather between lives, not a death screen.
    public float RespawnDelaySeconds;

    /// How long a matched player is warned and counted down for before the lobby
    /// takes them out of the arena. Long enough to finish the fight they are in;
    /// short enough that the match is not kept waiting on them.
    public float MatchWarningSeconds;

    /// How long a matched player gets to answer the loadout prompt before the lobby
    /// locks whatever they have not answered to its first option.
    ///
    /// This is the last thing standing between a match being made and everyone on it
    /// being teleported into it, so it is a conversion setting as much as a fairness
    /// one: the site cannot hand the match to a server until every player has chosen,
    /// and a player who is still reading is a player their opponent is waiting on.
    public float MatchPickSeconds;

    public List<LobbySpawn> Spawns;

    /// How many spare magazines a player carries, whatever the kit itself asks
    /// for. The magazine check keeps exactly this many topped up, so it is both
    /// the starting count and the cap.
    public byte SpareMagazines;

    /// Master switch for matchmaking. Off means the lobby is purely a warm-up
    /// server: no heartbeats leave the box, nobody is matched or relayed, and
    /// /noqueue has nothing to toggle.
    public bool EnableQueue;

    /// Consumables restocked to their cap on every kill. GrantOnSpawn decides
    /// which of them players also start a life with. Raise MaxCarried to let
    /// players bank more than one.
    public List<RewardItem> KillRewards;

    /// Paid out for killing several players in a row. Each entry fires on its own
    /// exact kill count, so a 5-kill streak collects the 2, 3 and 5 rewards on the
    /// way up rather than only the last one.
    public List<StreakReward> StreakRewards;

    /// How long a streak survives between kills. A kill that lands later than this
    /// starts the count again at one; dying ends a streak outright, whatever the
    /// gap. Keeping it short makes a streak a burst of fighting rather than a
    /// tally of the whole life.
    public float StreakWindowSeconds;

    /// Everyone fights solo. Steam group membership is applied automatically on
    /// connect and group mates cannot damage each other, so leaving it alone
    /// would quietly make the arena unplayable for anyone sharing a group.
    public bool ForceSoloPlayers;

    /// Sprinting never costs anything here. A warm-up server is about fights,
    /// not about running out of breath crossing the map to reach one.
    public bool UnlimitedStamina;

    /// Dropped items are deleted the moment they hit the ground, so the arena
    /// never fills with the guns and magazines of everyone who died in it.
    public bool RemoveDroppedItems;

    /// How often the arena is wiped back to a clean slate — world items and
    /// player-placed builds cleared, session stats zeroed, everyone respawned.
    public float ResetIntervalSeconds;

    /// Mirror each player's Elo into their in-game reputation, as a negative
    /// number. Unturned moves reputation on its own for kills and similar, so this
    /// is re-asserted on a timer rather than set once.
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

    /// How many players this lobby tries to keep in the arena rather than sending
    /// off to matches. Only the surplus above this is offered to the queue, longest
    /// wait first, because a browser listing showing a handful of players is what
    /// brings the next one in and every 1v1 started takes two of them out of it.
    ///
    /// Set it to 0 to go back to queueing everybody the moment they are eligible.
    public byte MinLobbyPopulation;

    /// How long a player can be held out of the queue by MinLobbyPopulation before
    /// they are offered a match regardless. The floor is there to keep the arena
    /// worth joining, not to keep people who came for a match from ever getting
    /// one — without this a lobby resting exactly on the floor never plays a single
    /// match and empties itself the slow way instead. Zero means never release.
    public float LobbyHoldMaxSeconds;

    /// Master switch for the solo target range. Off removes /range entirely —
    /// the command still exists but says the range is not set up here.
    public bool RangeEnabled;

    /// The barricade placed as a target. Must be an item this server can place as
    /// a barricade and one bullets can actually hit: an invulnerable barricade
    /// never raises the damage event the range counts hits with. 1408 is the cloth
    /// mannequin — humanoid, and flagged Vulnerable in the vanilla assets, unlike
    /// the metal one at 1409.
    public ushort RangeTargetItemId;

    /// How many targets a run puts up. Every one goes down in a single hit
    /// whatever it is shot with, so this is the whole length of a run.
    public byte RangeTargetCount;

    /// Metres out from the player the row is placed, and how many degrees of arc
    /// it is spread across. Wide and close is a tracking drill, narrow and far is
    /// a precision one.
    public float RangeDistance;
    public float RangeSpreadDegrees;

    public void LoadDefaults() {
        szAPIURL = "https://api.peekseek.com";
        szHexDefaultChatColor = "#FFFFFF";
        szChatIconURL = "https://domain.org/image.png";
        // Bitmask selecting mmconfig gamemode 0 (Competitive) — matches the site's
        // /queue/heartbeat bit convention, not a literal gamemode array index.
        szRegion = "paris";
        szServerCode = "";
        szReportChannel = "";
        szStaffRole = "";
        GamemodeBitmask = 1;
        HeartbeatIntervalSeconds = 5f;
        SpawnProtectionSeconds = 5f;
        RespawnDelaySeconds = 10f;
        MatchWarningSeconds = 3f;
        MatchPickSeconds = 8f;
        SpareMagazines = 1;
        AllowedKitCategories = ["meta", "sniper"];
        EnableQueue = true;
        ForceSoloPlayers = true;
        UnlimitedStamina = true;
        // Every kill earns a heal, one at a time so it stays a reward for the next
        // fight rather than a stockpile. Bounces are no longer standard equipment:
        // they are a streak reward now, so they have to be fought for.
        KillRewards = [
            new RewardItem { ItemID = 395, MaxCarried = 1, GrantOnSpawn = false }
        ];
        StreakWindowSeconds = 10f;
        // Two kills buys an umbrella and a bounce, three fits a drum to the gun
        // they are already holding, five is a rampage. Every id here is one the
        // site already hands out in mmconfig.json's powerups.
        StreakRewards = [
            new StreakReward {
                KillsRequired = 2,
                CooldownSeconds = 60f,
                szChatMessage = "<color=white>{0}</color> is on a <color=white>double kill</color>.",
                Items = [
                    new StreakItem { ItemID = 1103, Amount = 1 },
                    new StreakItem { ItemID = 1838, Amount = 1 }
                ]
            },
            new StreakReward {
                KillsRequired = 3,
                CooldownSeconds = 60f,
                szChatMessage = "<color=white>{0}</color> earned a <color=white>drum</color>.",
                // Both drums are listed and only the one the equipped gun can
                // chamber is fitted — the ranger drum to a ranger gun, the military
                // drum to a military one, and nothing at all to a sniper.
                Items = [
                    new StreakItem { ItemID = 125, Amount = 1 },
                    new StreakItem { ItemID = 17, Amount = 1 }
                ]
            },
            new StreakReward {
                KillsRequired = 5,
                CooldownSeconds = 60f,
                szChatMessage = "<color=white>{0}</color> is on a <color=white>rampage</color>!",
                Items = [
                    new StreakItem { ItemID = 1520, Amount = 2 },
                    new StreakItem { ItemID = 1838, Amount = 2 }
                ]
            }
        ];
        RemoveDroppedItems = true;
        ResetIntervalSeconds = 300f;
        // Two is the floor because a 1v1 takes exactly two: at four the lobby can
        // start a match and still show two, which the browser still sells.
        MinLobbyPopulation = 2;
        LobbyHoldMaxSeconds = 240f;
        RangeEnabled = true;
        RangeTargetItemId = 1408;
        RangeTargetCount = 8;
        RangeDistance = 18f;
        RangeSpreadDegrees = 90f;
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
                Magazine = new FallbackItem { ItemID = 6, Amount = 1, State = "" }
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
                Magazine = new FallbackItem { ItemID = 1384, Amount = 1, State = "" }
            }
        ];

        // Placeholder — real Alpha Valley spawn points get filled in once the
        // plugin is installed on the live arena map.
        Spawns = new List<LobbySpawn> {
            new LobbySpawn { Position = new SerializableVector3(0, 0, 0) }
        };
    }
}
