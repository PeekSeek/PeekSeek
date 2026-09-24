using PeekSeek.Core;
using SDG.Unturned;
using Xunit;

namespace PeekSeek.Tests;

public class DeathCauseLabelTests {
    [Theory]
    [InlineData(EDeathCause.BONES, "FELL")]
    [InlineData(EDeathCause.WATER, "DROWNED")]
    [InlineData(EDeathCause.BURNING, "BURNED")]
    [InlineData(EDeathCause.FREEZING, "FROZE")]
    [InlineData(EDeathCause.BLEEDING, "BLED OUT")]
    [InlineData(EDeathCause.SUICIDE, "SUICIDE")]
    [InlineData(EDeathCause.GRENADE, "BLOWN UP")]
    [InlineData(EDeathCause.LANDMINE, "BLOWN UP")]
    [InlineData(EDeathCause.MISSILE, "BLOWN UP")]
    [InlineData(EDeathCause.CHARGE, "BLOWN UP")]
    [InlineData(EDeathCause.ZOMBIE, "DIED")]
    [InlineData(EDeathCause.VEHICLE, "DIED")]
    public void NamesEachCauseForTheCard(EDeathCause cause, string expected) {
        Assert.Equal(expected, PSManager.DeathCauseLabel(cause));
    }
}
