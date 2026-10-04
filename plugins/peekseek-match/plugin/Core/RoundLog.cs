using System.Collections.Generic;
using System.Linq;
using Steamworks;

namespace PeekSeek.Core;

/// Round-scoped bookkeeping cleared by PSManager.NotifyTeamWon once each round ends —
/// separate from FightTracker, which is scoped to a single fight rather than the whole
/// round. Backs the ace check (who got every kill this round) and the post-round
/// per-enemy damage whisper.
internal static class RoundLog {
    private static readonly Dictionary<ulong, int> KillsByPlayer = [];
    private static readonly Dictionary<ulong, List<RoundDamageEntry>> DamageByAttacker = [];

    internal static void RecordKill(CSteamID killer) {
        KillsByPlayer.TryGetValue(killer.m_SteamID, out int count);
        KillsByPlayer[killer.m_SteamID] = count + 1;
    }

    internal static int KillsBy(CSteamID player) {
        return KillsByPlayer.TryGetValue(player.m_SteamID, out int count) ? count : 0;
    }

    internal static void RecordDamage(CSteamID attacker, CSteamID victim, float damage) {
        if (attacker == CSteamID.Nil) return;

        if (!DamageByAttacker.TryGetValue(attacker.m_SteamID, out List<RoundDamageEntry> entries)) {
            entries = [];
            DamageByAttacker[attacker.m_SteamID] = entries;
        }

        RoundDamageEntry entry = entries.FirstOrDefault(e => e.Victim == victim.m_SteamID);
        if (entry == null) {
            entry = new RoundDamageEntry { Victim = victim.m_SteamID };
            entries.Add(entry);
        }

        entry.Damage += damage;
        entry.Hits++;
    }

    internal static List<RoundDamageEntry> DamageBy(CSteamID attacker) {
        return DamageByAttacker.TryGetValue(attacker.m_SteamID, out List<RoundDamageEntry> entries)
            ? entries
            : [];
    }

    internal static void Clear() {
        KillsByPlayer.Clear();
        DamageByAttacker.Clear();
    }
}

internal class RoundDamageEntry {
    internal ulong Victim;
    internal float Damage;
    internal int Hits;
}
