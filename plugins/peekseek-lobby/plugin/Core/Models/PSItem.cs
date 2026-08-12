using System.Collections.Generic;
using System.Linq;
using JetBrains.Annotations;
using Newtonsoft.Json.Linq;

namespace PeekSeekLobby.Core.Models;

/// One [itemId, amount, state] entry from the website's /kit/{steam64} response —
/// the same wire shape the main PeekSeek plugin's PSItemConverter reads out of a
/// live match's kit data, reused here since the site emits it identically.
[UsedImplicitly]
internal class PSItem {
    internal ushort ItemID;
    internal byte Amount;
    internal byte[] State = [];

    internal static PSItem Parse(JToken entry) {
        return new PSItem {
            ItemID = (ushort)entry[0],
            Amount = (byte)entry[1],
            State = string.IsNullOrEmpty(entry[2]?.ToString())
                ? []
                : entry[2].ToString().Split(',').Select(byte.Parse).ToArray()
        };
    }

    internal static List<PSItem> ParseArray(JArray items) {
        return items.Select(Parse).ToList();
    }
}
