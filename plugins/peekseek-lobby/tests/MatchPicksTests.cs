using PeekSeekLobby.Core.Models;
using Xunit;

namespace PeekSeekLobby.Tests;

/// The loadout prompt's rules. Everything here is about the gap between a match being
/// made and everyone on it being teleported into it: the site cannot hand a match to a
/// server until every player has chosen, so what counts as "chosen" and what counts as
/// "nothing to choose" is the whole of how long anybody stands around waiting.
public class MatchPicksTests {
    private static MatchPicks Competitive() {
        MatchPicks picks = new();
        picks.Kits.AddRange(["meta", "sniper"]);
        picks.Powerups.AddRange(["Movement", "Ammo", "Boom"]);
        return picks;
    }

    /// A gamemode offering one option is not offering a choice. The site resolves those
    /// itself and sends the list empty, and prompting for them would be asking somebody
    /// to confirm the only thing that could happen.
    [Fact]
    public void OneOptionIsNotAChoice() {
        MatchPicks picks = new();
        picks.Kits.Add("meta");

        Assert.False(picks.NeedsKit);
        Assert.False(picks.NeedsPowerup);
        Assert.False(picks.Outstanding);
    }

    [Fact]
    public void BothHalvesAreOutstandingUntilBothAreAnswered() {
        MatchPicks picks = Competitive();
        Assert.True(picks.Outstanding);

        picks.Kit = "sniper";
        Assert.False(picks.NeedsKit);
        Assert.True(picks.Outstanding);

        picks.Powerup = "Boom";
        Assert.False(picks.Outstanding);
    }

    [Fact]
    public void OptionsAreAnsweredByTheirNumberInThePrompt() {
        MatchPicks picks = Competitive();

        Assert.Equal("meta", picks.OptionFor(powerup: false, "1"));
        Assert.Equal("sniper", picks.OptionFor(powerup: false, "2"));
        Assert.Equal("Boom", picks.OptionFor(powerup: true, "3"));
    }

    /// Somebody who reads "sniper" types "sniper", and nobody types capital letters
    /// while a match is waiting on them.
    [Fact]
    public void OptionsAreAlsoAnsweredByName() {
        MatchPicks picks = Competitive();

        Assert.Equal("sniper", picks.OptionFor(powerup: false, "SNIPER"));
        Assert.Equal("Movement", picks.OptionFor(powerup: true, "movement"));
    }

    /// A miss has to be distinguishable from a hit, because the caller lists the
    /// options again on a miss rather than silently doing nothing.
    [Fact]
    public void AnythingElseIsNoAnswerAtAll() {
        MatchPicks picks = Competitive();

        Assert.Null(picks.OptionFor(powerup: false, "0"));
        Assert.Null(picks.OptionFor(powerup: false, "3"));
        Assert.Null(picks.OptionFor(powerup: false, "-1"));
        Assert.Null(picks.OptionFor(powerup: false, "rocket"));
        Assert.Null(picks.OptionFor(powerup: false, ""));
        Assert.Null(picks.OptionFor(powerup: false, null));
    }

    /// The two lists are answered separately, so a powerup's number never reaches the
    /// kit list and a kit name never counts as a powerup.
    [Fact]
    public void TheTwoListsDoNotAnswerForEachOther() {
        MatchPicks picks = Competitive();

        Assert.Null(picks.OptionFor(powerup: true, "sniper"));
        Assert.Null(picks.OptionFor(powerup: false, "Boom"));
    }
}
