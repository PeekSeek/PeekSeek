using System.Collections.Generic;
using PeekSeekLobby.Core.Models;
using Xunit;

namespace PeekSeekLobby.Tests;

/// The rules a streak pays out under, which are otherwise only observable by
/// killing people on a live server: kills in a row, no more than a window apart,
/// no death in between, and one cooldown per reward.
public class KillStreakTests {
    private const float Window = 10f;

    private static List<StreakReward> Rewards(float cooldown = 60f) => [
        new StreakReward { KillsRequired = 2, CooldownSeconds = cooldown, Items = [] },
        new StreakReward { KillsRequired = 3, CooldownSeconds = cooldown, Items = [] },
        new StreakReward { KillsRequired = 5, CooldownSeconds = cooldown, Items = [] }
    ];

    [Fact]
    public void Advance_PaysNothingForTheFirstKill() {
        var streak = new KillStreak();

        Assert.Null(streak.Advance(0f, Window, Rewards()));
        Assert.Equal((byte)1, streak.Kills);
    }

    [Fact]
    public void Advance_PaysOutOnTheExactKillCount() {
        var streak = new KillStreak();
        List<StreakReward> rewards = Rewards();

        Assert.Null(streak.Advance(0f, Window, rewards));
        Assert.Equal(2, streak.Advance(1f, Window, rewards).KillsRequired);
        Assert.Equal(3, streak.Advance(2f, Window, rewards).KillsRequired);
        // Four is not a streak, so nothing lands until five.
        Assert.Null(streak.Advance(3f, Window, rewards));
        Assert.Equal(5, streak.Advance(4f, Window, rewards).KillsRequired);
    }

    /// The gap is what makes a streak a burst of fighting rather than a tally of
    /// the whole life.
    [Fact]
    public void Advance_StartsOverWhenKillsFallOutsideTheWindow() {
        var streak = new KillStreak();
        List<StreakReward> rewards = Rewards();

        streak.Advance(0f, Window, rewards);
        Assert.Equal(2, streak.Advance(9f, Window, rewards).KillsRequired);

        // 10.1s after the last one: too late to continue, so this is a first kill.
        Assert.Null(streak.Advance(19.1f, Window, rewards));
        Assert.Equal((byte)1, streak.Kills);
    }

    [Fact]
    public void End_StopsAStreakEvenMidWindow() {
        var streak = new KillStreak();
        List<StreakReward> rewards = Rewards();

        streak.Advance(0f, Window, rewards);
        streak.End();

        Assert.Equal((byte)0, streak.Kills);
        // The next kill is a first kill again, not the second of the old run.
        Assert.Null(streak.Advance(1f, Window, rewards));
    }

    /// Without this a player who keeps trading two kills for a death collects an
    /// umbrella and a bounce every few seconds.
    [Fact]
    public void Advance_WillNotPayTheSameStreakTwiceInsideItsCooldown() {
        var streak = new KillStreak();
        List<StreakReward> rewards = Rewards();

        streak.Advance(0f, Window, rewards);
        Assert.NotNull(streak.Advance(1f, Window, rewards));

        streak.End();
        streak.Advance(2f, Window, rewards);
        Assert.Null(streak.Advance(3f, Window, rewards));
    }

    [Fact]
    public void Advance_PaysTheSameStreakAgainOnceItsCooldownExpires() {
        var streak = new KillStreak();
        List<StreakReward> rewards = Rewards();

        streak.Advance(0f, Window, rewards);
        Assert.NotNull(streak.Advance(1f, Window, rewards));

        streak.End();
        streak.Advance(70f, Window, rewards);
        Assert.NotNull(streak.Advance(71f, Window, rewards));
    }

    /// A streak on cooldown must not stall the run, the kills keep counting, so
    /// the bigger reward further up still lands.
    [Fact]
    public void Advance_KeepsClimbingPastARewardOnCooldown() {
        var streak = new KillStreak();
        List<StreakReward> rewards = Rewards();

        streak.Advance(0f, Window, rewards);
        streak.Advance(1f, Window, rewards);
        streak.End();

        streak.Advance(2f, Window, rewards);
        Assert.Null(streak.Advance(3f, Window, rewards));
        Assert.Equal(3, streak.Advance(4f, Window, rewards).KillsRequired);
    }

    [Fact]
    public void EndRound_ClearsTheCooldownsToo() {
        var streak = new KillStreak();
        List<StreakReward> rewards = Rewards();

        streak.Advance(0f, Window, rewards);
        Assert.NotNull(streak.Advance(1f, Window, rewards));

        streak.EndRound();

        streak.Advance(2f, Window, rewards);
        Assert.NotNull(streak.Advance(3f, Window, rewards));
    }

    [Fact]
    public void Advance_CountsKillsWithNoRewardsConfigured() {
        var streak = new KillStreak();

        Assert.Null(streak.Advance(0f, Window, null));
        Assert.Null(streak.Advance(1f, Window, []));
        Assert.Equal((byte)2, streak.Kills);
    }
}
