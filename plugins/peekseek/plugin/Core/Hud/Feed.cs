using System;
using System.Collections.Generic;
using System.Linq;

namespace PeekSeek.Hud;

/// One kill as the feed shows it: who, with what, whom, and the killer's kills this
/// life at the moment it happened.
public class Kill {
    public ulong Killer;
    public string KillerName;
    public ulong Victim;
    public string VictimName;

    /// The item pictured for the weapon, or 0 for nothing, which is what a fall, the
    /// arena and a fist all look like.
    public ushort Weapon;

    /// How many the killer has this life, this one included. Shown from the second.
    public int Streak;

    public float At;
}

/// The last few kills, newest first, and the arithmetic of what to show for each.
/// Kept apart from Unturned so the streak and the wording can be tested.
public class Feed {
    private readonly List<Kill> kills = [];
    private readonly Dictionary<ulong, int> streaks = [];
    private readonly int rows;
    private readonly float lifetime;

    public Feed(int rows, float lifetime) {
        this.rows = rows;
        this.lifetime = lifetime;
    }

    /// Newest first, at most a screen's worth.
    public IReadOnlyList<Kill> Rows => kills;

    /// Record a kill at `now`. A kill of yourself, or by nothing, counts for nobody's
    /// streak but is still a row: the team wants to know they are one down.
    public Kill Add(ulong killer, string killerName, ulong victim, string victimName, ushort weapon, float now) {
        int streak = 0;
        if (killer != 0 && killer != victim) {
            streaks.TryGetValue(killer, out streak);
            streaks[killer] = ++streak;
        }

        Kill kill = new() {
            Killer = killer,
            KillerName = killerName,
            Victim = victim,
            VictimName = victimName,
            Weapon = weapon,
            Streak = streak,
            At = now
        };

        kills.Insert(0, kill);
        if (kills.Count > rows) kills.RemoveRange(rows, kills.Count - rows);
        return kill;
    }

    /// A life is over: the next kill starts from one.
    public void EndLife(ulong steam64) => streaks.Remove(steam64);

    /// Drop rows older than the lifetime. True when anything went.
    public bool Expire(float now) {
        int before = kills.Count;
        kills.RemoveAll(kill => now - kill.At > lifetime);
        return kills.Count != before;
    }

    /// II to X, from the second kill of a life, the way Valorant counts a double and
    /// up; a first kill has no badge and nothing goes past ten.
    public static string Badge(int streak) {
        if (streak < 2) return "";

        string[] numerals = ["", "I", "II", "III", "IV", "V", "VI", "VII", "VIII", "IX", "X"];
        return numerals[Math.Min(streak, 10)];
    }

    /// A name cut to fit its column, with an ellipsis where it was cut.
    public static string Fit(string name, int max) {
        if (string.IsNullOrEmpty(name)) return "";
        if (name.Length <= max) return name;

        return name.Substring(0, Math.Max(1, max - 1)).TrimEnd() + "…";
    }

    /// Whether a row is the viewer's, either end of it, which is the one thing the
    /// feed draws in red.
    public static bool Involves(Kill kill, ulong viewer) => kill.Killer == viewer || kill.Victim == viewer;

    public bool Any => kills.Any();
}
