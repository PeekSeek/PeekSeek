using System;
using uTelemetry;
using Xunit;

namespace uTelemetry.Tests;

public class KillTrackerTests
{
    [Theory]
    [InlineData(-9.81f, 1.0f, 100f, 800f, -0.07664f)]
    [InlineData(-9.81f, 1.0f, 0f, 800f, 0f)]
    [InlineData(-9.81f, 2.0f, 100f, 800f, -0.15328f)]
    [InlineData(-9.81f, 0.0f, 100f, 800f, 0f)]
    public void CalculateBulletDrop_ReturnsExpectedDrop(
        float gravity,
        float gravityMultiplier,
        float distance,
        float velocity,
        float expectedDrop)
    {
        float actual = KillTracker.CalculateBulletDrop(gravity, gravityMultiplier, distance, velocity);

        Assert.Equal(expectedDrop, actual, precision: 4);
    }
}
