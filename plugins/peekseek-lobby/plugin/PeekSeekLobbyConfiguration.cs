using System.Collections.Generic;
using System.Xml.Serialization;
using PeekSeek.Core.Models;
using PeekSeekLobby.Core.Models;
using Rocket.API;

namespace PeekSeekLobby;

[XmlRoot("Config")]
public class PeekSeekLobbyConfiguration : IRocketPluginConfiguration {
    public string szAPIURL;
    public string szHexDefaultChatColor;
    public string szChatIconURL;
    public string szRegion;

    /// Where reports from this server land. Empty uses whatever channel the site is
    /// configured with, which is the normal case; setting it routes one server's
    /// reports somewhere else without redeploying the site.
    public string szReportChannel;

    /// The Discord role a report pings. Empty uses the site's. The ping is sent in
    /// the message content rather than the embed, because a mention inside an embed
    /// renders as one but never notifies anybody.
    public string szStaffRole;
    public float HeartbeatIntervalSeconds;
    public float SpawnProtectionSeconds;

    /// How long a player waits at the standard spawn before being deployed to a
    /// lobby spawn point. They are alive and protected for the whole wait, the
    /// pause is a breather between lives, not a death screen.
    public float RespawnDelaySeconds;

    /// How long a matched player is warned and counted down for before the lobby
    /// takes them out of the arena. Long enough to finish the fight they are in;
    /// short enough that the match is not kept waiting on them.
    public float MatchWarningSeconds;

    /// How long somebody is here before the lobby puts the queue screen in front of
    /// them. Nobody is queued until they say so; long enough to have had a fight,
    /// short enough that somebody who came for a match is not left wondering.
    public float QueuePromptSeconds;

    /// The regions under the tiles on that screen. The gamemodes are not here: they
    /// are the site's, read from `/server/v1/gamemodes` on load, because a list that
    /// drifted from the site would queue players for the wrong thing.
    public List<QueueRegion> QueueRegions;

    /// The ping from which the question warns them. Matches in this region scale a
    /// shooter's damage down above a ping, and a player who finds that out mid-match
    /// was sent somewhere they should have been warned about first.
    public int HighPingMs;

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
    /// gap. A minute: long enough to cross the arena to the next fight, short
    /// enough that a streak is still a run and not a tally of the whole life.
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

    /// How often the arena is wiped back to a clean slate: world items and
    /// player-placed builds cleared, session stats zeroed, everyone respawned. The
    /// clock only runs while two or more are in the arena.
    public float ResetIntervalSeconds;

    /// How long the scoreboard is on everybody's screen at a reset.
    public float ResetBoardSeconds;

    /// Mirror each player's Elo into their in-game reputation, as a negative
    /// number. Unturned moves reputation on its own for kills and similar, so this
    /// is re-asserted on a timer rather than set once.
    public bool ShowEloAsReputation;

    /// Where a matched player is sent to log in and pick their kit. Opening it
    /// logged out is what records it as the page to come back to, so the site
    /// returns them here after login on its own.
    public string szMatchURL;

    /// Which kits.json weapon categories players may equip in the lobby and pick
    /// between with /kit, a subset of what the site actually supports, so this
    /// can grow (or shrink) without a plugin release. The first entry is the
    /// default a player equips on join.
    public List<string> AllowedKitCategories;

    /// Handed out when the website's kit data can't be fetched, so a site outage
    /// (or a player who has never saved a kit) still leaves someone able to
    /// fight rather than standing in the arena with an empty inventory. One
    /// entry per category in AllowedKitCategories.
    public List<FallbackKit> FallbackKits;

    /// Master switch for the solo target range. Off removes /range entirely,
    /// the command still exists but says the range is not set up here.
    public bool RangeEnabled;

    /// The barricade placed as a target. Must be an item this server can place as
    /// a barricade and one bullets can actually hit: an invulnerable barricade
    /// never raises the damage event the range counts hits with. 1408 is the cloth
    /// mannequin, humanoid, and flagged Vulnerable in the vanilla assets, unlike
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

    /// The kill feed at the top right, from the PeekSeek Kill Feed workshop item
    /// (`plugins/peekseek-match/ui`), which the server has to list in
    /// WorkshopDownloadConfig.json. Left empty, there is no feed; the chat lines stay
    /// either way. The key is any small number no other plugin here uses.
    public string KillFeedEffectGUID;
    public short KillFeedKey;
    public string ItemIconURL;
    public int KillFeedRows;
    public float KillFeedRowSeconds;
    public int KillFeedNameLength;

    /// The rest of the screens from the same workshop item: the bar along the top with
    /// a face per player, the banner, the death card with the respawn count in it,
    /// the scoreboard, and the board the lobby asks its one question on. The picture
    /// for a player the site has no picture for yet is Steam's own placeholder.
    public string LobbyBarEffectGUID;
    public short LobbyBarKey;
    public string DefaultAvatarURL;
    public string BannerEffectGUID;
    public short BannerKey;
    public float BannerSeconds;
    public string DeathCardEffectGUID;
    public short DeathCardKey;
    public float DeathCardSeconds;
    public string ScoreboardEffectGUID;
    public short ScoreboardKey;
    public string QueueEffectGUID;
    public short QueueKey;

    /// Where a rank badge comes from, with `{0}` standing for the rank's icon name,
    /// which the site answers on every player read. The site serves the same badges
    /// its own pages wear.
    public string RankIconURL;

