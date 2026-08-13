using PeekSeekLobby.Core;
using Xunit;

namespace PeekSeekLobby.Tests;

/// The name rule behind /stats <player> and /ms <player>. FindPlayer itself needs a
/// live client list, but the matching it depends on is pure and pinned here.
public class PlayerLookupTests {
    [Theory]
    [InlineData("RioDash", "rio")]
    [InlineData("RioDash", "RIO")]
    [InlineData("RioDash", "Dash")]
    [InlineData("RioDash", "RioDash")]
    [InlineData("RioDash", "r")]
    public void NameMatches_FindsCaseInsensitiveSubstrings(string name, string query) {
        Assert.True(LobbyManager.NameMatches(name, query));
    }

    [Theory]
    [InlineData("RioDash", "phantom")]
    [InlineData("RioDash", "RioDashing")]
    public void NameMatches_RejectsNonSubstrings(string name, string query) {
        Assert.False(LobbyManager.NameMatches(name, query));
    }

    /// Unturned hands back a null name for a client mid-disconnect, and a lookup
    /// running on the same tick must not take the whole command down with it.
    [Fact]
    public void NameMatches_NullNameIsNotAMatch() {
        Assert.False(LobbyManager.NameMatches(null, "rio"));
    }
}
