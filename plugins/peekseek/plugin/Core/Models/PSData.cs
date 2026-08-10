using System;
using System.Collections.Generic;
using System.Linq;
using JetBrains.Annotations;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace PeekSeek.Core.Models;

[UsedImplicitly]
internal class PSSettings {
    [JsonProperty("ballistics")]
    internal bool Ballistics = false;
    [JsonProperty("movement")]
    internal bool Movement = false;
    [JsonProperty("map")]
    internal string Map = null!;
}

[UsedImplicitly]
internal class PSItem {

    internal ushort ItemID = 0;
    internal byte Amount = 0;
    internal byte[] State = [];
}

[UsedImplicitly]
internal class PSUser {
    [JsonProperty("userId")]
    internal ulong UserID = ulong.MinValue;

    [JsonProperty("kit")]
    [JsonConverter(typeof(PSItemConverter))]
    internal List<PSItem> Kit = [];
}

[UsedImplicitly]
internal class PSTeam {
    [JsonProperty("name")]
    internal string Name = null!;
    [JsonProperty("users")]
    internal List<ulong> Users = [];
}

[UsedImplicitly]
internal class PSMeta {
    [JsonProperty("rounds")]
    internal byte Rounds = 0;
}

[UsedImplicitly]
internal class PSData {
    [JsonProperty("matchid")]
    internal int MatchID = 0;
    [JsonProperty("head")]
    internal string Head = null!;
    [JsonProperty("settings")]
    internal PSSettings Settings = null!;
    [JsonProperty("users")]
    internal List<PSUser> Users = null!;
    [JsonProperty("meta")]
    internal PSMeta Meta = null!;
    [JsonProperty("teams")]
    internal List<PSTeam> Teams = null!;
    [JsonProperty("spectators")]
    internal List<ulong> Spectators = null!;
}

internal class PSItemConverter : JsonConverter {
    public override bool CanConvert(Type objectType) {
        return objectType == typeof(List<PSItem>);
    }

    public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer) {
        var kitArray = JArray.Load(reader);

        return kitArray.Select(itemArray => new PSItem {
            ItemID = (ushort)itemArray[0],
            Amount = (byte)itemArray[1],
            State = string.IsNullOrEmpty(itemArray[2]?.ToString())
                    ? []
                    : itemArray[2].ToString().Split(',').Select(byte.Parse).ToArray()
        })
            .ToList();
    }

    public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer) {
        throw new NotImplementedException("Serialization not implemented.");
    }
}
