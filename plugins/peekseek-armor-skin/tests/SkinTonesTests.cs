using System.Collections.Generic;
using Xunit;

namespace PeekSeekArmorSkin.Tests;

/// IsInRange and For are absent here because they cannot run: RocketRedist's
/// Unturned assemblies want netstandard 2.1, which net48 cannot load, so any
/// test touching UnityEngine.Color throws at the first call.
public class SkinTonesTests {
    private static readonly List<int> Presets = [0, 2, 4, 7];

    [Theory]
    [InlineData(76561198344422592ul, 0)]
    [InlineData(76561198344422593ul, 2)]
    [InlineData(76561198344422594ul, 4)]
    [InlineData(76561198344422595ul, 7)]
    public void PresetFor_SpreadsConsecutiveSteamIdsAcrossAllFourTones(ulong steamId, int expected) {
        Assert.Equal(expected, SkinTones.PresetFor(steamId, Presets));
    }

    [Fact]
    public void PresetFor_IsTheSameEveryTimeForOnePlayer() {
        Assert.Equal(SkinTones.PresetFor(76561198344422592ul, Presets), SkinTones.PresetFor(76561198344422592ul, Presets));
    }

    [Fact]
    public void PresetFor_WrapsOnWhateverLengthTheConfigGives() {
        Assert.Equal(5, SkinTones.PresetFor(76561198344422593ul, [9, 5, 3]));
    }
}
