using Xunit;

namespace PeekSeekArena.Tests;

public class PeekSeekArenaConfigurationTests {
    /// A config with no spawns puts nobody anywhere, so the defaults must at least name one.
    [Fact]
    public void LoadDefaults_NamesASpawn() {
        var config = new PeekSeekArenaConfiguration();

        config.LoadDefaults();

        Assert.NotEmpty(config.Spawns);
    }
}
