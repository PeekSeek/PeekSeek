using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
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
        Avatars.Clear();
    }

    // ---- players --------------------------------------------------------------

    /// Each player's Steam picture, for the bar. Fetched when a player joins and held
    /// until they leave, because a match starts by drawing the bar right now and is
    /// not going to wait on an HTTP round trip.
    private static readonly Dictionary<ulong, string> Avatars = [];

    internal static string AvatarFor(ulong steam64) {
        return Avatars.TryGetValue(steam64, out string avatar) ? avatar : PeekSeek.Config.DefaultAvatarURL;
    }

    internal static void Forget(ulong steam64) {
        Avatars.Remove(steam64);
    }

    internal static void FetchPlayer(ulong steam64) {
        if (Client == null) return;

        Task.Run(async () => {
            string avatar = await ReadAvatar(steam64) ?? await ReadAvatarFromSteam(steam64);
            if (avatar != null) TaskDispatcher.QueueOnMainThread(() => Avatars[steam64] = avatar);
        });
    }

    /// `GET /players/{steam64}` carries `avatar` alongside the rank and whereabouts a
    /// lobby reads off it, from the same cache every page of the site uses.
    private static async Task<string> ReadAvatar(ulong steam64) {
        try {
            HttpResponseMessage response = await Client.GetAsync($"{Base}/players/{steam64}");
            if (!response.IsSuccessStatusCode) return null;

            JObject body = JObject.Parse(await response.Content.ReadAsStringAsync());
            string avatar = (string)body["avatar"];
            return string.IsNullOrEmpty(avatar) ? null : avatar;
        } catch (Exception ex) {
            TaskDispatcher.QueueOnMainThread(() => Logger.LogWarning($"Failed to fetch the profile of {steam64}: {ex.Message}"));
            return null;
        }
    }

    /// Steam's own answer, for a site that does not carry the picture yet: the public
    /// profile as XML, which needs no key and answers for a private profile too.
    private static async Task<string> ReadAvatarFromSteam(ulong steam64) {
        try {
            string xml = await Client.GetStringAsync($"https://steamcommunity.com/profiles/{steam64}?xml=1");
            Match full = Regex.Match(xml, @"<avatarFull><!\[CDATA\[(.*?)\]\]></avatarFull>");
            return full.Success ? full.Groups[1].Value : null;
        } catch (Exception ex) {
            TaskDispatcher.QueueOnMainThread(() => Logger.LogWarning($"Failed to fetch the Steam picture of {steam64}: {ex.Message}"));
            return null;
        }
    }

    // ---- the session --------------------------------------------------------------

    /// The code players paste into Unturned's connect box — the game server's Steam ID,
    /// the same value the in-game "Copy Server Code" button copies. Provider.server is
    /// the wrong field for this (reads back as 0 on a dedicated server — it's meant for
    /// a client's view of the server it's connected to, not the server's own identity);
    /// SteamGameServer.GetSteamID() is what Unturned's own "Copy Server Code" command
    /// actually calls.
    private static string ServerCode => SteamGameServer.GetSteamID().ToString();

    /// Everything one /report carries, gathered on the game thread before the upload
    /// leaves it. Every field here reads live match state that is not safe to touch
    /// from the background task that does the sending.
    internal class Report {
        internal ulong Reported;
        internal ulong Reporter;
        internal string Reason;
        internal int DamageDealt;
        internal int ShotsFired;
        internal int ShotsHit;
        internal int HeadshotHits;
        internal byte[] Screenshot;
    }

    /// Send one report, with the reported player's spy screenshot attached.
    ///
    /// Multipart because the screenshot is the point of it: the round it is evidence of
    /// is usually over by the time anyone reads the report, so it has to arrive with
    /// the report rather than as a second call that can fail on its own.
    ///
    /// Deliberately outside the session loop. That loop is what keeps a match alive and
    /// must not be held up by an upload, and a report that failed to send must not be
    /// retried forever the way a match result is: it is one moment somebody complained
    /// about, not a result the site is waiting on.
    internal static void SendReport(Report report) {
        Task.Run(async () => {
            try {
                MultipartFormDataContent form = new() {
                    { new StringContent(report.Reported.ToString()), "reported" },
                    { new StringContent(report.Reporter.ToString()), "reporter" },
                    { new StringContent(report.Reason ?? ""), "reason" },
                    { new StringContent(ServerCode), "serverCode" },
                    { new StringContent("a live match"), "where" },
                    { new StringContent(report.DamageDealt.ToString()), "damageDealt" },
                    { new StringContent(report.ShotsFired.ToString()), "shotsFired" },
                    { new StringContent(report.ShotsHit.ToString()), "shotsHit" },
                    { new StringContent(report.HeadshotHits.ToString()), "headshotHits" }
                };

                if (report.Screenshot != null) {
                    ByteArrayContent image = new(report.Screenshot);
                    image.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
                    form.Add(image, "screenshot", "spy.jpg");
                }

                HttpResponseMessage response = await Client.PostAsync($"{Base}/reports", form);
                if (!response.IsSuccessStatusCode)
                    Logger.LogWarning($"Report for {report.Reported} refused: {response.StatusCode}");
            } catch (Exception ex) {
                Logger.LogWarning($"Report for {report.Reported} failed to send: {ex.Message}");
            }
        });
    }

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

            // Draining still reports a result it owes, but stops asking for work.
            // Asking is the only way a match is ever handed over — matchmaking runs
            // from this poll — so not asking is the whole of the claim being given
            // up, and there is no window in which the site can assign to a server
            // that has already decided to go.
            PSSessionAnswer answer = Draining ? null : await Poll(token);
            if (answer != null) {
                TaskDispatcher.QueueOnMainThread(() => PSManager.Apply(answer));
            }

            await Task.Delay(answer == null ? BackoffMs : PollIntervalMs, token);
        }
    }

    private static volatile bool Draining;

    /// Stop asking the site for matches, so this server can be taken down without
    /// stranding one. Reversible: a server that finds itself holding a match after
    /// all goes straight back to asking.
    internal static void StopAskingForWork() {
        Draining = true;
    }

    internal static void ResumeAskingForWork() {
        Draining = false;
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

    /// Lock a pick a player made on the board here, the same door the site's own
    /// match page uses. Either half may be null. Fire-and-forget: the next session
    /// poll carries the site's view of who has picked, which is the one that counts.
    internal static void Decide(ulong steam64, string kit, string powerup) {
        string query = string.Join("&", new[] {
            kit == null ? null : $"kit={Uri.EscapeDataString(kit)}",
            powerup == null ? null : $"powerup={Uri.EscapeDataString(powerup)}"
        }.Where(part => part != null));
        Post($"{Base}/players/{steam64}/decide?{query}", "{}");
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
