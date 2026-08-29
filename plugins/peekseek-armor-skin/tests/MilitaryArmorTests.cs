using SDG.Unturned;
using Xunit;

namespace PeekSeekArmorSkin.Tests;

public class MilitaryArmorTests {
    private static PeekSeekArmorSkinConfiguration Defaults() {
        var config = new PeekSeekArmorSkinConfiguration();
        config.LoadDefaults();
        return config;
    }

    [Theory]
    [InlineData(ELimb.SKULL, 0.85f)]
    [InlineData(ELimb.SPINE, 0.63f)]
    [InlineData(ELimb.LEFT_ARM, 0.9f)]
    [InlineData(ELimb.RIGHT_HAND, 0.9f)]
    [InlineData(ELimb.LEFT_LEG, 0.9f)]
    [InlineData(ELimb.RIGHT_FOOT, 0.9f)]
    public void For_GivesEveryPlayerAFullMilitarySet(ELimb limb, float expected) {
        Assert.Equal(expected, MilitaryArmor.For(limb, Defaults()), 3);
    }

    [Theory]
    [InlineData(ELimb.LEFT_FRONT)]
    [InlineData(ELimb.RIGHT_BACK)]
    public void For_LeavesTheLimbsVanillaNeverArmoursAlone(ELimb limb) {
        Assert.Equal(1f, MilitaryArmor.For(limb, Defaults()), 3);
    }
}
