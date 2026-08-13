using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using PeekSeek.Core.Models;
using Rocket.Core.Utils;
using SDG.Unturned;
using Steamworks;
using Logger = Rocket.Core.Logging.Logger;

namespace PeekSeek.Core.Web;

/// This server's side of the `/server/v1/*` contract.
///
/// One loop drives everything. Every few seconds it tells the site which match this
/// server is holding and does whatever it is told back, and on the way past it retries
/// any match result that has not been acknowledged yet. Keeping both in one loop is
/// what makes the two safe together: for as long as a result is outstanding the server
/// keeps naming that match, and a match a server still names is never treated as
/// abandoned.
internal static class RemoteAPI {
    private const int PollIntervalMs = 3000;
    private const int BackoffMs = 5000;

    private static string Base => PeekSeek.Config.szAPIURL.TrimEnd('/') + "/server/v1";

    private static HttpClient Client;
    private static CancellationTokenSource Cancellation;

    /// The result this server owes the site, kept until the site says it has it. Null
    /// whenever there is nothing outstanding.
    private static PendingResult Outstanding;

    private class PendingResult {
        internal int MatchId;
        internal string Json;
        internal int Attempts;
    }

    internal static void Init() {
        Client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        Client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    internal static void Shutdown() {
        Cancellation?.Cancel();
        Client?.Dispose();
        Client = null;
    }

    /// The code players paste into Unturned's connect box — the game server's Steam ID,
    /// the same value the in-game "Copy Server Code" button copies. Provider.server is
    /// the wrong field for this (reads back as 0 on a dedicated server — it's meant for
    /// a client's view of the server it's connected to, not the server's own identity);
    /// SteamGameServer.GetSteamID() is what Unturned's own "Copy Server Code" command
    /// actually calls.
    private static string ServerCode => SteamGameServer.GetSteamID().ToString();

    internal static void StartSession() {
        Cancellation = new CancellationTokenSource();
        Task.Run(async () => await RunSession(Cancellation.Token));
    }

    private static async Task RunSession(CancellationToken token) {
        if (PeekSeek.Config.UseTestingJSON) {
            LoadTestingMatch();
            return;
        }

        LogOnce($"Session loop started against {Base} as server {ServerCode}.");

        while (!token.IsCancellationRequested) {
            await ReportOutstandingResult(token);

            PSSessionAnswer answer = await Poll(token);
            if (answer != null) {
                TaskDispatcher.QueueOnMainThread(() => PSManager.Apply(answer));
            }

            await Task.Delay(answer == null ? BackoffMs : PollIntervalMs, token);
        }
    }

    /// The match this server is responsible for as far as the site is concerned: the one
    /// it is running, or — if it has finished one and not managed to report it — that
    /// one, so the site keeps the match open until the report lands.
    private static int HeldMatchId => Outstanding?.MatchId ?? PSManager.HeldMatchId;

    private static async Task<PSSessionAnswer> Poll(CancellationToken token) {
        string url = $"{Base}/session?serverCode={ServerCode}&matchId={HeldMatchId}";
        try {
            HttpResponseMessage response = await Client.GetAsync(url, token);
            if (!response.IsSuccessStatusCode) {
                LogOnce($"Session poll rejected with {(int)response.StatusCode} {response.StatusCode}. "
                    + $"Check {Base} and that this server's IP is allow-listed.", warning: true);
                return null;
            }

            string body = await response.Content.ReadAsStringAsync();
            if (string.IsNullOrWhiteSpace(body)) {
                LogOnce("Session poll answered with an empty body.", warning: true);
                return null;
            }

            LogOnce("Session polls are being answered.");
            return JsonConvert.DeserializeObject<PSSessionAnswer>(body);
        } catch (OperationCanceledException) {
            return null;
        } catch (Exception ex) {
            LogOnce($"Session poll failed: {ex.Message}", warning: true);
            return null;
        }
    }

    /// Queues a match's final result. Retried by the session loop until the site
    /// acknowledges it, because this call is the only thing that scores a match —
    /// unlike the snapshot and kill posts, there is no next call to paper over a lost
    /// one.
    internal static void QueueResult(int matchId, string json) {
        Outstanding = new PendingResult { MatchId = matchId, Json = json };
        Logger.Log($"Match {matchId} finished. Reporting its result.");
    }

    private static async Task ReportOutstandingResult(CancellationToken token) {
        PendingResult pending = Outstanding;
        if (pending == null) return;

        pending.Attempts++;
        try {
            HttpResponseMessage response = await Client.PostAsync(
                $"{Base}/matches/{pending.MatchId}/result", JsonBody(pending.Json), token);

            if (!response.IsSuccessStatusCode) {
                TaskDispatcher.QueueOnMainThread(() => Logger.LogError(
                    $"Match {pending.MatchId} result rejected with {(int)response.StatusCode} "
                    + $"{response.StatusCode} (attempt {pending.Attempts}). Retrying."));
                return;
            }

            // Cleared only on a real acknowledgement. "alreadyRecorded" counts: it means
            // an earlier attempt landed and only the reply was lost.
            Outstanding = null;
            TaskDispatcher.QueueOnMainThread(() => {
                Logger.Log($"Match {pending.MatchId} result accepted after {pending.Attempts} attempt(s).");
                PSManager.OnResultAcknowledged(pending.MatchId);
            });
        } catch (OperationCanceledException) {
            // Shutting down; the site's own housekeeping closes the match out.
        } catch (Exception ex) {
            TaskDispatcher.QueueOnMainThread(() => Logger.LogError(
                $"Match {pending.MatchId} result failed to send (attempt {pending.Attempts}): {ex.Message}. Retrying."));
        }
    }

    /// Fire-and-forget: each snapshot replaces the last, so a dropped one costs nothing
    /// and the next one covers for it.
    internal static void SendState(int matchId, PSUpdateState state) {
        Post($"{Base}/matches/{matchId}/state", JsonConvert.SerializeObject(state));
    }

    /// Fire-and-forget too, but for a different reason: kills are sent as they happen
    /// rather than batched at the end, so a server that dies mid-match only loses the
    /// one kill in flight rather than the whole log.
    internal static void SendKill(int matchId, PSKillEvent killEvent) {
        Post($"{Base}/matches/{matchId}/kills", JsonConvert.SerializeObject(killEvent));
    }

    private static void Post(string url, string json) {
        Task.Run(async () => {
            try {
                await Client.PostAsync(url, JsonBody(json));
            } catch (Exception ex) {
                TaskDispatcher.QueueOnMainThread(() => Logger.LogWarning($"POST {url} failed: {ex.Message}"));
            }
        });
    }

    private static StringContent JsonBody(string json) {
        return new StringContent(json, Encoding.UTF8, "application/json");
    }

    /// Testing mode stands in for the site entirely — polling a real one alongside it
    /// would let a live answer wipe the fixture's match state seconds after it is
    /// seeded.
    private static void LoadTestingMatch() {
        try {
            PSMatch match = JsonConvert.DeserializeObject<PSMatch>(File.ReadAllText("TestingJSON.json"));
            TaskDispatcher.QueueOnMainThread(() => PSManager.Apply(new PSSessionAnswer {
                Action = PSSessionAnswer.Host,
                MatchId = match.Id,
                Match = match
            }));
        } catch (Exception ex) {
            Logger.LogError($"Failed to load TestingJSON.json! {ex.Message}");
        }
    }

    private static string LastStatusLogged = string.Empty;

    /// The session loop runs every few seconds forever, so logging each pass would bury
    /// the console and hide the one line that matters. This reports only when the
    /// outcome changes, which is enough to see "401, this server's IP isn't
    /// allow-listed" and to watch it recover.
    private static void LogOnce(string message, bool warning = false) {
        if (LastStatusLogged == message) return;
        LastStatusLogged = message;

        TaskDispatcher.QueueOnMainThread(() => {
            if (warning) Logger.LogWarning(message);
            else Logger.Log(message);
        });
    }
}
