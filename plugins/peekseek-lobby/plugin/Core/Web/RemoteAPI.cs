using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using PeekSeekLobby.Core.Models;
using Rocket.Core.Logging;
using Rocket.Core.Utils;

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
    /// A heartbeat that comes back carrying a match id means the player has been
    /// matched, and the lobby's job is then to hand them over to the server
    /// hosting it.
    internal static void SendHeartbeat(ulong steam64, int gamemode, string region) {
        Task.Run(async () => {
            try {
                HttpResponseMessage response = await Client.GetAsync(
                    $"{ApiUrl}/unt/queue/heartbeat?steam64={steam64}&gamemode={gamemode}&region={region}"
                );
                if (!response.IsSuccessStatusCode) {
                    ReportHeartbeatStatus(response.StatusCode);
                    return;
                }
                ReportHeartbeatStatus(response.StatusCode);

                int matchId = MatchIdFrom(await ReadJson(response));
                if (matchId <= 0) return;

                string code = await FetchMatchServerCode(matchId);
                if (string.IsNullOrEmpty(code)) return;

                TaskDispatcher.QueueOnMainThread(() => LobbyManager.SendToMatch(steam64, code));
            } catch (Exception ex) {
                Logger.LogError($"Failed to send queue heartbeat for {steam64}: {ex.Message}");
            }
        });
    }

    private static HttpStatusCode? LastHeartbeatStatus;

    /// Every connected player heartbeats every few seconds, so logging each
    /// result would bury the console — this reports only when the outcome
    /// changes, which is enough to see "401, the server IP isn't allow-listed"
    /// or to watch it recover without the noise.
    private static void ReportHeartbeatStatus(HttpStatusCode status) {
        if (LastHeartbeatStatus == status) return;
        LastHeartbeatStatus = status;

        TaskDispatcher.QueueOnMainThread(() => {
            if (status == HttpStatusCode.OK) {
                Logger.Log($"Queue heartbeats are being accepted ({(int)status}).");
                return;
            }
            Logger.LogWarning(
                $"Queue heartbeats are being rejected with {(int)status} {status} — "
                + $"players will not enter matchmaking. Check {ApiUrl} and that this server's IP is allow-listed.");
        });
    }

    /// Null unless the response really is JSON. The endpoint 404s to an HTML
    /// error page when the site is down or predates this route, and every player
    /// heartbeats every few seconds — parsing that would bury the console in
    /// identical parse errors.
    private static async Task<JObject> ReadJson(HttpResponseMessage response) {
        if (response.Content.Headers.ContentType?.MediaType != "application/json") return null;
        return JObject.Parse(await response.Content.ReadAsStringAsync());
    }

    private static int MatchIdFrom(JObject body) {
        JToken matchId = body?["matchid"];
        return matchId == null || matchId.Type == JTokenType.Null ? 0 : (int)matchId;
    }

    /// The match's `claimed` value — the Unturned server code of whichever server
    /// picked the match up, which is what a player connects with. Empty while no
    /// server has claimed it yet, so the next heartbeat can try again.
    private static async Task<string> FetchMatchServerCode(int matchId) {
        HttpResponseMessage response = await Client.GetAsync($"{ApiUrl}/match/overlay?id={matchId}");
        if (!response.IsSuccessStatusCode) return null;

        JObject body = await ReadJson(response);
        return body?["claimed"]?.ToString();
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
