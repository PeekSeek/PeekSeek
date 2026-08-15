using PeekSeek.Core;
using Xunit;

namespace PeekSeek.Tests;

/// The bow half of the loadout snapshot. Everything else in LiveState reads a live
/// Player, but the state a weapon carries is plain bytes, and the overlay's arrow count
/// is only right if bytes 8, 9 and 10 mean what they are read as here.
public class LiveStateTests {
    /// The state the Movement powerup hands the crossbow out with: an explosive arrow
    /// (1209, low byte 185 and high byte 4) nocked, with one shot in it.
    private static readonly byte[] LoadedCrossbow =
        [146, 0, 0, 0, 0, 0, 0, 0, 185, 4, 1, 1, 1, 100, 100, 100, 100, 100];

    [Fact]
    public void NockedArrow_LoadedCrossbow_ReadsTheArrowId() {
        Assert.Equal((ushort)1209, LiveState.NockedArrow(LoadedCrossbow));
    }

    [Fact]
    public void NockedArrow_NoRoundsLeft_ReturnsZero() {
        byte[] spent = (byte[])LoadedCrossbow.Clone();
        spent[10] = 0;

        Assert.Equal((ushort)0, LiveState.NockedArrow(spent));
    }

    [Fact]
    public void NockedArrow_StateTooShortToHoldAmmo_ReturnsZero() {
        Assert.Equal((ushort)0, LiveState.NockedArrow([185, 4]));
    }

    [Fact]
    public void NockedArrow_NoState_ReturnsZero() {
        Assert.Equal((ushort)0, LiveState.NockedArrow(null));
    }
}
