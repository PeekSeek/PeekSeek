using PeekSeekLobby.Core;
using Xunit;

namespace PeekSeekLobby.Tests;

/// The timing rule behind spawn protection, which has now been wrong twice: once
/// leaving players permanently invincible, once resetting the respawn wait faster
/// than it ran down. Both halves live on PlayerState as plain arithmetic over a
/// caller-supplied `now`, so every scenario below is checkable without a server.
public class SpawnProtectionTests {
    private const float Delay = 10f;      // RespawnDelaySeconds
    private const float Protection = 5f;  // SpawnProtectionSeconds

    private static LobbyManager.PlayerState Died(float at = 100f) {
        LobbyManager.PlayerState state = new();
        state.ScheduleDeploy(at, Delay, Protection);
        return state;
    }

    [Fact]
    public void ScheduleDeploy_BooksTheDeployAfterTheDelay() {
        Assert.Equal(110f, Died().DeployAt);
    }

    [Fact]
    public void ScheduleDeploy_CoversTheWaitAndTheLandingInOneTimer() {
        // Protected for the whole wait at the standard spawn, and for the
        // protection window again once the deploy has put them in the arena.
        Assert.Equal(115f, Died().SpawnProtectedUntil);
    }

    [Theory]
    [InlineData(100f)]  // the instant of death
    [InlineData(105f)]  // mid-wait, stood at the standard spawn
    [InlineData(110f)]  // the deploy itself
    [InlineData(114.9f)] // the last moment of cover in the arena
    public void ProtectedForEveryMomentBetweenDyingAndTheCoverRunningOut(float now) {
        Assert.True(Died().IsProtectedAt(now));
    }

    [Fact]
    public void NotProtectedOnceTheCoverHasRunOut() {
        Assert.False(Died().IsProtectedAt(115f));
    }

    /// The regression that mattered: OnLifeUpdated fires for as long as a player is
    /// dead and RespawnAnyoneStillDead sweeps in behind it every second, so this is
    /// called many times per death. Only the first may book anything.
    [Fact]
    public void ScheduleDeploy_IsRefusedWhileADeployIsAlreadyPending() {
        LobbyManager.PlayerState state = Died(100f);

        Assert.False(state.ScheduleDeploy(103f, Delay, Protection));
        Assert.Equal(110f, state.DeployAt);
        Assert.Equal(115f, state.SpawnProtectedUntil);
    }

    [Fact]
    public void ScheduleDeploy_BooksAgainOnceTheDeployHasBeenTaken() {
        LobbyManager.PlayerState state = Died(100f);
        state.DeployAt = 0;  // what Tick does when it deploys them

        Assert.True(state.ScheduleDeploy(200f, Delay, Protection));
        Assert.Equal(210f, state.DeployAt);
    }

    /// Shooting out from behind protection gives it up immediately, but the pending
    /// deploy survives — someone who forfeited it still has to reach the arena.
    [Fact]
    public void ForfeitingProtectionLeavesThePendingDeployAlone() {
        LobbyManager.PlayerState state = Died(100f);
        state.SpawnProtectedUntil = 0;

        Assert.False(state.IsProtectedAt(101f));
        Assert.Equal(110f, state.DeployAt);
    }

    [Fact]
    public void APlayerWhoJoinedWithoutDyingHasNoDeployPending() {
        LobbyManager.PlayerState state = new() { SpawnProtectedUntil = 105f };

        Assert.Equal(0f, state.DeployAt);
        Assert.True(state.IsProtectedAt(104f));
        Assert.False(state.IsProtectedAt(105f));
    }

    [Fact]
    public void AFreshStateProtectsNobody() {
        Assert.False(new LobbyManager.PlayerState().IsProtectedAt(0f));
    }
}
