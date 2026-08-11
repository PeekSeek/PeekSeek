using PeekSeek.Core;
using Steamworks;
using Xunit;

namespace PeekSeek.Tests;

/// RecordHit/RecordShot both take a UnityEngine.Vector3, which cannot be constructed in
/// this test host (see the plugin README's "Not covered" table) — so only the paths that
/// never touch one are exercised here. The rest needs a running Unturned/Rocket host.
[Collection("FightTracker")]
public class FightTrackerTests {
    public FightTrackerTests() {
        FightTracker.Clear();
    }

    [Fact]
    public void TakeFight_UnknownVictim_ReturnsEmpty() {
        Assert.Empty(FightTracker.TakeFight(new CSteamID(76561198000000045UL)));
    }

    [Fact]
    public void RecentShotsBy_UnknownShooter_ReturnsEmpty() {
        Assert.Empty(FightTracker.RecentShotsBy(new CSteamID(76561198000000046UL)));
    }

    [Fact]
    public void TakeHitsFrom_UnknownPlayer_ReturnsEmpty() {
        var player = new CSteamID(76561198000000047UL);
        var attacker = new CSteamID(76561198000000048UL);

        Assert.Empty(FightTracker.TakeHitsFrom(player, attacker));
    }
}
