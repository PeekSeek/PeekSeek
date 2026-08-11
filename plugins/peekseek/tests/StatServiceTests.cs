using PeekSeek.Core;
using Steamworks;
using Xunit;

namespace PeekSeek.Tests;

[Collection("StatService")]
public class StatServiceTests {
    public StatServiceTests() {
        StatService.InstanceStats.Clear();
    }

    [Fact]
    public void UpdateStatWins_NewPlayer_GetReturnsOne() {
        var id = new CSteamID(76561198000000001UL);

        StatService.UpdateStatWins(id);

        Assert.Equal((byte)1, StatService.GetStatWins(id));
    }

    [Fact]
    public void UpdateStatKills_RepeatedCalls_Accumulate() {
        var id = new CSteamID(76561198000000002UL);

        StatService.UpdateStatKills(id);
        StatService.UpdateStatKills(id);
        StatService.UpdateStatKills(id);

        Assert.Equal((byte)3, StatService.GetStatKills(id));
    }

    [Fact]
    public void GetStatDeaths_UnknownPlayer_ReturnsZero() {
        var id = new CSteamID(76561198000000099UL);

        Assert.Equal((byte)0, StatService.GetStatDeaths(id));
    }

    [Fact]
    public void HasStats_UnknownPlayer_ReturnsFalse() {
        Assert.False(StatService.HasStats(new CSteamID(76561198000000030UL)));
    }

    [Fact]
    public void HasStats_AfterAnyUpdate_ReturnsTrue() {
        var id = new CSteamID(76561198000000031UL);
        StatService.UpdateStatDeaths(id);

        Assert.True(StatService.HasStats(id));
    }

    [Fact]
    public void IsAliveThisRound_UnknownPlayer_ReturnsTrue() {
        Assert.True(StatService.IsAliveThisRound(new CSteamID(76561198000000020UL)));
    }

    [Fact]
    public void MarkDeadThisRound_MakesPlayerNotAlive() {
        var id = new CSteamID(76561198000000021UL);

        StatService.MarkDeadThisRound(id);

        Assert.False(StatService.IsAliveThisRound(id));
    }

    [Fact]
    public void BeginRound_RevivesEveryoneWithoutLosingStats() {
        var id = new CSteamID(76561198000000022UL);
        StatService.UpdateStatDeaths(id);
        StatService.MarkDeadThisRound(id);

        StatService.BeginRound();

        Assert.True(StatService.IsAliveThisRound(id));
        Assert.Equal((byte)1, StatService.GetStatDeaths(id));
    }

    [Fact]
    public void GetTotalPlayedRounds_SumsWinsAcrossPlayers() {
        var a = new CSteamID(76561198000000010UL);
        var b = new CSteamID(76561198000000011UL);

        StatService.UpdateStatWins(a);
        StatService.UpdateStatWins(a);
        StatService.UpdateStatWins(b);
        StatService.UpdateStatWins(b);
        StatService.UpdateStatWins(b);

        Assert.Equal(5, StatService.GetTotalPlayedRounds());
    }
}
