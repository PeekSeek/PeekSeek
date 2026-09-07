using System;
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
        AttackerSpawnDistance = 70f,
        AttackerBearing = 141f
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

    /// A lone attacker spawns on the bearing itself rather than at one edge of the
    /// spread, so a one-versus-one does not start somebody off to the side.
    [Fact]
    public void ASingleAttackerSpawnsOnTheBearing() {
        SiegeSite site = North();
        site.AttackerSpawnAt(0, 1, out float x, out _, out float z);

        double radians = site.AttackerBearing * Math.PI / 180d;

        Assert.Equal(site.Anchor.X + Math.Sin(radians) * site.AttackerSpawnDistance, x, 2);
        Assert.Equal(site.Anchor.Z + Math.Cos(radians) * site.AttackerSpawnDistance, z, 2);
    }

    [Fact]
    public void AttackersSpawnOutsideTheBaseTheyAreAttacking() {
        SiegeSite site = North();

        for (int i = 0; i < 5; i++) {
            site.AttackerSpawnAt(i, 5, out float x, out _, out float z);
            Assert.False(site.Contains(x, z), $"Attacker {i} spawned inside the site.");
        }
    }

    [Fact]
    public void AttackersSpawnAtTheConfiguredDistance() {
        SiegeSite site = North();

        for (int i = 0; i < 5; i++) {
            site.AttackerSpawnAt(i, 5, out float x, out _, out float z);
            Assert.Equal(site.AttackerSpawnDistance, Distance(site.Anchor.X, site.Anchor.Z, x, z), 2);
        }
    }

    [Fact]
    public void AttackersDoNotSpawnOnTopOfEachOther() {
        SiegeSite site = North();

        site.AttackerSpawnAt(0, 5, out float firstX, out _, out float firstZ);
        site.AttackerSpawnAt(4, 5, out float lastX, out _, out float lastZ);

        Assert.True(Distance(firstX, firstZ, lastX, lastZ) > 5f);
    }

    private static float Distance(float x1, float z1, float x2, float z2) {
        return (float)Math.Sqrt((x1 - x2) * (x1 - x2) + (z1 - z2) * (z1 - z2));
    }
}
