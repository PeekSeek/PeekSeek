using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Serialization;

namespace PeekSeekRaid.Core.Models;

/// What a piece is, as far as breaking it goes.
public enum TargetKind {
    /// Floors and pillars, which keep the game's own damage.
    Untabled,

    /// A metal door, hatch or shutter.
    Door,
    BirchWall,
    MapleWall,
    MetalWall,

    /// Anything else built: roofs, ramps, gadgets.
    Other
}

/// How many of one tool it takes to break each kind of piece. 0 means the tool does
/// nothing to it. For a held tool (the chainsaw) the numbers are seconds of cutting.
public class BreakRule {
    [XmlAttribute]
    public string Tool;

    [XmlAttribute]
    public ushort ItemId;

    [XmlAttribute]
    public float Door;

    [XmlAttribute]
    public float Birch;

    [XmlAttribute]
    public float Maple;

    [XmlAttribute]
    public float Metal;

    [XmlAttribute]
    public float Other;

    public BreakRule() { }

    public BreakRule(string tool, ushort itemId, float door, float birch, float maple, float metal, float other) {
        Tool = tool;
        ItemId = itemId;
        Door = door;
        Birch = birch;
        Maple = maple;
        Metal = metal;
        Other = other;
    }

    public float For(TargetKind kind) => kind switch {
        TargetKind.Door => Door,
        TargetKind.BirchWall => Birch,
        TargetKind.MapleWall => Maple,
        TargetKind.MetalWall => Metal,
        TargetKind.Other => Other,
        _ => 0f
    };
}

/// The hits table: whatever the game worked out, a tool on the table takes a piece down
/// in exactly its number of hits, so a raider can count walls instead of guessing at
/// armour tiers and falloff.
public static class BreakTable {
    /// The rule for a tool, or null for a tool not on the table, which keeps the
    /// game's own damage.
    public static BreakRule For(IEnumerable<BreakRule> rules, ushort tool) {
        return rules.FirstOrDefault(rule => rule.ItemId == tool);
    }

    /// The damage one hit does so that `hits` of them break a piece of `maxHealth`: 0
    /// for a tool that does nothing to it.
    public static ushort DamagePerHit(ushort maxHealth, float hits) {
        if (hits <= 0f) return 0;

        return (ushort)Math.Min(ushort.MaxValue, Math.Ceiling(maxHealth / (double)hits));
    }
}

/// How long each player has been cutting each piece with a held tool. A cut counts as
/// continuous while the hits keep coming less than `gap` apart; stop for longer and the
/// clock starts again.
public class CutTimer {
    private readonly float gap;
    private readonly Dictionary<(ulong Player, int Piece), (float Started, float Last)> cuts = [];

    public CutTimer(float gap) {
        this.gap = gap;
    }

    /// Record a hit at `now` and say how long this cut has been going.
    public float Hit(ulong player, int piece, float now) {
        (ulong, int) key = (player, piece);
        float started = cuts.TryGetValue(key, out (float Started, float Last) cut) && now - cut.Last <= gap ? cut.Started : now;
        cuts[key] = (started, now);
        return now - started;
    }

    public void Clear() => cuts.Clear();
}
