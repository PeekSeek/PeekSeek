using System;
using System.Collections.Generic;
using PeekSeekSiege.Core.Models;
using Xunit;

namespace PeekSeekSiege.Tests;

/// UnityEngine.dll targets netstandard2.1 and cannot be loaded by the net48 test host,
/// so everything here goes through SiegeSite's float API rather than its Vector3 one.
/// That is the same arithmetic — the Vector3 overloads are one-line wrappers over it.
public class SiegeSiteTests {
    private static SiegeSite North() => new() {
        Name = "North",
        Anchor = new SerializableVector3(74.97f, 65.65f, 311.35f),
        Yaw = 321.4624f,
        BuildRadius = 32f,
        AttackerSpawns = [
            new SerializableVector3(152.60f, 67.76f, 383.00f),
            new SerializableVector3(8.21f, 68.47f, 286.77f),
            new SerializableVector3(91.95f, 62.73f, 232.95f)
        ]
    };

    [Fact]
    public void TheAnchorIsInsideItsOwnSite() {
        SiegeSite site = North();
        Assert.True(site.Contains(site.Anchor.X, site.Anchor.Z));
    }

    [Fact]
    public void JustOutsideTheRadiusIsOutside() {
        SiegeSite site = North();

        Assert.True(site.Contains(site.Anchor.X + 31f, site.Anchor.Z));
        Assert.False(site.Contains(site.Anchor.X + 33f, site.Anchor.Z));
    }

    /// The two configured sites are hundreds of metres apart, so their build radii come
    /// nowhere near each other. If somebody widens a radius past this, the build phase
    /// stops being private and this test is the thing that says so.
    [Fact]
    public void TheTwoSitesDoNotOverlap() {
        PeekSeekSiegeConfiguration config = new();
        config.LoadDefaults();

        SiegeSite a = config.Sites[0];
        SiegeSite b = config.Sites[1];

        float apart = Distance(a.Anchor.X, a.Anchor.Z, b.Anchor.X, b.Anchor.Z);

        Assert.True(apart > a.BuildRadius + b.BuildRadius,
            $"Sites are {apart}m apart but their build radii total {a.BuildRadius + b.BuildRadius}m.");
    }

    /// Neither team can walk from its own plot to a point that sees the other's, which
    /// is the entire reason the leash exists.
    [Fact]
    public void NeitherSiteReachesTheOther() {
        PeekSeekSiegeConfiguration config = new();
        config.LoadDefaults();

        SiegeSite a = config.Sites[0];
        SiegeSite b = config.Sites[1];

        Assert.False(a.Contains(b.Anchor.X, b.Anchor.Z));
        Assert.False(b.Contains(a.Anchor.X, a.Anchor.Z));
    }

    /// A lone attacker stands on the point itself: the dot on the map is where they
    /// come in, not the middle of a spread they are somewhere in.
    [Fact]
    public void ASingleAttackerSpawnsOnThePoint() {
        SiegeSite site = North();

        for (int spawn = 0; spawn < site.AttackerSpawns.Count; spawn++) {
            site.AttackerSpawnAt(spawn, 0, 1, out float x, out float y, out float z);

            Assert.Equal(site.AttackerSpawns[spawn].X, x, 3);
            Assert.Equal(site.AttackerSpawns[spawn].Y, y, 3);
            Assert.Equal(site.AttackerSpawns[spawn].Z, z, 3);
        }
    }

    /// Five who chose the same dot stand a step apart round it, all within arm's reach
    /// of the point, none on the same square metre.
    [Fact]
    public void ATeamOnOneSpawnStandsAroundIt() {
        SiegeSite site = North();
        SerializableVector3 point = site.AttackerSpawns[1];
        List<(float, float)> stood = [];

        for (int i = 0; i < 5; i++) {
            site.AttackerSpawnAt(1, i, 5, out float x, out _, out float z);

            Assert.True(Distance(point.X, point.Z, x, z) < 4f, $"Attacker {i} is {Distance(point.X, point.Z, x, z)}m from the dot.");
            foreach ((float ox, float oz) in stood) {
                Assert.True(Distance(ox, oz, x, z) > 1f, $"Attacker {i} is on top of a teammate.");
            }
            stood.Add((x, z));
        }
    }

    /// Every configured spawn, at both sites, is off the plot it attacks: an attacker
    /// who came in on the base would be past the walls before the round started.
    [Fact]
    public void EveryAttackerSpawnIsOutsideTheSiteItAttacks() {
        PeekSeekSiegeConfiguration config = new();
        config.LoadDefaults();

        foreach (SiegeSite site in config.Sites) {
            Assert.Equal(3, site.AttackerSpawns.Count);
            foreach (SerializableVector3 spawn in site.AttackerSpawns) {
                Assert.False(site.Contains(spawn.X, spawn.Z), $"A spawn of {site.Name} is inside its build radius.");
            }
        }
    }

    /// An attacker spawns looking at the base. Yaw is Unity's: 0 faces +Z, 90 faces +X.
    [Theory]
    [InlineData(0f, -10f, 0f)]
    [InlineData(-10f, 0f, 90f)]
    [InlineData(0f, 10f, 180f)]
    [InlineData(10f, 0f, -90f)]
    public void AnAttackerFacesTheAnchor(float dx, float dz, float yaw) {
        SiegeSite site = North();

        Assert.Equal(yaw, site.YawTowardsAnchor(site.Anchor.X + dx, site.Anchor.Z + dz), 2);
    }

    /// Chat says where a spawn lies from the base, so a bearing has to be measured
    /// clockwise from north: the North site's first spawn is up and to the right.
    [Fact]
    public void ASpawnsBearingIsClockwiseFromNorth() {
        SiegeSite site = North();

        Assert.InRange(site.BearingOfSpawn(0), 30f, 60f);
        Assert.InRange(site.BearingOfSpawn(1), 240f, 280f);
        Assert.InRange(site.BearingOfSpawn(2), 165f, 195f);
    }

    private static float Distance(float x1, float z1, float x2, float z2) {
        return (float)Math.Sqrt((x1 - x2) * (x1 - x2) + (z1 - z2) * (z1 - z2));
    }
}
