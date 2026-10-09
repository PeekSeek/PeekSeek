using System;
using PeekSeek.Core;
using Xunit;

namespace PeekSeek.Tests;

/// The ping handicap must never take a hit below what Unturned will register.
///
/// DamageTool.damagePlayer keeps Mathf.FloorToInt(damage * times) points of damage and
/// throws the whole hit away when that is zero. So the handicap's floor has to survive
/// being multiplied back by the same `times` in single precision, and that is the whole
/// subtlety: a floor of 1f/times looks exact and is not. At times = 0.85, which is the
/// skull's own armour in PeekSeekArmorSkin, (1f/0.85f) * 0.85f is 0.99999994 and floors
/// to zero, so the "fixed" handicap still deleted every head shot on a handicapped
/// player. These cases exist so that never has to be found on a live server again.
public class HandicapTests {
    /// Every multiplier a real match can produce, at the band that cuts hardest.
    ///
    /// 0 is the worst case on purpose: at 0% the handicap wants to take everything, so
    /// the floor is the only thing keeping the hit alive, and if it is off by one
    /// rounding step the hit vanishes.
    [Theory]
    [InlineData(0.85f)]   // SkullArmor, no global multiplier
    [InlineData(1.7f)]    // SkullArmor at a 2.0 global
    [InlineData(0.425f)]  // SkullArmor at a 0.5 global
    [InlineData(0.4725f)] // legs at a 0.9 global with falloff
    [InlineData(0.63f)]   // skull limb and armour together
    [InlineData(0.2025f)]
    [InlineData(1f)]
    [InlineData(0.75f)]
    [InlineData(0.9f)]
    [InlineData(3f)]
    public void AHandicappedHitStillRegisters(float times) {
        foreach (int percentage in new[] { 0, 1, 25, 50, 99, 100 }) {
            float handicapped = PSManager.Handicapped(100f, times, percentage);

            // The multiply stays in single precision, because that is the whole point:
            // the game does it in float and the rounding error is the bug. Widening
            // either side to double here would compute a more accurate product than
            // the game ever sees and the test would pass against the broken code.
            float product = handicapped * times;

            // Math.Floor stands in for Mathf.FloorToInt, which the net48 test host
            // cannot load. Flooring a float widened to double is exact, and the damage
            // here is always positive, so the two agree.
            int registered = (int)Math.Floor((double)product);

            Assert.True(registered >= 1, $"times {times} at {percentage}% floors to {registered}");
        }
    }

    /// The floor only ever raises a hit. A band that takes nothing off must leave the
    /// damage alone, or the handicap would be buffing people.
    [Theory]
    [InlineData(0.85f)]
    [InlineData(1f)]
    [InlineData(3f)]
    public void AFullBandTakesNothingOff(float times) {
        Assert.Equal(100f, PSManager.Handicapped(100f, times, 100), 3);
    }

    /// The case the one above cannot see. At 100 damage the floor is orders of
    /// magnitude below the cut, so any floor at all passes; the claim only has teeth
    /// just above where the floor stops applying, where 1.0001/times sits a hair over
    /// the damage itself. 2 at a 0.5 multiplier is exactly one point of damage, the
    /// smallest hit that still registers, and the floor there is 2.0002.
    ///
    /// Returned unchanged means unchanged, so this is exact rather than rounded: a
    /// shot nobody is handicapping must not come back out of here a different number.
    [Theory]
    [InlineData(2f, 0.5f)]
    [InlineData(1f, 1f)]
    [InlineData(0.34f, 3f)]
    [InlineData(1.18f, 0.85f)]
    public void AFullBandDoesNotBuffTheSmallestRegisteringHit(float damage, float times) {
        Assert.Equal(damage, PSManager.Handicapped(damage, times, 100));
    }

    /// Below 100 the floor is still allowed to raise the hit, and has to: this is the
    /// one case where a cut would otherwise land under a point and vanish.
    [Fact]
    public void APartialBandStillGetsItsFloor() {
        Assert.True(PSManager.Handicapped(2f, 0.5f, 1) * 0.5f >= 1f);
    }

    /// A hit the multiplier was always going to erase is not the handicap's doing, and
    /// the handicap must not quietly resurrect it: a glancing shot that vanished before
    /// anybody's ping was involved keeps vanishing.
    [Fact]
    public void AHitThatNeverRegisteredIsLeftAlone() {
        // 1 damage at a 0.5 multiplier is half a point, which the game floors to zero
        // with or without a handicap. Taking nothing off leaves it exactly as it was,
        // and taking everything off does not get a floor it was never owed.
        Assert.Equal(1f, PSManager.Handicapped(1f, 0.5f, 100), 3);
        Assert.Equal(0f, PSManager.Handicapped(1f, 0.5f, 0), 3);
    }

    /// times = 0 would make the floor a division by zero, which is how an invulnerable
    /// limb or a disabled multiplier arrives.
    [Fact]
    public void AZeroMultiplierDoesNotDivide() {
        Assert.Equal(0f, PSManager.Handicapped(100f, 0f, 0), 3);
        Assert.Equal(100f, PSManager.Handicapped(100f, 0f, 100), 3);
    }
}
