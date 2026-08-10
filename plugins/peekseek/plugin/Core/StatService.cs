using System.Collections.Generic;
using System.Linq;
using PeekSeek.Core.Models;
using Steamworks;

namespace PeekSeek.Core;

internal static class StatService {
    internal static List<PSPlayerStat> InstanceStats = [];

    internal static void UpdateStatWins(CSteamID player) {
        PSPlayerStat stat = InstanceStats.FirstOrDefault(
            x => x.SteamID == player.m_SteamID
        );

        if (stat == null) {
            stat = new PSPlayerStat {
                SteamID = player.m_SteamID,
                Wins = 0,
            };

            InstanceStats.Add(stat);
        }

        stat.Wins++;
    }

    internal static void UpdateStatKills(CSteamID player) {
        PSPlayerStat stat = InstanceStats.FirstOrDefault(
            x => x.SteamID == player.m_SteamID
        );
        if (stat == null) {
            stat = new PSPlayerStat {
                SteamID = player.m_SteamID,
                Kills = 0,
            };
            InstanceStats.Add(stat);
        }
        stat.Kills++;
    }

    internal static void UpdateStatDeaths(CSteamID player) {
        PSPlayerStat stat = InstanceStats.FirstOrDefault(
            x => x.SteamID == player.m_SteamID
        );
        if (stat == null) {
            stat = new PSPlayerStat {
                SteamID = player.m_SteamID,
                Deaths = 0,
            };
            InstanceStats.Add(stat);
        }
        stat.Deaths++;
    }

    internal static byte GetStatWins(CSteamID player) {
        PSPlayerStat stat = InstanceStats.FirstOrDefault(
            x => x.SteamID == player.m_SteamID
        );

        return stat?.Wins ?? (byte)0;
    }

    internal static byte GetStatKills(CSteamID player) {
        PSPlayerStat stat = InstanceStats.FirstOrDefault(
            x => x.SteamID == player.m_SteamID
        );
        return stat?.Kills ?? (byte)0;
    }

    internal static byte GetStatDeaths(CSteamID player) {
        PSPlayerStat stat = InstanceStats.FirstOrDefault(
            x => x.SteamID == player.m_SteamID
        );
        return stat?.Deaths ?? (byte)0;
    }

    internal static int GetTotalPlayedRounds() {
        return InstanceStats.Sum(x => x.Wins);
    }
}
