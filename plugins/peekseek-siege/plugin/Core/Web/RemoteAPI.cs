using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using PeekSeekSiege.Core.Models;
using Rocket.Core.Logging;
using Rocket.Core.Utils;

namespace PeekSeekSiege.Core;

/// The one thing Siege asks the PeekSeek site for: what gun a player fights with.
///
/// Siege makes its own matches from whoever is on the server, so it does not use the
/// session contract the match plugin does. It still uses the site's kits, because a
/// player who has spent time setting a loadout up on peekseek.com should walk into this
/// gamemode holding it.
///
/// Kits are fetched when a player joins and held until they leave. That is on purpose:
/// a round starts by handing everybody their gear right now, and a round is not going
/// to wait on an HTTP round trip. A player whose fetch has not landed yet gets the
/// gamemode's default items for that round and their real kit from the next one.
internal static class RemoteAPI {
    private static string Base => PeekSeekSiege.Config.szAPIURL.TrimEnd('/') + "/server/v1";

    private static HttpClient Client;

    private static readonly Dictionary<ulong, List<SiegeItem>> Kits = [];

    internal static void Init() {
        Client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        Client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    internal static void Shutdown() {
        Client?.Dispose();
        Client = null;
        Kits.Clear();
    }

    /// The player's kit as last fetched, or an empty list if there is not one yet.
    internal static List<SiegeItem> KitFor(ulong steam64) {
        return Kits.TryGetValue(steam64, out List<SiegeItem> kit) ? kit : [];
    }

    internal static void Forget(ulong steam64) {
        Kits.Remove(steam64);
    }

    /// Pull one player's configured kit category and remember it.
    internal static void Fetch(ulong steam64) {
        if (Client == null) return;

        Task.Run(async () => {
            List<SiegeItem> items = await Read(steam64);
            if (items == null) return;

            TaskDispatcher.QueueOnMainThread(() => Kits[steam64] = items);
        });
    }

    private static async Task<List<SiegeItem>> Read(ulong steam64) {
        try {
            HttpResponseMessage response = await Client.GetAsync($"{Base}/players/{steam64}/kits");
            if (!response.IsSuccessStatusCode) return null;

            JObject body = JObject.Parse(await response.Content.ReadAsStringAsync());
            string category = PeekSeekSiege.Config.KitCategory;

            if (body[category] is not JObject kit || kit["items"] is not JArray items) return null;

            List<SiegeItem> parsed = items.Select(Parse).ToList();

            // The site breaks the magazine out of the item list so a caller can keep a
            // spare topped up separately. Siege has no reload economy, so it just goes
            // in the bag with everything else.
            if (kit["magazine"] is JObject magazine) parsed.Add(Parse(magazine));

            return parsed;
        } catch (Exception ex) {
            Logger.LogError($"Failed to fetch kit for {steam64}: {ex.Message}");
            return null;
        }
    }

    /// The site sends state as an array of bytes; this plugin's config writes it as the
    /// comma-separated string a human can paste out of mmconfig.json. Same bytes.
    private static SiegeItem Parse(JToken entry) {
        byte[] state = entry["state"] is JArray bytes ? bytes.Select(b => (byte)b).ToArray() : [];

        return new SiegeItem {
            ItemId = (ushort)entry["itemId"],
            Amount = (byte)entry["amount"],
            State = string.Join(",", state)
        };
    }
}
