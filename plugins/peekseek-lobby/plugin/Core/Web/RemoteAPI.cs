using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using PeekSeekLobby.Core.Models;
using Rocket.Core.Logging;
using Rocket.Core.Utils;
using Steamworks;

namespace PeekSeekLobby.Core.Web;

/// The lobby's side of the `/server/v1/*` contract.
///
/// One call per player per tick answers everything the lobby needs to know about them:
/// their rank, whether they have a match, and whether that match is ready to be sent
/// to. It used to take three — queue, then poll for a match, then poll the overlay for
/// a server code — and each of those was a separate chance to leave somebody standing
/// in the arena while their match waited for them.
internal static class RemoteAPI {
    private static string Base => PeekSeekLobby.Config.szAPIURL.TrimEnd('/') + "/server/v1";

    /// This server's own connect code, which is its Steam ID. Read at runtime rather
    /// than configured, so it cannot be set wrong and does not have to be set at all
    /// on a server somebody just stood up.
    private static string ServerCode => SteamGameServer.GetSteamID().ToString();

    private static HttpClient Client;

    internal static void Init() {
        Client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        Client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    internal static void Shutdown() {
        Client?.Dispose();
        Client = null;
    }

    /// Why a player may not play, as the site reports it.
    internal class BanInfo {
        internal string Reason;
        internal bool Permanent;
    }

    /// Everything the lobby acts on for one player, as the site sees them.
    internal class PlayerState {
        internal int Elo;
        internal int MatchId;

        /// The ban keeping this player out, or null when they may play. Carried on
        /// every player read rather than only on the queue call, so a ban that lands
        /// while somebody is already stood in the arena reaches the lobby on their
        /// next refresh instead of waiting for them to try to queue.
        internal BanInfo Ban;

        /// The server code to hand this player over to, or null while there is nowhere
        /// to send them yet. Non-null is the site's promise that the match is being
        /// hosted right now and this player will be let in.
        internal string RelayTo;

        /// The badge the rank their Elo earns wears, for the board, or null from a
        /// site that does not say.
        internal string RankIcon;
    }

    /// Queue this player and read back where they stand. Both jobs in one call because
    /// they are one question: a player standing in the lobby wants a match, and the
    /// answer is either "not yet" or "here is where to go".
    internal static void RefreshQueued(ulong steam64, ushort gamemodes, string region) {
        Send(HttpMethod.Put,
            $"{Base}/players/{steam64}/queue?gamemodes={gamemodes}&region={Uri.EscapeDataString(region)}",
            steam64);
    }

    /// The same read, without queueing them. That side-effect-free version is the whole
    /// reason it exists: it is what lets the lobby pull someone who used /noqueue out of
    /// the arena when a match reaches them from the site or from their party, without
    /// quietly undoing their choice.
    internal static void Refresh(ulong steam64) {
        Send(HttpMethod.Get, $"{Base}/players/{steam64}", steam64);
    }

    /// A player's Steam picture, for the bar: the site's copy if it has one, else
    /// Steam's own public profile as XML, which needs no key. Asked once, on join.
    internal static void FetchAvatar(ulong steam64, System.Action<string> found) {
        Task.Run(async () => {
            try {
                string url = null;
                HttpResponseMessage response = await Client.GetAsync($"{Base}/players/{steam64}");
                if (response.IsSuccessStatusCode) url = Text((await ReadJson(response))?["avatar"]);

                if (string.IsNullOrEmpty(url)) {
                    string xml = await Client.GetStringAsync($"https://steamcommunity.com/profiles/{steam64}?xml=1");
                    System.Text.RegularExpressions.Match full = System.Text.RegularExpressions.Regex.Match(
                        xml, @"<avatarFull><!\[CDATA\[(.*?)\]\]></avatarFull>");
                    url = full.Success ? full.Groups[1].Value : null;
                }

                if (!string.IsNullOrEmpty(url)) TaskDispatcher.QueueOnMainThread(() => found(url));
            } catch (Exception ex) {
                ReportFailure(ex.Message);
            }
        });
    }

    /// Who already has a refresh out. The lobby asks again for every player every few
    /// seconds regardless of what happened to the last ask, so a site that has gone
    /// slow — or a request sitting out its ten second timeout — would otherwise leave
    /// a growing pile of identical requests per player, each holding a socket and
    /// each landing on the same state moments after the one before it.
    ///
    /// Skipping is safe: whichever request is still out refreshes the queue heartbeat
    /// when it lands, and the queue only forgets somebody after a full minute.
    private static readonly HashSet<ulong> InFlight = [];

    private static void Send(HttpMethod method, string url, ulong steam64) {
        lock (InFlight) {
            if (!InFlight.Add(steam64)) return;
        }

        Task.Run(async () => {
            try {
                HttpResponseMessage response = await Client.SendAsync(new HttpRequestMessage(method, url));
                ReportStatus(response.StatusCode);
                if (!response.IsSuccessStatusCode) return;

                JObject body = await ReadJson(response);
                if (body == null) return;

                PlayerState state = new() {
                    Elo = Number(body["elo"]) ?? 0,
                    MatchId = Number(body["matchId"]) ?? 0,
                    RelayTo = Text(body["relayTo"]),
                    RankIcon = Text(body["rankIcon"]),
                    Ban = ParseBan(body["ban"])
                };
                bool eloKnown = body["elo"] != null && body["elo"].Type != JTokenType.Null;

                TaskDispatcher.QueueOnMainThread(() => LobbyManager.ApplyPlayerState(steam64, state, eloKnown));
            } catch (Exception ex) {
                ReportFailure(ex.Message);
            } finally {
                lock (InFlight) {
                    InFlight.Remove(steam64);
                }
            }
        });
    }

    /// The site's `ban` block, or null when this player may play.
    ///
    /// Internal so a test can hold the seam where a renamed field would silently stop
    /// a banned player being removed, and nothing else in the lobby would look wrong.
    internal static BanInfo ParseBan(JToken token) {
        if (token == null || token.Type != JTokenType.Object) return null;

        return new BanInfo {
            Reason = Text(token["reason"]),
            Permanent = token["permanent"] != null && token["permanent"].Type == JTokenType.Boolean
                && token["permanent"].Value<bool>()
        };
    }

    /// Everything one /report carries, gathered on the game thread before the upload
    /// leaves it. Every field here reads live game state that is not safe to touch
    /// from the background task that does the sending.
    internal class Report {
        internal ulong Reported;
        internal ulong Reporter;
        internal string Reason;
        internal string Where;
        internal int DamageDealt;
        internal int ShotsFired;
        internal int ShotsHit;
        internal int HeadshotHits;
        internal byte[] Screenshot;
    }

    /// Send one report, with the reported player's spy screenshot attached.
    ///
    /// Multipart because the screenshot is the point of it: whatever it is evidence of
    /// is usually over by the time anyone reads the report, so it has to arrive with
    /// the report rather than as a second call that can fail on its own.
    ///
    /// Deliberately outside the InFlight deduplication the player refreshes use. That
    /// exists to stop a slow site piling up identical polls; a report is not a poll,
    /// there is only ever one per command, and dropping it because a refresh happened
    /// to be out would silently lose the evidence.
    internal static void SendReport(Report report) {
        Task.Run(async () => {
            try {
                MultipartFormDataContent form = new() {
                    { new StringContent(report.Reported.ToString()), "reported" },
                    { new StringContent(report.Reporter.ToString()), "reporter" },
                    { new StringContent(report.Reason ?? ""), "reason" },
                    { new StringContent(ServerCode), "serverCode" },
                    { new StringContent(report.Where ?? "the warm-up lobby"), "where" },
                    { new StringContent(PeekSeekLobby.Config.szReportChannel ?? ""), "channel" },
                    { new StringContent(PeekSeekLobby.Config.szStaffRole ?? ""), "staffRole" },
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

    private static async Task<JObject> ReadJson(HttpResponseMessage response) {
        if (response.Content.Headers.ContentType?.MediaType != "application/json") return null;
        return JObject.Parse(await response.Content.ReadAsStringAsync());
    }

    private static int? Number(JToken token) {
        return token == null || token.Type == JTokenType.Null ? null : (int)token;
    }

    private static string Text(JToken token) {
        return token == null || token.Type == JTokenType.Null ? null : token.ToString();
    }

    /// What this arena looks like right now: how many are in it, and how many of those
    /// want a match.
    ///
    /// One call for the whole server rather than one per player, because all three are
    /// facts about the server. The queue cannot stand in for the first of them: this
    /// lobby holds players back from queueing to keep its own listing worth joining, so
    /// a full arena can honestly have nobody queueing in it.
    ///
    /// The other two are sent because that hold is otherwise invisible. A lobby that
    /// reports only its population looks identical whether it is three people happily
    /// fighting or three people who all asked for a match and are being made to wait,
    /// and those are the two cases anything trying to raise matches per day has to tell
    /// apart.
    ///
    /// Players holding a match are left out of the population because the site counts
    /// every roster it handed out by itself. Somebody standing in the arena picking
    /// their kit is on both lists, and sending them here too would count them twice in
    /// one total.
    ///
    /// Fire and forget — nothing here reads the answer, and a reading that misses a
    /// beat is a counter that is a few seconds stale rather than a lobby that stops.
    internal static void ReportPopulation(int players, int queueing) {
        Task.Run(async () => {
            try {
                StringContent body = new(
                    $"{{\"players\":{players},\"queueing\":{queueing}}}",
                    Encoding.UTF8, "application/json");
                await Client.PostAsync($"{Base}/lobby/population", body);
            } catch (Exception ex) {
                ReportFailure(ex.Message);
            }
        });
    }

    internal static void LeaveQueue(ulong steam64) {
        Task.Run(async () => {
            try {
                await Client.DeleteAsync($"{Base}/players/{steam64}/queue");
            } catch (Exception ex) {
                Logger.LogError($"Failed to dequeue {steam64}: {ex.Message}");
            }
        });
    }

    /// The player's saved kit for one category, resolved into ready-to-equip items.
    internal static async Task<KitLoadout> FetchKit(ulong steam64, string category) {
        try {
            HttpResponseMessage response = await Client.GetAsync($"{Base}/players/{steam64}/kits");
            if (!response.IsSuccessStatusCode) return null;

            JObject body = await ReadJson(response);
            if (body?[category] is not JObject kit || kit["items"] is not JArray items) return null;

            return new KitLoadout {
                Items = PSItem.ParseArray(items),
                Magazine = kit["magazine"] is JObject magazine ? PSItem.Parse(magazine) : null
            };
        } catch (Exception ex) {
            Logger.LogError($"Failed to fetch kit for {steam64}: {ex.Message}");
            return null;
        }
    }

    private static HttpStatusCode? LastStatus;
    private static string LastFailure;

    /// Every connected player refreshes every few seconds, so logging each result would
    /// bury the console. This reports only when the outcome changes, which is enough to
    /// see "401, this server's IP isn't allow-listed" and to watch it recover.
    private static void ReportStatus(HttpStatusCode status) {
        if (LastStatus == status) return;
        LastStatus = status;
        LastFailure = null;

        TaskDispatcher.QueueOnMainThread(() => {
            if (status == HttpStatusCode.OK) {
                Logger.Log($"Talking to {Base} — players are being queued and ranked.");
                return;
            }
            Logger.LogWarning(
                $"{Base} is answering {(int)status} {status} — players will not be matched, "
                + "and rank will not show. Check the URL and that this server's IP is allow-listed.");
        });
    }

    private static void ReportFailure(string message) {
        if (LastFailure == message) return;
        LastFailure = message;
        LastStatus = null;

        TaskDispatcher.QueueOnMainThread(() =>
            Logger.LogWarning($"Could not reach {Base}: {message}. Players will not be matched."));
    }
}
