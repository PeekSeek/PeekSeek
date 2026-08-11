using PeekSeek.Core;
using SDG.Unturned;
using Xunit;

namespace PeekSeek.Tests;

public class DeathCauseTranslationKeyTests {
    [Theory]
    [InlineData(EDeathCause.BONES, "death_fall")]
    [InlineData(EDeathCause.WATER, "death_drown")]
    [InlineData(EDeathCause.BURNING, "death_burn")]
    [InlineData(EDeathCause.FREEZING, "death_freeze")]
    [InlineData(EDeathCause.BLEEDING, "death_bleed")]
    [InlineData(EDeathCause.SUICIDE, "death_suicide")]
    [InlineData(EDeathCause.GRENADE, "death_explosive")]
    [InlineData(EDeathCause.LANDMINE, "death_explosive")]
    [InlineData(EDeathCause.MISSILE, "death_explosive")]
    [InlineData(EDeathCause.CHARGE, "death_explosive")]
    [InlineData(EDeathCause.ZOMBIE, "death_generic")]
    [InlineData(EDeathCause.VEHICLE, "death_generic")]
    public void MapsEachCauseToItsKey(EDeathCause cause, string expectedKey) {
        Assert.Equal(expectedKey, PSManager.DeathCauseTranslationKey(cause));
    }
}
