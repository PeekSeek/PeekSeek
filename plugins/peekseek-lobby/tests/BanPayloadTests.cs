using Newtonsoft.Json.Linq;
using PeekSeekLobby.Core.Web;
using Xunit;

namespace PeekSeekLobby.Tests;

/// The `ban` block exactly as `/server/v1/players/{steam64}` sends it.
///
/// Pinned for the same reason as the `choose` block, and with more at stake: a renamed
/// or retyped field here does not throw, it leaves `Ban` null, and a banned player is
/// simply never removed from the arena. That is the failure this whole field exists to
/// stop — a permanently banned account queued from in-game for six weeks because no
/// part of the game path had ever been told about bans. `test_the_game_server_is_told_why`
/// holds the site's end of it.
public class BanPayloadTests {
    private const string Banned = """
        {
          "steam64": "76561198000000001",
          "elo": 300,
          "matchId": null,
          "relayTo": null,
          "choose": null,
          "ban": { "reason": "Cheating", "expires": 9999999999, "permanent": true }
        }
        """;

    private const string Clean = """
        {
          "steam64": "76561198000000001",
          "elo": 300,
          "matchId": null,
          "relayTo": null,
          "choose": null,
          "ban": null
        }
        """;

    private static RemoteAPI.BanInfo Ban(string body) => RemoteAPI.Ban(JObject.Parse(body)["ban"]);

    [Fact]
    public void ABannedPlayerCarriesTheirReason() {
        RemoteAPI.BanInfo ban = Ban(Banned);

        Assert.NotNull(ban);
        Assert.Equal("Cheating", ban.Reason);
        Assert.True(ban.Permanent);
    }

    [Fact]
    public void APlayerWhoMayPlayHasNoBan() {
        Assert.Null(Ban(Clean));
    }

    /// A build of the site that predates the field at all. Older servers must keep
    /// working, so a missing key reads as "not banned" rather than throwing.
    [Fact]
    public void AResponseWithoutTheFieldIsNotABan() {
        Assert.Null(RemoteAPI.Ban(JObject.Parse("""{ "elo": 300 }""")["ban"]));
    }

    /// A temporary ban is still a ban; only the wording of the kick differs.
    [Fact]
    public void ATemporaryBanIsNotPermanent() {
        RemoteAPI.BanInfo ban = RemoteAPI.Ban(
            JObject.Parse("""{ "ban": { "reason": "Toxicity", "expires": 1788000000, "permanent": false } }""")["ban"]);

        Assert.NotNull(ban);
        Assert.False(ban.Permanent);
    }
}
