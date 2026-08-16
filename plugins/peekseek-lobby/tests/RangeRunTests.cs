using PeekSeekLobby.Core.Models;
using Xunit;

namespace PeekSeekLobby.Tests;

/// The target range's clock and its layout. Both are plain arithmetic held away
/// from UnityEngine on purpose, so the rules that decide where the row sits and
/// when a run is over are checkable without a server.
public class RangeRunTests {
    [Fact]
    public void ARunIsNotOverWhileTargetsStand() {
        RangeRun run = new(100f, 3);

        Assert.Equal(3, run.Standing);
        Assert.False(run.IsComplete);
    }

    [Fact]
    public void KnockingDownTheLastTargetEndsTheRun() {
        RangeRun run = new(100f, 2);

        Assert.Equal(1, run.Knock());
        Assert.False(run.IsComplete);
        Assert.Equal(0, run.Knock());
        Assert.True(run.IsComplete);
    }

    /// Two bullets landing on the last mannequin in the same frame both reach
    /// Knock. Going negative would leave the run complete but the finish already
    /// fired, so the count floors instead.
    [Fact]
    public void KnockingPastTheLastTargetFloorsAtZero() {
        RangeRun run = new(100f, 1);
        run.Knock();

        Assert.Equal(0, run.Knock());
        Assert.Equal(0, run.Standing);
    }

    [Fact]
    public void ElapsedIsMeasuredFromTheStart() {
        Assert.Equal(14.5f, new RangeRun(100f, 4).Elapsed(114.5f), 3);
    }

    [Fact]
    public void NoTargetsMeansNoAngles() {
        Assert.Empty(RangeRun.TargetAngles(0, 90f));
    }

    /// One target has no spread to be spread across, and belongs straight ahead
    /// rather than at one end of the arc.
    [Fact]
    public void ASingleTargetSitsDeadAhead() {
        Assert.Equal([0f], RangeRun.TargetAngles(1, 90f));
    }

    [Fact]
    public void TheRowIsCentredOnWhereThePlayerIsLooking() {
        float[] angles = RangeRun.TargetAngles(8, 90f);

        Assert.Equal(8, angles.Length);
        Assert.Equal(-45f, angles[0], 3);
        Assert.Equal(45f, angles[7], 3);
        Assert.Equal(0f, angles[0] + angles[7], 3);
    }

    [Fact]
    public void TargetsAreEvenlySpaced() {
        float[] angles = RangeRun.TargetAngles(5, 100f);
        float step = angles[1] - angles[0];

        for (int i = 1; i < angles.Length; i++) {
            Assert.Equal(step, angles[i] - angles[i - 1], 3);
        }
        Assert.Equal(25f, step, 3);
    }

    /// A best of zero is how "no best yet" is stored, so a first clear always
    /// counts however slow it was.
    [Fact]
    public void AFirstClearIsAlwaysAPersonalBest() {
        Assert.True(RangeRun.BeatsBest(93.4f, 0f));
    }

    [Theory]
    [InlineData(12.0f, 14.0f, true)]
    [InlineData(14.0f, 12.0f, false)]
    [InlineData(12.0f, 12.0f, false)]
    public void OnlyAFasterTimeBeatsTheBest(float elapsed, float best, bool expected) {
        Assert.Equal(expected, RangeRun.BeatsBest(elapsed, best));
    }
}
