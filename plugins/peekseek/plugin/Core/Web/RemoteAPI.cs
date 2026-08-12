using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using PeekSeek.Core.Models;
using PeekSeek.Core.Models.Enums;
using Rocket.Core.Logging;
using Rocket.Core.Utils;
using SDG.Unturned;
using Steamworks;

namespace PeekSeek.Core.Web;

internal static class RemoteAPI {
    private static readonly string API_URL = PeekSeek.Config.szAPIURL;
    private static readonly string WAIT_URL = $"{API_URL}/unt/eventwait/";
    private static readonly string RESULTS_URL = $"{API_URL}/unt/results/";
    private static readonly string UPDATE_URL = $"{API_URL}/unt/update/";
    private static readonly string KILLS_URL = $"{API_URL}/unt/kills/";

    private static HttpClient Client;
    internal static void Init() {
        Client = new HttpClient();
        Client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    internal static void Shutdown() {
        CancellationTokenSource.Cancel();
        Client.Dispose();
        Client = null;
    }

    private static CancellationTokenSource CancellationTokenSource;
    internal static void StartWaitingForMessages() {
        CancellationTokenSource = new CancellationTokenSource();
        Task.Run(async () => {
            await WaitForMessage(CancellationTokenSource.Token);
        });
    }

    internal static void NotifyServerUpdate(PSUpdateState state) {
        Task.Run(async () => {
            await Client.PostAsync(UPDATE_URL +
                                   $"?matchid={PSManager.Data?.MatchID}", new StringContent(JsonConvert.SerializeObject(state), Encoding.UTF8, "application/json"));
        });
    }

    /// Fire-and-forget, same as NotifyServerUpdate — a dropped kill record isn't worth
    /// blocking the match loop over, and the next kill's request carries no dependency
    /// on this one having landed first.
    internal static void SendKillEvent(PSKillEvent killEvent) {
        Task.Run(async () => {
            await Client.PostAsync(KILLS_URL +
                                   $"?matchid={PSManager.Data?.MatchID}", new StringContent(JsonConvert.SerializeObject(killEvent), Encoding.UTF8, "application/json"));
        });
    }

    /// Takes the match id by value: the caller clears PSManager.Data before this task runs.
    internal static void SendResults(string json, int matchID) {
        Task.Run(async () => {
            HttpResponseMessage msg = await Client.PostAsync(RESULTS_URL + $"?matchid={matchID}", new StringContent(json, Encoding.UTF8, "application/json"));
            TaskDispatcher.QueueOnMainThread(()
                    => Logger.Log($"{RESULTS_URL + $"?matchid={matchID}"} returned {msg.StatusCode}")
            );
        });
    }

    /// The code players paste into Unturned's connect box — the game server's Steam ID,
    /// the same value the in-game "Copy Server Code" button copies. Provider.server is
    /// the wrong field for this (reads back as 0 on a dedicated server — it's meant for a
    /// client's view of the server it's connected to, not the server's own identity);
    /// SteamGameServer.GetSteamID() is what Unturned's own "Copy Server Code" command
    /// actually calls.
    private static string ServerCode => SteamGameServer.GetSteamID().ToString();

    private static string WaitQuery =>
        $"?port={Provider.port}&code={ServerCode}&matchid={PSManager.Data?.MatchID.ToString() ?? "0"}";

    private static async Task WaitForMessage(CancellationToken token) {
        if (PeekSeek.Config.UseTestingJSON) {
            // Testing mode stands in for the real API entirely — polling dev/prod
            // alongside it would let a live "stop" response wipe the fixture's
            // match state seconds after it's seeded.
            try {
                string szJson = File.ReadAllText("TestingJSON.json");
                TaskDispatcher.QueueOnMainThread(() => {
                    PSManager.ProcessData(
                        JsonConvert.DeserializeObject<PSData>(
                            szJson
                    ));
                });
            } catch (Exception ex) {
                Logger.LogError($"Failed to load TestingJSON.json! {ex.Message}");
            }
            return;
        }

        while (!token.IsCancellationRequested) {
            string szResponse;
            try {
                HttpResponseMessage message = await Client.GetAsync(WAIT_URL + WaitQuery, token);
                TaskDispatcher.QueueOnMainThread(() => {
                    Logger.Log($"{WAIT_URL + WaitQuery} returned {message.StatusCode}");
                });
                szResponse = await message.Content.ReadAsStringAsync();
            } catch (WebException ex) {
                await Task.Delay(5000, token);

                TaskDispatcher.QueueOnMainThread(() => {
                    Logger.LogError($"Failed to fetch data from PeekSeek API! {WAIT_URL}, {ex.Message}");
                });

                continue;
            }

            if (string.IsNullOrEmpty(szResponse)) {
                TaskDispatcher.QueueOnMainThread(() => {
                    Logger.LogError($"Unexpected response from PeekSeek API! {WAIT_URL + WaitQuery}, Empty response");
                });
                await Task.Delay(5000, token);
                continue;
            }

            try {
                TaskDispatcher.QueueOnMainThread(() => {
                    PSManager.ProcessData(
                        JsonConvert.DeserializeObject<PSData>(
                            szResponse
                        ));
                });
            } catch (JsonException ex) {
                TaskDispatcher.QueueOnMainThread(() => {
                    Logger.LogError($"Failed to parse JSON from PeekSeek API! {WAIT_URL + WaitQuery}, {ex.Message}");
                });
            }


            await Task.Delay(5000, token);
        }
        Logger.Log($"Cancellation requested, stopping.");
    }
}
