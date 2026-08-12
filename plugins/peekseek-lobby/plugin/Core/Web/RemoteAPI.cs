using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using PeekSeekLobby.Core.Models;
using Rocket.Core.Logging;

namespace PeekSeekLobby.Core.Web;

internal static class RemoteAPI {
    private static string ApiUrl => PeekSeekLobby.Config.szAPIURL;

    private static HttpClient Client;

    internal static void Init() {
        Client = new HttpClient();
        Client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    internal static void Shutdown() {
        Client?.Dispose();
        Client = null;
    }

    /// Fire-and-forget, same as the main PeekSeek plugin's RemoteAPI calls — a
    /// dropped heartbeat costs nothing, the next tick's heartbeat covers for it.
    internal static void SendHeartbeat(ulong steam64, int gamemode, string region) {
        Task.Run(async () => {
            try {
                await Client.GetAsync(
                    $"{ApiUrl}/unt/queue/heartbeat?steam64={steam64}&gamemode={gamemode}&region={region}"
                );
            } catch (Exception ex) {
                Logger.LogError($"Failed to send queue heartbeat for {steam64}: {ex.Message}");
            }
        });
    }

    internal static void SendQuit(ulong steam64) {
        Task.Run(async () => {
            try {
                await Client.GetAsync($"{ApiUrl}/unt/queue/quit?steam64={steam64}");
            } catch (Exception ex) {
                Logger.LogError($"Failed to dequeue {steam64}: {ex.Message}");
            }
        });
    }

    /// The public kit endpoint returns every category the player owns at once;
    /// this reads out just the one the lobby actually equips people with.
    internal static async Task<KitLoadout> FetchKit(ulong steam64, string category) {
        try {
            HttpResponseMessage response = await Client.GetAsync($"{ApiUrl}/kit/{steam64}");
            if (!response.IsSuccessStatusCode) return null;

            string json = await response.Content.ReadAsStringAsync();
            JObject body = JObject.Parse(json);
            if (body[category] is not JObject categoryData || categoryData["items"] is not JArray items) return null;

            return new KitLoadout {
                Items = PSItem.ParseArray(items),
                Magazine = categoryData["magazine"] is JArray magazine ? PSItem.Parse(magazine) : null
            };
        } catch (Exception ex) {
            Logger.LogError($"Failed to fetch kit for {steam64}: {ex.Message}");
            return null;
        }
    }
}
