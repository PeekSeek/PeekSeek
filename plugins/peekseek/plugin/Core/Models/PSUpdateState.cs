using System.Collections.Generic;
using Newtonsoft.Json;

namespace PeekSeek.Core.Models;

/// The live match snapshot pushed to /unt/update and rendered by the stream overlay.
/// Every field here is something the overlay draws, so adding one means the overlay
/// can show it without another round trip.
internal class PSUpdateState {
    [JsonProperty("teams")]
    public List<PSUpdateTeam> Teams { get; set; }
    /// The round being played right now, 1-based.
    [JsonProperty("round")]
    public int Round { get; set; }
    /// Rounds a team must win to take the match.
    [JsonProperty("roundsToWin")]
    public int RoundsToWin { get; set; }
    /// The most recent round or match result, or null before the first one.
    [JsonProperty("event")]
    public PSUpdateEvent Event { get; set; }
    /// The last few kills, oldest first, for the overlay's kill feed.
    [JsonProperty("feed")]
    public List<PSUpdateKill> Feed { get; set; }

    public PSUpdateState() {
        Teams = [];
        Feed = [];
    }
}

/// One entry in the kill feed. Ids increase for the life of the match so the overlay
/// can animate in only the entries it has not shown yet.
internal class PSUpdateKill {
    [JsonProperty("id")]
    public int Id { get; set; }
    /// Null when nobody killed them — bleeding out, falling, the arena border.
    [JsonProperty("killer")]
    public string Killer { get; set; }
    [JsonProperty("victim")]
    public string Victim { get; set; }
    /// Item id the killer was holding, or 0 when there was no killer.
    [JsonProperty("weapon")]
    public int Weapon { get; set; }
    /// Lowercased ELimb, so the overlay can mark headshots.
    [JsonProperty("limb")]
    public string Limb { get; set; }
}

/// A result worth announcing on the overlay. Id increases with every new event so the
/// overlay can tell a fresh result from the same one being re-sent each tick.
internal class PSUpdateEvent {
    [JsonProperty("id")]
    public int Id { get; set; }
    /// "roundWon" or "matchWon".
    [JsonProperty("kind")]
    public string Kind { get; set; }
    [JsonProperty("team")]
    public string Team { get; set; }
    [JsonProperty("round")]
    public int Round { get; set; }
}

internal class PSUpdateTeam {
    [JsonProperty("teamName")]
    public string TeamName { get; set; }
    [JsonProperty("wins")]
    public int Wins { get; set; }
    [JsonProperty("losses")]
    public int Losses { get; set; }
    [JsonProperty("users")]
    public Dictionary<string, UserStats> Users { get; set; }

    public PSUpdateTeam() {
        Users = [];
    }
}

internal class UserStats {
    /// The player's in-game character name, so the overlay does not have to show a
    /// Steam persona that may not match what casters see in game.
    [JsonProperty("name")]
    public string Name { get; set; }
    [JsonProperty("kills")]
    public int Kills { get; set; }
    [JsonProperty("deaths")]
    public int Deaths { get; set; }
    /// Kills landed on the skull, counted separately so the overlay can show more
    /// than a bare kill/death pair.
    [JsonProperty("headshots")]
    public int Headshots { get; set; }
    /// Where the player stands in the round, as one of:
    ///   "alive"   — in play
    ///   "dead"    — killed this round; they respawn immediately, so this is tracked
    ///               rather than read off PlayerLife
    ///   "waiting" — in the spawn safezone, which is the between-rounds lobby, so
    ///               they are not in play either
    ///   "offline" — not on the server
    [JsonProperty("state")]
    public string State { get; set; }
    /// 0-100.
    [JsonProperty("health")]
    public int Health { get; set; }
    [JsonProperty("isBleeding")]
    public bool IsBleeding { get; set; }
    [JsonProperty("isBroken")]
    public bool IsBroken { get; set; }
    /// Item id currently held, or 0 for empty hands.
    [JsonProperty("equipped")]
    public int Equipped { get; set; }
    /// Everything they are carrying, one entry per item id.
    [JsonProperty("inventory")]
    public List<PSUpdateItem> Inventory { get; set; }

    public UserStats() {
        Inventory = [];
    }
}

internal class PSUpdateItem {
    [JsonProperty("id")]
    public int Id { get; set; }
    [JsonProperty("name")]
    public string Name { get; set; }
    /// The item's EItemType lowercased ("gun", "medical", "throwable", "magazine", ...),
    /// which is how the overlay decides which slot and icon treatment an item gets.
    [JsonProperty("kind")]
    public string Kind { get; set; }
    [JsonProperty("amount")]
    public int Amount { get; set; }
}
