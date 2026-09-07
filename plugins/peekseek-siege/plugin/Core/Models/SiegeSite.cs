using System;
using System.Collections.Generic;
using System.Xml.Serialization;
using UnityEngine;

namespace PeekSeekSiege.Core.Models;

/// One team's plot of ground: where its base is anchored, how far from that anchor its
/// players may build and stand, and where the other team comes in from.
///
/// Both sites are on the same map, so BuildRadius is what stops a team wandering far
/// enough to look at the other team's build in progress. It is deliberately tight — a
/// site is a building, not a district.
///
/// The geometry below is written in plain floats with Vector3 only at the edges. That
/// is not style: UnityEngine.dll targets netstandard2.1 and will not load under the
/// net48 test host, so anything a test needs to check has to be reachable without
/// touching a Unity type.
public class SiegeSite {
    [XmlAttribute]
    public string Name;

    /// The middle of the site. The anchor floor and its claim flag go here, and every
    /// distance below is measured from it.
    public SerializableVector3 Anchor;

    /// Which way a player faces when they are put here, in degrees.
    [XmlAttribute]
    public float Yaw;

    /// How far from Anchor a player may build during the build phase. Also the leash:
    /// step outside it and you are pulled back.
    [XmlAttribute]
    public float BuildRadius;

    /// How far out the attacking team starts. Far enough that the defenders cannot
    /// shoot them off spawn, close enough that a round is not a walk.
    [XmlAttribute]
    public float AttackerSpawnDistance;

    /// Which way, in degrees, the attackers come in from. Kept separate from Yaw so an
    /// approach can be pointed down a valley rather than at whatever the anchor happens
    /// to face.
    [XmlAttribute]
    public float AttackerBearing;

    /// How far apart, in degrees of bearing, a full team is spread across its spawn.
    private const float SpawnSpreadDegrees = 20f;

    public Vector3 AnchorPoint => new(Anchor.X, Anchor.Y, Anchor.Z);

    /// Whether a point is inside this site's build radius, ignoring height. A player on
    /// the roof of their own base is still on their own plot, and a leash that pulled
    /// them down off it would be unplayable.
    public bool Contains(float x, float z) {
        float dx = x - Anchor.X;
        float dz = z - Anchor.Z;

        return dx * dx + dz * dz <= BuildRadius * BuildRadius;
    }

    public bool Contains(Vector3 point) => Contains(point.x, point.z);

    /// Where the attacking team drops in, spread around AttackerBearing so a team does
    /// not spawn stacked on one square metre. A lone attacker gets the bearing itself
    /// rather than one edge of the spread.
    public void AttackerSpawnAt(int index, int total, out float x, out float y, out float z) {
        float bearing = AttackerBearing;
        if (total > 1) {
            bearing += -SpawnSpreadDegrees / 2f + SpawnSpreadDegrees * index / (total - 1);
        }

        double radians = bearing * Math.PI / 180d;

        x = Anchor.X + (float)Math.Sin(radians) * AttackerSpawnDistance;
        y = Anchor.Y + 2f;
        z = Anchor.Z + (float)Math.Cos(radians) * AttackerSpawnDistance;
    }

    public Vector3 AttackerSpawn(int index, int total) {
        AttackerSpawnAt(index, total, out float x, out float y, out float z);
        return new Vector3(x, y, z);
    }
}

/// The two sites, in the order the config lists them. Wrapped so the rest of the plugin
/// asks for "the other side" rather than juggling indices.
public class SiegeSites {
    private readonly List<SiegeSite> sites;

    public SiegeSites(List<SiegeSite> configured) {
        sites = configured;
    }

    public SiegeSite this[int index] => sites[index];

    public SiegeSite Other(int index) => sites[1 - index];

    public int Count => sites.Count;
}
