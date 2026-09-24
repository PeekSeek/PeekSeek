using PeekSeekLobby.Core;
using Xunit;

namespace PeekSeekLobby.Tests;

/// Nobody is in the queue until they say so, and somebody who says so on arrival is
/// still not offered a match from the instant they connect. The gap is plain
/// arithmetic on PlayerState, so every case is checkable without a server.
public class QueueGraceTests {
    private const float Grace = 10f;

    private static LobbyManager.PlayerState Joined(float at = 100f) {
        return new LobbyManager.PlayerState { Queueing = true, QueueFrom = at + Grace };
    }

    [Fact]
    public void ANewPlayerIsNotQueueingUntilTheySaySo() {
        LobbyManager.PlayerState state = new() { QueueFrom = 110f };

        Assert.False(state.Queueing);
        Assert.False(state.WantsAMatch(999f));
    }

    [Fact]
    public void AQueueingPlayerIsNotYetOfferedWhileArriving() {
        Assert.False(Joined().WantsAMatch(100f));
    }

    [Theory]
    [InlineData(100f)]  // the instant they connected
    [InlineData(105f)]
    [InlineData(109.9f)]
    public void NotOfferedAMatchWhileStillArriving(float now) {
        Assert.False(Joined().WantsAMatch(now));
    }

    [Fact]
    public void OfferedAMatchOnceTheGraceHasPassed() {
        Assert.True(Joined().WantsAMatch(110f));
    }

    /// The grace is about arriving, not about opting in. Someone who typed the command
    /// has plainly arrived, so ToggleQueueing clears it and they queue on the next tick.
    [Fact]
    public void AskingForTheQueueByNameSkipsTheGrace() {
        LobbyManager.PlayerState state = Joined();
        state.QueueFrom = 0;

        Assert.True(state.WantsAMatch(100f));
    }

    [Fact]
    public void OptingOutIsStillHonouredAfterTheGrace() {
        LobbyManager.PlayerState state = Joined();
        state.Queueing = false;

        Assert.False(state.WantsAMatch(999f));
    }

    /// Someone already holding a match must never be queued for a second one, however
    /// long they have been connected.
    [Fact]
    public void APlayerWithAMatchIsNeverQueuedAgain() {
        LobbyManager.PlayerState state = Joined();
        state.MatchId = 6953;

        Assert.False(state.WantsAMatch(999f));
    }
}
