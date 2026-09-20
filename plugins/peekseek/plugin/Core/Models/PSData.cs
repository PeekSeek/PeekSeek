using System.Collections.Generic;
using System.Linq;
using JetBrains.Annotations;
using Newtonsoft.Json;

namespace PeekSeek.Core.Models;

/// The shapes `/server/v1/session` answers with. Field names are the API's, spelled
/// out with [JsonProperty] rather than relying on a naming convention, so a rename on
/// either side is a compile-time edit here instead of a field that silently reads null.
[UsedImplicitly]
internal class PSSettings {
    [JsonProperty("ballistics")]
    internal bool Ballistics;

    [JsonProperty("movement")]
    internal bool Movement;

    [JsonProperty("map")]
    internal string Map = null!;
}

/// One item in a kit, already resolved by the site — id, how many, and the attachment
/// state bytes to stamp onto it.
[UsedImplicitly]
internal class PSItem {
    [JsonProperty("itemId")]
    internal ushort ItemID;

    [JsonProperty("amount")]
    internal byte Amount;

    [JsonProperty("state")]
    internal byte[] State = [];
}

[UsedImplicitly]
internal class PSPlayer {
    [JsonProperty("steam64")]
    internal ulong Steam64;

    [JsonProperty("kit")]
    internal List<PSItem> Kit = [];
}

[UsedImplicitly]
internal class PSTeam {
    [JsonProperty("name")]
    internal string Name = null!;

    [JsonProperty("players")]
    internal List<ulong> Players = [];
}

[UsedImplicitly]
internal class PSGamemode {
    [JsonProperty("id")]
    internal int Id;

    [JsonProperty("name")]
    internal string Name = null!;
}

[UsedImplicitly]
internal class PSMatch {
    [JsonProperty("id")]
    internal int Id;

    [JsonProperty("region")]
    internal string Region = null!;

    [JsonProperty("gamemode")]
    internal PSGamemode Gamemode = null!;

    [JsonProperty("roundsToWin")]
    internal byte RoundsToWin = 3;

    [JsonProperty("settings")]
    internal PSSettings Settings = null!;

    [JsonProperty("teams")]
    internal List<PSTeam> Teams = null!;

    [JsonProperty("players")]
    internal List<PSPlayer> Players = null!;

    [JsonProperty("spectators")]
    internal List<ulong> Spectators = [];

    /// The kit the site resolved for this player, or an empty list for someone not on
    /// the roster — a spectator, or a player the site did not send one for.
    internal List<PSItem> KitFor(ulong steam64) {
        return Players?.FirstOrDefault(p => p.Steam64 == steam64)?.Kit ?? [];
    }

    /// Whether this match lists the player at all, on any team.
    internal bool HasPlayer(ulong steam64) {
        return Teams != null && Teams.Any(team => team.Players.Contains(steam64));
    }
}

/// One answer to one session poll. Exactly one `action` comes back, and everything
/// else on this object is whatever that action carries.
[UsedImplicitly]
internal class PSSessionAnswer {
    internal const string Idle = "idle";
    internal const string Wait = "wait";
    internal const string Host = "host";
    internal const string Abort = "abort";

    [JsonProperty("action")]
    internal string Action = Idle;

    [JsonProperty("matchId")]
    internal int MatchId;

    [JsonProperty("match")]
    internal PSMatch Match;

    /// Steam64s the site is still waiting on a pick from, for the wait action.
    [JsonProperty("waitingOn")]
    internal List<string> WaitingOn = [];

    [JsonProperty("selectionEndsAt")]
    internal long SelectionEndsAt;

    [JsonProperty("reason")]
    internal string Reason;
}
