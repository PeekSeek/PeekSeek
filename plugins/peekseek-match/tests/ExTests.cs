using PeekSeek.Core;
using Xunit;

namespace PeekSeek.Tests;

public class ExTests {
    [Theory]
    [InlineData(100, 80)]
    [InlineData(5, 4)]
    [InlineData(0, 0)]
    [InlineData(1, 0)]
    [InlineData(-10, -8)]
    [InlineData(1000, 800)]
    public void ToFemaleSalary_AppliesEightyPercentWithTruncation(int input, int expected) {
        Assert.Equal(expected, input.ToFemaleSalary());
    }
}