    /// Fill in the fields this file is too old to carry. See PeekSeek's
    /// Core/ConfigDefaults.cs: Rocket never merges a new field into a written
    /// configuration.xml, so without this the board nobody has configured never opens
    /// and the lobby asks every player to queue the instant they land.
    public System.Collections.Generic.IReadOnlyList<string> FillInWhatOlderFilesLack() {
        PeekSeekLobbyConfiguration fresh = new();
        fresh.LoadDefaults();
        PeekSeek.Core.ConfigDefaults defaults = new();

        QueuePromptSeconds = defaults.Or(QueuePromptSeconds, fresh.QueuePromptSeconds, nameof(QueuePromptSeconds));
        HighPingMs = defaults.Or(HighPingMs, fresh.HighPingMs, nameof(HighPingMs));
        ResetBoardSeconds = defaults.Or(ResetBoardSeconds, fresh.ResetBoardSeconds, nameof(ResetBoardSeconds));
        BannerEffectGUID = defaults.Or(BannerEffectGUID, fresh.BannerEffectGUID, nameof(BannerEffectGUID));
        BannerKey = defaults.Or(BannerKey, fresh.BannerKey, nameof(BannerKey));
        BannerSeconds = defaults.Or(BannerSeconds, fresh.BannerSeconds, nameof(BannerSeconds));
        DeathCardEffectGUID = defaults.Or(DeathCardEffectGUID, fresh.DeathCardEffectGUID, nameof(DeathCardEffectGUID));
        DeathCardKey = defaults.Or(DeathCardKey, fresh.DeathCardKey, nameof(DeathCardKey));
        DeathCardSeconds = defaults.Or(DeathCardSeconds, fresh.DeathCardSeconds, nameof(DeathCardSeconds));
        ScoreboardEffectGUID = defaults.Or(ScoreboardEffectGUID, fresh.ScoreboardEffectGUID, nameof(ScoreboardEffectGUID));
        ScoreboardKey = defaults.Or(ScoreboardKey, fresh.ScoreboardKey, nameof(ScoreboardKey));
        QueueEffectGUID = defaults.Or(QueueEffectGUID, fresh.QueueEffectGUID, nameof(QueueEffectGUID));
        QueueKey = defaults.Or(QueueKey, fresh.QueueKey, nameof(QueueKey));
        RankIconURL = defaults.Or(RankIconURL, fresh.RankIconURL, nameof(RankIconURL));
        QueueRegions = QueueRegions is { Count: > 0 } ? QueueRegions : fresh.QueueRegions;

        return defaults.Filled;
    }

    public void LoadDefaults() {
        szAPIURL = "https://api.peekseek.com";
        szHexDefaultChatColor = "#FFFFFF";
        szChatIconURL = "https://domain.org/image.png";
        szRegion = "paris";
        szReportChannel = "";
        szStaffRole = "";
        HeartbeatIntervalSeconds = 5f;
        SpawnProtectionSeconds = 5f;
        RespawnDelaySeconds = 5f;
        MatchWarningSeconds = 3f;
        QueuePromptSeconds = 60f;
        HighPingMs = 100;
        QueueRegions = [
            new QueueRegion { Name = "EU-West", Code = "paris" },
            new QueueRegion { Name = "AS-South", Code = "singapore" },
            new QueueRegion { Name = "US-West", Code = "utah" },
            new QueueRegion { Name = "SA-East", Code = "brazil" }
        ];
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
        StreakWindowSeconds = 60f;
        // Two kills buys an umbrella and a bounce, three fits a drum to the gun
        // they are already holding, five is a rampage. Every id here is one the
        // site already hands out in a gamemode's powerups.
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
                // chamber is fitted, the ranger drum to a ranger gun, the military
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
        ResetBoardSeconds = 5f;
        RangeEnabled = true;
        RangeTargetItemId = 1408;
        RangeTargetCount = 8;
        RangeDistance = 18f;
        RangeSpreadDegrees = 90f;
        ShowEloAsReputation = true;
        szMatchURL = "https://dev.peekseek.net/match";

        KillFeedEffectGUID = "c4d5e6f708192a3b4c5d6e7f8091a2b3";
        KillFeedKey = 27320;
        ItemIconURL = "https://peekseek.net/static/unturned/{0}.png";
        KillFeedRows = 6;
        KillFeedRowSeconds = 7f;
        KillFeedNameLength = 24;

        LobbyBarEffectGUID = "d6e7f8a9b0c1d2e3f4a5b6c7d8e9f0a1";
        LobbyBarKey = 27321;
        DefaultAvatarURL = "https://avatars.steamstatic.com/fef49e7fa7e1997310d705b2a6158ff8dc1cdfeb_full.jpg";
        BannerEffectGUID = "df8dfb9bbb304f6faf46884227651559";
        BannerKey = 27322;
        BannerSeconds = 4f;
        DeathCardEffectGUID = "6969104457d9419fa2f3e53f5e1c6b76";
        DeathCardKey = 27323;
        DeathCardSeconds = 5f;
        ScoreboardEffectGUID = "6441527b813a4bd7b17809470064a1af";
        ScoreboardKey = 27324;
        QueueEffectGUID = "4369a4e938bf4db3862c83355a910129";
        QueueKey = 27325;
        RankIconURL = "https://peekseek.net/static/ranks/{0}.png";

        // The same first-option gun, attachments and outfit the website would
        // resolve for a player who has never saved a kit, see kits.json and
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

        // Placeholder, real Alpha Valley spawn points get filled in once the
        // plugin is installed on the live arena map.
        Spawns = new List<LobbySpawn> {
            new LobbySpawn { Position = new SerializableVector3(0, 0, 0) }
        };
    }
}
