using System;
using System.Collections.Generic;
using System.Linq;
using PeekSeek.Core.Models;
using SDG.Unturned;
using Steamworks;
using UnityEngine;

namespace PeekSeek.Core;

/// Buffers the hits and shots a kill event is built from. Nothing here is sent anywhere by
/// itself — PSManager reads it at the moment of death and clears what it took, so a fight
/// that never ends in a kill (someone disengages) just ages out on its own the next time
/// either player is involved in something new, rather than needing a timer to clean it up.
internal static class FightTracker {
    /// How long a hit stays part of "the fight leading up to this death". Long enough for
    /// a dragged-out gunfight, short enough that an unrelated hit from ten minutes ago
    /// can't get attributed to a much later death.
    private const float HitWindowSeconds = 10f;
    /// How long a shot stays eligible as part of "what the killer fired during the fight".
    private const float ShotWindowSeconds = 5f;

    private static readonly Dictionary<ulong, List<PSFightHit>> HitsTaken = [];
    private static readonly Dictionary<ulong, List<PSShotSample>> RecentShots = [];

    internal static void RecordHit(CSteamID victim, CSteamID attacker, float damage, ELimb limb, Vector3 attackerPosition, Vector3 victimPosition, float? reactionTimeSeconds = null) {
        Trim(victim);

        if (!HitsTaken.TryGetValue(victim.m_SteamID, out List<PSFightHit> hits)) {
            hits = [];
            HitsTaken[victim.m_SteamID] = hits;
        }

        bool attributed = attacker != CSteamID.Nil;
        hits.Add(new PSFightHit {
            Time = NowMs(),
            Attacker = attributed ? attacker.ToString() : null,
            Damage = damage,
            Limb = limb.ToString().ToLowerInvariant(),
            ReactionTimeSeconds = reactionTimeSeconds,
            AttackerPosition = attributed ? PSVector3.From(attackerPosition) : null,
            VictimPosition = PSVector3.From(victimPosition)
        });
    }

    /// Only meaningful in bullet-drop mode — instant-hit mode has no per-shot event to
    /// sample from, so callers simply never call this outside that mode.
    internal static void RecordShot(CSteamID shooter, Vector3 origin, Vector3 aimDirection) {
        if (!RecentShots.TryGetValue(shooter.m_SteamID, out List<PSShotSample> shots)) {
            shots = [];
            RecentShots[shooter.m_SteamID] = shots;
        }

        shots.Add(new PSShotSample { Time = NowMs(), Origin = PSVector3.From(origin), AimDirection = PSVector3.From(aimDirection) });

        long cutoff = NowMs() - (long)(ShotWindowSeconds * 1000);
        shots.RemoveAll(shot => shot.Time < cutoff);
    }

    /// Everything the victim took on the way to this death, oldest first. Clears the
    /// buffer, since once a death consumes a fight there is nothing left to attach a
    /// later, unrelated death to.
    internal static List<PSFightHit> TakeFight(CSteamID victim) {
        Trim(victim);

        if (!HitsTaken.TryGetValue(victim.m_SteamID, out List<PSFightHit> hits)) return [];

        HitsTaken.Remove(victim.m_SteamID);
        return hits;
    }

    /// The reciprocal side of a fight: hits `player` took specifically from `attacker` —
    /// what the eventual loser landed on the eventual winner before dying. Only those
    /// entries are removed; unlike TakeFight, hits from anyone else stay buffered, since a
    /// kill against one enemy shouldn't erase a still-ongoing fight with a different one.
    internal static List<PSFightHit> TakeHitsFrom(CSteamID player, CSteamID attacker) {
        Trim(player);

        if (!HitsTaken.TryGetValue(player.m_SteamID, out List<PSFightHit> hits)) return [];

        string attackerID = attacker.ToString();
        List<PSFightHit> matching = [.. hits.Where(hit => hit.Attacker == attackerID)];
        hits.RemoveAll(hit => hit.Attacker == attackerID);
        return matching;
    }

    /// The killer's own shots inside the same recent window. Not cleared on read — unlike
    /// a victim's hits, a shooter's activity doesn't end just because one fight did.
    internal static List<PSShotSample> RecentShotsBy(CSteamID shooter) {
        if (!RecentShots.TryGetValue(shooter.m_SteamID, out List<PSShotSample> shots)) return [];

        long cutoff = NowMs() - (long)(ShotWindowSeconds * 1000);
        return [.. shots.Where(shot => shot.Time >= cutoff)];
    }

    internal static void Clear() {
        HitsTaken.Clear();
        RecentShots.Clear();
    }

    private static void Trim(CSteamID victim) {
        if (!HitsTaken.TryGetValue(victim.m_SteamID, out List<PSFightHit> hits)) return;

        long cutoff = NowMs() - (long)(HitWindowSeconds * 1000);
        hits.RemoveAll(hit => hit.Time < cutoff);
    }

    private static long NowMs() {
        return DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    }
}
