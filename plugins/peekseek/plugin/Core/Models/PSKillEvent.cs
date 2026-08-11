using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEngine;

namespace PeekSeek.Core.Models;

/// One durable kill record pushed to /unt/kills. Unlike PSUpdateState this is never
/// re-derived — it is built once, at the moment of death, from whatever the fight tracker
/// buffered, and stored as-is. New fields should be additive: the website stores this JSON
/// verbatim in one column, so nothing here is a frozen schema the way the /unt/* routes are.
internal class PSKillEvent {
    [JsonProperty("id")]
    public int Id { get; set; }
    /// Unix milliseconds.
    [JsonProperty("time")]
    public long Time { get; set; }
    [JsonProperty("map")]
    public string Map { get; set; }
    [JsonProperty("round")]
    public int Round { get; set; }
    /// Null when nobody killed them — bleeding out, falling, the arena border.
    [JsonProperty("killer")]
    public string Killer { get; set; }
    [JsonProperty("killerName")]
    public string KillerName { get; set; }
    [JsonProperty("victim")]
    public string Victim { get; set; }
    [JsonProperty("victimName")]
    public string VictimName { get; set; }
    /// Lowercased EDeathCause.
    [JsonProperty("cause")]
    public string Cause { get; set; }
    /// Lowercased ELimb, of the fatal hit.
    [JsonProperty("limb")]
    public string Limb { get; set; }
    /// Item id the killer was holding, or 0 when there was no killer.
    [JsonProperty("weapon")]
    public int Weapon { get; set; }
    [JsonProperty("killerPos")]
    public PSVector3 KillerPosition { get; set; }
    [JsonProperty("victimPos")]
    public PSVector3 VictimPosition { get; set; }
    /// Every hit the victim took in the run-up to this death, oldest first — from
    /// whoever, not only the eventual killer, so a multi-attacker fight is visible as one.
    [JsonProperty("hits")]
    public List<PSFightHit> Hits { get; set; }
    /// The other side of the same fight: hits the killer took specifically from the
    /// victim before winning it — what the loser managed to land back. Empty when the
    /// victim never got a hit in, which is itself worth knowing.
    [JsonProperty("reciprocalHits")]
    public List<PSFightHit> ReciprocalHits { get; set; }
    /// The killer's own recent shots (origin + aim direction), oldest first. Only
    /// populated in bullet-drop mode (Settings.Ballistics) — instant-hit mode has no
    /// per-shot event to sample, so this is left empty rather than guessed at.
    [JsonProperty("shots")]
    public List<PSShotSample> Shots { get; set; }

    public PSKillEvent() {
        Hits = [];
        ReciprocalHits = [];
        Shots = [];
    }
}

/// One hit landed on the eventual victim before they died.
internal class PSFightHit {
    [JsonProperty("time")]
    public long Time { get; set; }
    /// Null for the same reason PSKillEvent.Killer can be — unattributed damage.
    [JsonProperty("attacker")]
    public string Attacker { get; set; }
    [JsonProperty("damage")]
    public float Damage { get; set; }
    [JsonProperty("limb")]
    public string Limb { get; set; }
    [JsonProperty("attackerPos")]
    public PSVector3 AttackerPosition { get; set; }
    [JsonProperty("victimPos")]
    public PSVector3 VictimPosition { get; set; }
    /// Seconds from the attacker first seeing an enemy to this hit — set only on the hit
    /// that actually consumed an open visibility window (see VisibilityTracker), so most
    /// hits leave this null rather than a stale or fabricated number.
    [JsonProperty("reactionTime")]
    public float? ReactionTimeSeconds { get; set; }
}

/// One bullet the killer fired, sampled from UseableGun.onBulletSpawned. Origin and aim
/// direction are enough to reconstruct spray/aim-offset from a stationary crosshair
/// centerline; not claimed to be "shots at this victim specifically" since the killer may
/// have been engaging more than one person in the sampled window.
internal class PSShotSample {
    [JsonProperty("time")]
    public long Time { get; set; }
    [JsonProperty("origin")]
    public PSVector3 Origin { get; set; }
    [JsonProperty("aim")]
    public PSVector3 AimDirection { get; set; }
}

internal class PSVector3 {
    [JsonProperty("x")]
    public float X { get; set; }
    [JsonProperty("y")]
    public float Y { get; set; }
    [JsonProperty("z")]
    public float Z { get; set; }

    internal static PSVector3 From(Vector3 v) {
        return new PSVector3 { X = v.x, Y = v.y, Z = v.z };
    }
}
