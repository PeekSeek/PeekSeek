using System.Collections.Generic;
using PeekSeekLobby.Core.Models;
using Xunit;

namespace PeekSeekLobby.Tests;

/// The rule that keeps the arena populated enough to be worth joining from the
/// browser while still letting matches happen. Pure arithmetic on wait times, so
/// every case is checkable without a server.
public class QueueHoldTests {
    private const float Now = 1000f;
    private const float NoRelease = 0f;

    private static HashSet<ulong> Offered(
        Dictionary<ulong, float> waiting, int population, int floor = 2,
        float maxHold = NoRelease, float now = Now) {
        return QueueHold.Offered(waiting, population, floor, maxHold, now);
    }

    [Fact]
    public void NobodyIsOfferedWhenTheLobbyIsOnTheFloor() {
        Dictionary<ulong, float> waiting = new() { [1] = Now, [2] = Now };

        Assert.Empty(Offered(waiting, population: 2));
    }

    /// Four in the lobby can start one 1v1 and still show two, which is the whole
    /// point of the floor.
    [Fact]
    public void OnlyTheSurplusAboveTheFloorIsOffered() {
        Dictionary<ulong, float> waiting = new() { [1] = 10f, [2] = 20f, [3] = 30f, [4] = 40f };

        HashSet<ulong> offered = Offered(waiting, population: 4);

        Assert.Equal(2, offered.Count);
        Assert.Contains(1UL, offered);
        Assert.Contains(2UL, offered);
    }

    [Fact]
    public void TheLongestWaitIsOfferedFirst() {
        Dictionary<ulong, float> waiting = new() { [1] = 90f, [2] = 10f, [3] = 50f };

        HashSet<ulong> offered = Offered(waiting, population: 3);

        Assert.Equal([2UL], offered);
    }

    [Fact]
    public void EveryoneIsOfferedOnceTheLobbyIsComfortablyFull() {
        Dictionary<ulong, float> waiting = new() { [1] = 10f, [2] = 20f, [3] = 30f };

        Assert.Equal(3, Offered(waiting, population: 9).Count);
    }

    /// A floor of zero is how the hold is switched off, and has to mean everybody
    /// queues the moment they are eligible.
    [Fact]
    public void AFloorOfZeroHoldsNobody() {
        Dictionary<ulong, float> waiting = new() { [1] = 10f, [2] = 20f };

        Assert.Equal(2, Offered(waiting, population: 2, floor: 0).Count);
    }

    /// Without this a lobby resting exactly on the floor never plays a match, and
    /// the people held there to keep the window lit are the ones who leave.
    [Fact]
    public void WaitingLongEnoughReleasesAPlayerAnyway() {
        Dictionary<ulong, float> waiting = new() { [1] = Now - 300f, [2] = Now - 10f };

        HashSet<ulong> offered = Offered(waiting, population: 2, maxHold: 240f);

        Assert.Equal([1UL], offered);
    }

    [Fact]
    public void AReleaseOfZeroNeverFires() {
        Dictionary<ulong, float> waiting = new() { [1] = 0f };

        Assert.Empty(Offered(waiting, population: 2, maxHold: NoRelease));
    }

    [Fact]
    public void AnEmptyQueueOffersNobody() {
        Assert.Empty(Offered([], population: 6));
    }
}
