using PeekSeekLobby.Core;
using Xunit;

namespace PeekSeekLobby.Tests;

/// What a player's own state says between being given a match and being on it.
///
/// This path decides whether somebody plays again today. Two in five matches end with
/// nobody having turned up, and the player they were built out of is usually still
/// standing in this lobby when that happens, so "the match went away" is an ordinary
/// ending rather than an edge case. Plain arithmetic on PlayerState, so every case is
/// checkable without a server.
public class MatchHandoverTests {
    private const float Now = 1000f;
    private const string Server = "90142322901234567";
    private const int NoMatch = 0;

    private static LobbyManager.PlayerState Matched(int matchId = 6953) {
        return new LobbyManager.PlayerState { Queueing = true, MatchId = matchId };
    }

    [Fact]
    public void APlayerHoldingAMatchIsNotOfferedAnother() {
        Assert.False(Matched().WantsAMatch(Now));
    }

    /// Holding a match is already what keeps somebody out of the queue, so switching
    /// Queueing off as well changed nothing while the match lasted and threw away the
    /// one thing needed to put them back when it did not.
    [Fact]
    public void BeingGivenAMatchDoesNotThrowAwayTheirQueueChoice() {
        Assert.True(Matched().Queueing);
    }

    [Fact]
    public void AMatchTheSiteNoLongerReportsHasBeenLost() {
        Assert.True(Matched().LostTheirMatch(NoMatch));
    }

    [Fact]
    public void AMatchTheSiteStillReportsHasNot() {
        Assert.False(Matched().LostTheirMatch(6953));
    }

    /// Nothing to lose, and nothing to put back. Everyone in the arena is refreshed on
    /// the same timer, so this is the answer for almost every player on almost every
    /// pass and it must stay silent.
    [Fact]
    public void APlayerWithNoMatchHasLostNothing() {
        Assert.False(new LobbyManager.PlayerState().LostTheirMatch(NoMatch));
    }

    [Fact]
    public void ClearingALostMatchPutsThemStraightBackInTheQueue() {
        LobbyManager.PlayerState state = Matched();

        state.MatchId = 0;

        Assert.True(state.WantsAMatch(Now));
    }

    /// Somebody who opted out with /noqueue and was then pulled into a match made on the
    /// site stays opted out when that match falls through. The lobby puts them back
    /// where they were; it does not decide for them.
    [Fact]
    public void AMatchFallingThroughDoesNotOptSomebodyBackIn() {
        LobbyManager.PlayerState state = Matched();
        state.Queueing = false;

        state.MatchId = 0;

        Assert.False(state.WantsAMatch(Now));
    }

    [Fact]
    public void AMatchedPlayerIsSentToTheServerHoldingTheirMatch() {
        Assert.True(Matched().ShouldRelayTo(Server));
    }

    /// The site answers with the same code for as long as the match is live, and a relay
    /// the client refuses leaves the player standing here. Sent twice, they are invited
    /// again every few seconds and told about it in chat each time.
    [Fact]
    public void TheSameServerIsNeverOfferedTwice() {
        LobbyManager.PlayerState state = Matched();
        state.RelayedTo = Server;

        Assert.False(state.ShouldRelayTo(Server));
    }

    /// A server that was ready before the countdown finished still waits for it, or
    /// being warned would buy the player nothing.
    [Fact]
    public void NobodyIsRelayedOutOfTheMiddleOfTheirCountdown() {
        LobbyManager.PlayerState state = Matched();
        state.PullAt = Now + 3f;

        Assert.False(state.ShouldRelayTo(Server));
    }

    [Fact]
    public void APlayerWithNoMatchIsNeverRelayed() {
        Assert.False(new LobbyManager.PlayerState().ShouldRelayTo(Server));
    }

    /// A player put back into the arena and matched again has to be relayable to the
    /// same server as last time, the free server that takes their next match is very
    /// often the one that just gave up on their last one.
    [Fact]
    public void AFreshMatchCanGoBackToTheServerThatLostTheLastOne() {
        LobbyManager.PlayerState state = Matched();
        state.RelayedTo = Server;

        state.MatchId = 0;
        state.RelayedTo = null;
        state.MatchId = 6954;

        Assert.True(state.ShouldRelayTo(Server));
    }
}
