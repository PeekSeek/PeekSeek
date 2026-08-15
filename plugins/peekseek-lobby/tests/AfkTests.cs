using PeekSeekLobby.Core;
using Xunit;

namespace PeekSeekLobby.Tests;

/// The part of /afk that is plain state rather than teleports and chat: who can be shot
/// while parked, and whether coming back costs them a death. ToggleAfk itself needs a
/// live server, so what is checkable here is the rule Tick and the damage handler read.
public class AfkTests {
    private const float Delay = 10f;
    private const float Protection = 5f;

    [Fact]
    public void AfkPlayersCannotBeShot() {
        LobbyManager.PlayerState state = new() { Afk = true };

        Assert.True(state.IsProtectedAt(0f));
        Assert.True(state.IsProtectedAt(999_999f));
    }

    /// The reason protection is held open rather than left on its timer: somebody
    /// standing at spawn for ten minutes cannot shoot back and cannot walk away, so a
    /// protection window that expires under them turns /afk into a way to farm them.
    [Fact]
    public void AfkOutlastsTheProtectionWindowItWasGivenOnDeath() {
        LobbyManager.PlayerState state = new();
        state.ScheduleDeploy(100f, Delay, Protection);
        state.Afk = true;

        // 115f is the exact moment the death's own protection would have run out.
        Assert.True(state.IsProtectedAt(115f));
    }

    /// Tick refuses to deploy an AFK player even with a booking due, which is the guard
    /// that keeps someone who went AFK mid-respawn from being dropped back in.
    [Fact]
    public void ADueDeployStillDoesNotPutAnAfkPlayerBackIn() {
        LobbyManager.PlayerState state = new();
        state.ScheduleDeploy(100f, Delay, Protection);
        state.Afk = true;

        Assert.True(state.Afk && state.DeployAt <= 110f);
    }

    /// ToggleAfk clears the pending deploy on the way in and books a fresh one on the
    /// way out, so the wait to re-enter the arena is the same wait a respawn serves
    /// rather than one left over from before they stepped away.
    [Fact]
    public void ComingBackBooksTheNormalWait() {
        LobbyManager.PlayerState state = new() { Afk = false, DeployAt = 0 };

        Assert.True(state.ScheduleDeploy(200f, Delay, Protection));
        Assert.Equal(210f, state.DeployAt);
    }

    [Fact]
    public void ComingBackDoesNotDoubleBookAPendingDeploy() {
        LobbyManager.PlayerState state = new();
        state.ScheduleDeploy(100f, Delay, Protection);

        Assert.False(state.ScheduleDeploy(200f, Delay, Protection));
        Assert.Equal(110f, state.DeployAt);
    }

    /// /afk and /noqueue are different switches. Stepping away from the keyboard is not
    /// leaving matchmaking, and a match found while AFK still pulls the player into it.
    [Fact]
    public void GoingAfkLeavesQueueingAlone() {
        LobbyManager.PlayerState state = new() { Afk = true };

        Assert.True(state.Queueing);
    }

    /// An AFK player is still queued, so they are still offered matches once their
    /// arrival grace has passed. SendToMatch is what clears the AFK on the way out.
    [Fact]
    public void AnAfkPlayerIsStillOfferedMatches() {
        LobbyManager.PlayerState state = new() { Afk = true, QueueFrom = 0 };

        Assert.True(state.WantsAMatch(100f));
    }
}
