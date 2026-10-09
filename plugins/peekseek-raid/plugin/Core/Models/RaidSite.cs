using System;
using System.Collections.Generic;
using System.Xml.Serialization;
using UnityEngine;

namespace PeekSeekRaid.Core.Models;

/// One team's plot of ground: where its base is anchored, how far from that anchor its
/// players may build and stand, and the places the other team can choose to come in
/// from.
///
/// Both sites are on the same map, so BuildRadius is what stops a team wandering far
/// enough to look at the other team's build in progress. It is deliberately tight, a
/// site is a building, not a district.
///
/// The geometry below is written in plain floats with Vector3 only at the edges. That
/// is not style: UnityEngine.dll targets netstandard2.1 and will not load under the
/// net48 test host, so anything a test needs to check has to be reachable without
/// touching a Unity type.
public class RaidSite {
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

    /// Where the attacking team can come in from: the three points an attacker chooses
    /// between on the map before a round. The order is the order the dots are numbered,
    /// and the dots are baked into the UI from the same numbers (ui/tools/generate.py),
    /// so a change here is a change there too.
    [XmlArray]
    public List<SerializableVector3> AttackerSpawns = [];

    /// How far from a chosen point the attackers who chose it stand, so a team that all
    /// picked the same dot does not start on one square metre. Small: the point is
    /// the choice, and a spread wide enough to matter would be a fourth spawn.
    private const float SpawnSpreadMetres = 2.5f;

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

    /// Which way a point lies from the anchor and how far, as a label for its dot on
    /// the map: "NE  106 m". North is up the map, which is +z.
    public string Bearing(SerializableVector3 point) {
        float dx = point.X - Anchor.X;
        float dz = point.Z - Anchor.Z;
        float metres = (float)Math.Sqrt(dx * dx + dz * dz);

        // Compass degrees, clockwise from north.
        float degrees = (float)((Math.Atan2(dx, dz) * 180 / Math.PI + 360) % 360);
        string[] points = ["N", "NE", "E", "SE", "S", "SW", "W", "NW"];
        string way = points[(int)Math.Round(degrees / 45) % 8];

        return $"{way}  {Math.Round(metres)} m";
    }

    /// Where an attacker who chose spawn `spawn` stands, as the `index`th of `total`
    /// who chose it. One stands on the point itself; more stand in a ring round it, so
    /// the spot on the map is where the first one is and the rest are within arm's reach.
    public void AttackerSpawnAt(int spawn, int index, int total, out float x, out float y, out float z) {
        SerializableVector3 point = AttackerSpawns[Math.Max(0, Math.Min(spawn, AttackerSpawns.Count - 1))];
        x = point.X;
        y = point.Y;
        z = point.Z;
        if (total <= 1) return;

        double radians = 2d * Math.PI * index / total;
        x += (float)Math.Sin(radians) * SpawnSpreadMetres;
        z += (float)Math.Cos(radians) * SpawnSpreadMetres;
    }

    public Vector3 AttackerSpawn(int spawn, int index, int total) {
        AttackerSpawnAt(spawn, index, total, out float x, out float y, out float z);
        return new Vector3(x, y, z);
    }

    /// The yaw that looks at the anchor from a point, in Unity's degrees: 0 faces +Z
    /// and turns clockwise seen from above. An attacker spawns looking at the base.
    public float YawTowardsAnchor(float x, float z) {
        return (float)(Math.Atan2(Anchor.X - x, Anchor.Z - z) * 180d / Math.PI);
    }
}

/// The two sites, in the order the config lists them. Wrapped so the rest of the plugin
/// asks for "the other side" rather than juggling indices.
public class RaidSites {
    private readonly List<RaidSite> sites;

    public RaidSites(List<RaidSite> configured) {
        sites = configured;
    }

    public RaidSite this[int index] => sites[index];

    public RaidSite Other(int index) => sites[1 - index];

    public int Count => sites.Count;
}
