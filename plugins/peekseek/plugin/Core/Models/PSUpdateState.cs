using System.Collections.Generic;
using Newtonsoft.Json;

namespace PeekSeek.Core.Models;

internal class PSUpdateState {
    public List<PSUpdateTeam> Teams { get; set; }

    public PSUpdateState() {
        Teams = [];
    }
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
    [JsonProperty("kills")]
    public int Kills { get; set; }
    [JsonProperty("deaths")]
    public int Deaths { get; set; }
    [JsonProperty("isAlive")]
    public bool IsAlive { get; set; }
}
