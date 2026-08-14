using System.Collections.Generic;
using System.Linq;
using PeekSeek.Core.Models;
using Steamworks;

namespace PeekSeek.Core;

internal static class StatService {
    internal static List<PSPlayerStat> InstanceStats = [];

    private static PSPlayerStat Find(CSteamID player) {
        return InstanceStats.FirstOrDefault(
            x => x.SteamID == player.m_SteamID
        );
    }

    private static PSPlayerStat GetOrCreate(CSteamID player) {
        PSPlayerStat stat = Find(player);

        if (stat == null) {
            stat = new PSPlayerStat {
                SteamID = player.m_SteamID
            };

            InstanceStats.Add(stat);
        }

        return stat;
    }

    internal static void UpdateStatWins(CSteamID player) {
        GetOrCreate(player).Wins++;
    }

    internal static void UpdateStatKills(CSteamID player) {
        GetOrCreate(player).Kills++;
    }

    internal static void UpdateStatDeaths(CSteamID player) {
        GetOrCreate(player).Deaths++;
    }

    internal static void UpdateStatHeadshots(CSteamID player) {
        GetOrCreate(player).Headshots++;
    }

    /// One landed shot. Called from the damage hook rather than the kill hook, so it
    /// counts what the shooter actually hit rather than only the shots that finished
    /// somebody off.
    internal static void RecordHit(CSteamID player, bool headshot) {
        PSPlayerStat stat = GetOrCreate(player);
        stat.Hits++;
        if (headshot) stat.HeadshotHits++;
    }

    internal static ushort GetStatHits(CSteamID player) {
        return Find(player)?.Hits ?? 0;
    }

    internal static ushort GetStatHeadshotHits(CSteamID player) {
        return Find(player)?.HeadshotHits ?? 0;
    }

    internal static void AddDamageDealt(CSteamID player, float damage) {
        GetOrCreate(player).DamageDealt += damage;
    }

    internal static void AddDamageTaken(CSteamID player, float damage) {
        GetOrCreate(player).DamageTaken += damage;
    }

    internal static byte GetStatWins(CSteamID player) {
        return Find(player)?.Wins ?? 0;
    }

    internal static byte GetStatKills(CSteamID player) {
        return Find(player)?.Kills ?? 0;
    }

    internal static byte GetStatDeaths(CSteamID player) {
        return Find(player)?.Deaths ?? 0;
    }

    internal static byte GetStatHeadshots(CSteamID player) {
        return Find(player)?.Headshots ?? 0;
    }

    internal static bool HasStats(CSteamID player) {
        return Find(player) != null;
    }

    internal static float GetStatDamageDealt(CSteamID player) {
        return Find(player)?.DamageDealt ?? 0;
    }

    internal static float GetStatDamageTaken(CSteamID player) {
        return Find(player)?.DamageTaken ?? 0;
    }

    internal static void MarkDeadThisRound(CSteamID player) {
        GetOrCreate(player).IsDeadThisRound = true;
    }

    internal static bool IsAliveThisRound(CSteamID player) {
        return !(Find(player)?.IsDeadThisRound ?? false);
    }

    /// Puts everyone back in play. Called when a round spawns, so the overlay stops
    /// showing last round's casualties.
    internal static void BeginRound() {
        foreach (PSPlayerStat stat in InstanceStats) {
            stat.IsDeadThisRound = false;
        }
    }

    internal static int GetTotalPlayedRounds() {
        return InstanceStats.Sum(x => x.Wins);
    }
}
