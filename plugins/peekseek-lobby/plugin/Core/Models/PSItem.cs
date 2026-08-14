using System.Collections.Generic;
using System.Linq;
using JetBrains.Annotations;
using Newtonsoft.Json.Linq;

namespace PeekSeekLobby.Core.Models;

/// One item from the site's kit data: `{"itemId": 363, "amount": 1, "state": [1, 2]}`.
/// Self-describing rather than a positional triple, so nothing here has to know that
/// the third slot used to be a comma-separated string of state bytes.
[UsedImplicitly]
internal class PSItem {
    internal ushort ItemID;
    internal byte Amount;
    internal byte[] State = [];

    internal static PSItem Parse(JToken entry) {
        return new PSItem {
            ItemID = (ushort)entry["itemId"],
            Amount = (byte)entry["amount"],
            State = entry["state"] is JArray state ? state.Select(b => (byte)b).ToArray() : []
        };
    }

    internal static List<PSItem> ParseArray(JArray items) {
        return items.Select(Parse).ToList();
    }
}
