using System;
using System.Collections.Generic;
using System.Linq;
using PeekSeekRaid.Core.Models;
using Xunit;

namespace PeekSeekRaid.Tests;

public class PickDrawTests {
    /// Somebody who never picked gets a powerup nobody on their team has.
    [Fact]
    public void TheFallbackIsNeverTaken() {
        Random random = new(7);
        HashSet<int> taken = [0, 1, 2, 3, 5, 6, 7, 8, 9];

        for (int i = 0; i < 50; i++) Assert.Equal(4, PickDraw.Fallback(10, taken, random));
    }

    [Fact]
    public void AFullBoardHasNothingLeft() {
        Assert.Equal(-1, PickDraw.Fallback(3, new HashSet<int> { 0, 1, 2 }, new Random(1)));
        Assert.Equal(-1, PickDraw.Fallback(0, new HashSet<int>(), new Random(1)));
    }
}

public class BreakTableTests {
    private static List<BreakRule> Rules() {
        PeekSeekRaidConfiguration config = new();
        config.LoadDefaults();
        return config.BreakRules;
    }

    /// The rows of the table, as the spec states them.
    [Theory]
    [InlineData(1241, TargetKind.Door, 1f)]
    [InlineData(1241, TargetKind.BirchWall, 1f)]
    [InlineData(1241, TargetKind.MapleWall, 1f)]
    [InlineData(1241, TargetKind.MetalWall, 3f)]
    [InlineData(1241, TargetKind.Other, 3f)]
    [InlineData(1520, TargetKind.Door, 1f)]
    [InlineData(1520, TargetKind.MapleWall, 0f)]
    [InlineData(519, TargetKind.Door, 2f)]
    [InlineData(519, TargetKind.BirchWall, 1f)]
    [InlineData(519, TargetKind.MapleWall, 1f)]
    [InlineData(519, TargetKind.MetalWall, 3f)]
    [InlineData(490, TargetKind.BirchWall, 1f)]
    [InlineData(490, TargetKind.MapleWall, 3f)]
    [InlineData(490, TargetKind.MetalWall, 0f)]
    public void TheTableSaysSo(ushort tool, TargetKind kind, float hits) {
        Assert.Equal(hits, BreakTable.For(Rules(), tool).For(kind));
    }

    /// A tool off the table keeps the game's own damage.
    [Fact]
    public void AToolOffTheTableHasNoRule() {
        Assert.Null(BreakTable.For(Rules(), 254));
    }

    /// Hits round up, so the last hit always breaks the piece: a metal wall of 1100
    /// takes 367 a charge, and three of them are 1101.
    [Fact]
    public void HitsRoundUpSoTheLastOneBreaksIt() {
        Assert.Equal(367, BreakTable.DamagePerHit(1100, 3f));
        Assert.True(BreakTable.DamagePerHit(1100, 3f) * 3 >= 1100);
        Assert.Equal(0, BreakTable.DamagePerHit(1100, 0f));
    }

    /// A cut is continuous while the hits keep coming; a pause starts the clock again.
    [Fact]
    public void ACutRestartsAfterAPause() {
        CutTimer cuts = new(0.6f);

        Assert.Equal(0f, cuts.Hit(1, 5, 10f));
        Assert.Equal(0.5f, cuts.Hit(1, 5, 10.5f), 3);
        Assert.Equal(1.0f, cuts.Hit(1, 5, 11.0f), 3);
        Assert.Equal(0f, cuts.Hit(1, 5, 12.0f));
        Assert.Equal(0f, cuts.Hit(2, 5, 12.1f));
    }
}

public class BreachTests {
    /// The breach cost is the cheapest ray.
    [Fact]
    public void TheCheapestRayIsTheCost() {
        List<List<BreachPiece>> rays = [
            [new BreachPiece(3), new BreachPiece(3)],
            [new BreachPiece(2), new BreachPiece(1)],
            [new BreachPiece(3), new BreachPiece(3), new BreachPiece(1)]
        ];

        (int ray, float cost) = Breach.Cheapest(rays);
        Assert.Equal(1, ray);
        Assert.Equal(3f, cost);
    }

    [Fact]
    public void NoRaysCostNothing() {
        Assert.Equal((-1, 0f), Breach.Cheapest([]));
    }

    /// A sealed metal box: three metal walls on the way out are nine charges. The
    /// outermost metal goes to maple first, until the ray is at the limit.
    [Fact]
    public void TheOutermostMetalGoesFirst() {
        List<BreachPiece> ray = [new BreachPiece(3, 2), new BreachPiece(3, 2), new BreachPiece(3, 2)];

        Assert.Equal(new[] { 0, 1, 2 }, Breach.Downgrades(ray, 6f));
        Assert.Equal(new[] { 2 }, Breach.Downgrades(ray, 8f));
    }

    /// A door has no maple form and is passed over.
    [Fact]
    public void DoorsAreNotDowngraded() {
        List<BreachPiece> ray = [new BreachPiece(3, 2), new BreachPiece(1), new BreachPiece(3, 2), new BreachPiece(1)];

        Assert.Equal(new[] { 2 }, Breach.Downgrades(ray, 7f));
    }

    [Fact]
    public void ARayUnderTheLimitIsLeftAlone() {
        Assert.Empty(Breach.Downgrades([new BreachPiece(3, 2), new BreachPiece(2)], 6f));
    }
}

public class RosterTests {
    /// Every powerup has a class on its own side.
    [Fact]
    public void EachSideHasItsOwnClasses() {
        PeekSeekRaidConfiguration config = new();
        config.LoadDefaults();

        Assert.All(config.RaidOptions, option => Assert.True(option.Category <= PowerupClass.Support, option.Name));
        Assert.All(config.DefenceOptions, option => Assert.True(option.Category >= PowerupClass.Special, option.Name));
    }

    /// The name is how the board and the bar tell powerups apart.
    [Fact]
    public void NamesAreUnique() {
        PeekSeekRaidConfiguration config = new();
        config.LoadDefaults();

        List<string> names = config.RaidOptions.Concat(config.DefenceOptions).Select(option => option.Name).ToList();
        Assert.Equal(names.Count, names.Distinct().Count());
    }

    /// Every configured tool, and the rocket and airstrike numbers the sheet promises.
    [Fact]
    public void TheAirstrikeIsNineRocketsInSixteenMetres() {
        PeekSeekRaidConfiguration config = new();
        config.LoadDefaults();

        Assert.Equal(9, config.StrikerRockets);
        Assert.Equal(16f, config.StrikerRadius);
    }
}

public class DoorStateTests {
    /// A door keeps who opens it in its own state: owner, then group, then open. With
    /// both zero a locked door opens for nobody, which is the bug this guards.
    [Fact]
    public void TheDoorStateCarriesTheGroup() {
        byte[] state = DoorLock.State(0, 103582791429521412UL);

        Assert.Equal(17, state.Length);
        Assert.Equal(0UL, BitConverter.ToUInt64(state, 0));
        Assert.Equal(103582791429521412UL, BitConverter.ToUInt64(state, 8));
        Assert.Equal(0, state[16]);
    }
}
