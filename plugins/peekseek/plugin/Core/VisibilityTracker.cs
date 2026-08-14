using System.Collections.Generic;
using PeekSeek.Core.Models;
using SDG.Unturned;
using Steamworks;
using UnityEngine;

namespace PeekSeek.Core;

/// Reaction time isn't an event Unturned fires — it has to be inferred by polling line of
/// sight. This tracks, per player, the moment an enemy first became visible to them since
/// their last hit landed; PSManager consumes that window the instant they deal damage, so
/// a kill's first hit can report "time from seeing someone to actually hitting them."
///
/// The sight-blocking layer mask below is assembled from SDG.Unturned.RayMasks rather than
/// a named "line of sight" preset — Unturned doesn't ship one — so treat the exact mix as a
/// reasonable first pass to sanity-check against a live server, not a verified constant the
/// way the event hooks elsewhere in this codebase are.
internal static class VisibilityTracker {
    private const float FieldOfViewDegrees = 100f;
    private const int SightBlockingMask = RayMasks.LARGE | RayMasks.MEDIUM | RayMasks.SMALL
        | RayMasks.GROUND | RayMasks.GROUND2 | RayMasks.ENVIRONMENT
        | RayMasks.STRUCTURE | RayMasks.BARRICADE | RayMasks.VEHICLE;

    /// Steam64 to the realtimeSinceStartup an enemy first came into view, for whoever
    /// currently has an open reaction-time window (i.e. has not hit anyone since).
    private static readonly Dictionary<ulong, float> FirstVisibleAt = [];

    /// Run every tick from PSManager on a short interval — see PSManager.VisibilityIntervalSeconds.
    internal static void Tick(List<PSTeam> teams) {
        List<Player> alive = [];
        foreach (PSTeam team in teams) {
            foreach (ulong user in team.Players) {
                Player p = PlayerTool.getPlayer(new CSteamID(user));
                if (p != null && !p.life.isDead && !p.movement.isSafe) alive.Add(p);
            }
        }

        foreach (Player shooter in alive) {
            bool seesAnyone = false;
            foreach (Player target in alive) {
                if (target == shooter) continue;
                if (SameTeam(teams, shooter, target)) continue;

                if (CanSee(shooter, target)) {
                    seesAnyone = true;
                    break;
                }
            }

            ulong shooterID = shooter.channel.owner.playerID.steamID.m_SteamID;
            if (seesAnyone) {
                if (!FirstVisibleAt.ContainsKey(shooterID)) FirstVisibleAt[shooterID] = Time.realtimeSinceStartup;
            } else {
                FirstVisibleAt.Remove(shooterID);
            }
        }
    }

    /// Consumes the reaction-time window for an attacker who just dealt damage. Null when
    /// there was no open window — either this isn't their first hit of the engagement, or
    /// visibility and the hit landed within the same tick before a window could open.
    internal static float? ConsumeReactionTime(CSteamID attacker) {
        if (!FirstVisibleAt.TryGetValue(attacker.m_SteamID, out float since)) return null;

        FirstVisibleAt.Remove(attacker.m_SteamID);
        return Time.realtimeSinceStartup - since;
    }

    internal static void Clear() {
        FirstVisibleAt.Clear();
    }

    private static bool SameTeam(List<PSTeam> teams, Player a, Player b) {
        return TeamOf(teams, a.channel.owner.playerID.steamID.m_SteamID)
            == TeamOf(teams, b.channel.owner.playerID.steamID.m_SteamID);
    }

    private static string TeamOf(List<PSTeam> teams, ulong steamID) {
        foreach (PSTeam team in teams) {
            if (team.Players.Contains(steamID)) return team.Name;
        }
        return null;
    }

    private static bool CanSee(Player shooter, Player target) {
        Vector3 origin = shooter.look.aim.position;
        Vector3 toTarget = target.transform.position - origin;
        if (Vector3.Angle(shooter.look.aim.forward, toTarget) > FieldOfViewDegrees / 2f) return false;

        return !Physics.Linecast(origin, target.transform.position, SightBlockingMask);
    }
}
