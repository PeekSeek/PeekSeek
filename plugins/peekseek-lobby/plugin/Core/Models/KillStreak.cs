using System.Collections.Generic;
using System.Linq;

namespace PeekSeekLobby.Core.Models;

/// One player's run of kills, and which streak rewards they have collected
/// recently. Deliberately knows nothing about Unturned: the rules that decide a
/// payout — kills in a row, a window between them, a cooldown per reward — are
/// the part worth reading and testing on their own, and handing out the items is
/// the part that needs a live player.
internal class KillStreak {
    private byte kills;
    private float lastKillAt;
    private readonly Dictionary<byte, float> offCooldownAt = [];

    internal byte Kills => kills;

    /// Ends the run. Dying always does this, however recent the last kill was.
    internal void End() {
        kills = 0;
    }

    /// Forgets the run and every cooldown with it, for a round that has ended.
    internal void EndRound() {
        kills = 0;
        offCooldownAt.Clear();
    }

    /// Counts a kill and returns the one reward it just earned, or null for a kill
    /// that lands on no streak. A kill arriving more than windowSeconds after the
    /// last one starts the run again at one. A reward still on cooldown is passed
    /// over rather than held back — the run carries on climbing and the next
    /// streak up can still pay out.
    internal StreakReward Advance(float now, float windowSeconds, List<StreakReward> rewards) {
        if (now - lastKillAt > windowSeconds) kills = 0;
        lastKillAt = now;
        // Capped rather than wrapped: rolling past 255 would land back on the low
        // kill counts and pay their rewards out all over again.
        if (kills < byte.MaxValue) kills++;

        StreakReward reward = rewards?.FirstOrDefault(entry => entry.KillsRequired == kills);
        if (reward == null) return null;

        if (offCooldownAt.TryGetValue(reward.KillsRequired, out float readyAt) && now < readyAt) return null;
        offCooldownAt[reward.KillsRequired] = now + reward.CooldownSeconds;
        return reward;
    }
}
