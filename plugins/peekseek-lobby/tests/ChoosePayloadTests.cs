using Newtonsoft.Json.Linq;
using PeekSeekLobby.Core.Models;
using PeekSeekLobby.Core.Web;
using Xunit;

namespace PeekSeekLobby.Tests;

/// The `choose` block exactly as `/server/v1/players/{steam64}` sends it.
///
/// Pinned here because this is the one seam that fails silently: a renamed or retyped
/// field does not throw, it just leaves every matched player with no loadout prompt and
/// every match waiting out selectKitsTime, which looks like the site being slow rather
/// than like a parsing bug. The literals below are the site's own response shape, and
/// `test_a_matched_player_is_told_what_is_left_to_choose` holds the other end of it.
public class ChoosePayloadTests {
    private const string Matched = """
        {
          "steam64": "76561198000000001",
          "elo": 1000,
          "matchId": 7306,
          "relayTo": null,
          "choose": {
            "kits": ["meta", "sniper"],
            "powerups": ["Movement", "Ammo", "Boom", "Doctor", "Trapper"],
            "kit": null,
            "powerup": null
          }
        }
        """;

    private static JToken Choose(string body) => JObject.Parse(body)["choose"];

    [Fact]
    public void AMatchedPlayerGetsBothMenus() {
        MatchPicks picks = RemoteAPI.Choices(Choose(Matched));

        Assert.Equal(["meta", "sniper"], picks.Kits);
        Assert.Equal(["Movement", "Ammo", "Boom", "Doctor", "Trapper"], picks.Powerups);
        Assert.True(picks.NeedsKit);
        Assert.True(picks.NeedsPowerup);
    }

    /// A choice already made comes back as the value, not as an absent field, so the
    /// prompt can show what they picked instead of asking again.
    [Fact]
    public void AChoiceAlreadyMadeComesBackAsItself() {
        MatchPicks picks = RemoteAPI.Choices(Choose("""
            {"choose": {"kits": ["meta", "sniper"], "powerups": ["Ammo", "Boom"],
                        "kit": "sniper", "powerup": null}}
            """));

        Assert.Equal("sniper", picks.Kit);
        Assert.False(picks.NeedsKit);
        Assert.True(picks.NeedsPowerup);
    }

    /// Null is what the site sends for a player with no match, and for a gamemode that
    /// leaves them nothing to decide. Both mean the same thing to the lobby: no prompt.
    [Fact]
    public void NothingToChooseIsNullRatherThanAnEmptyBlock() {
        Assert.Null(RemoteAPI.Choices(Choose("""
            {"steam64": "76561198000000001", "matchId": null, "choose": null}
            """)));
        Assert.Null(RemoteAPI.Choices(null));
    }

    /// A build of the site that predates `choose` answers without the field at all.
    /// That has to read as "no prompt" rather than throwing inside the refresh loop,
    /// which runs for every player every few seconds.
    [Fact]
    public void AnOlderSiteWithoutTheFieldIsNotAnError() {
        Assert.Null(RemoteAPI.Choices(Choose("""
            {"steam64": "76561198000000001", "elo": 1000, "matchId": null, "relayTo": null}
            """)));
    }
}
