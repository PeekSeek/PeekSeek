using PeekSeek.Core;
using Steamworks;
using Xunit;

namespace PeekSeek.Tests;

[Collection("RoundLog")]
public class RoundLogTests {
    public RoundLogTests() {
        RoundLog.Clear();
    }

    [Fact]
    public void KillsBy_UnknownPlayer_ReturnsZero() {
        Assert.Equal(0, RoundLog.KillsBy(new CSteamID(76561198000000060UL)));
    }

    [Fact]
    public void RecordKill_IncrementsTheTally() {
        var killer = new CSteamID(76561198000000061UL);

        RoundLog.RecordKill(killer);
        RoundLog.RecordKill(killer);

        Assert.Equal(2, RoundLog.KillsBy(killer));
    }

    [Fact]
    public void RecordDamage_AccumulatesPerVictim() {
        var attacker = new CSteamID(76561198000000062UL);
        var victim = new CSteamID(76561198000000063UL);

        RoundLog.RecordDamage(attacker, victim, 20f);
        RoundLog.RecordDamage(attacker, victim, 15f);

        var entries = RoundLog.DamageBy(attacker);
        Assert.Single(entries);
        Assert.Equal(35f, entries[0].Damage);
        Assert.Equal(2, entries[0].Hits);
    }

    [Fact]
    public void RecordDamage_SeparatesEntriesPerVictim() {
        var attacker = new CSteamID(76561198000000064UL);
        var first = new CSteamID(76561198000000065UL);
        var second = new CSteamID(76561198000000066UL);

        RoundLog.RecordDamage(attacker, first, 10f);
        RoundLog.RecordDamage(attacker, second, 25f);

        Assert.Equal(2, RoundLog.DamageBy(attacker).Count);
    }

    [Fact]
    public void RecordDamage_UnattributedDamage_IsIgnored() {
        var victim = new CSteamID(76561198000000067UL);

        RoundLog.RecordDamage(CSteamID.Nil, victim, 10f);

        Assert.Empty(RoundLog.DamageBy(CSteamID.Nil));
    }

    [Fact]
    public void Clear_EmptiesKillsAndDamage() {
        var id = new CSteamID(76561198000000068UL);
        RoundLog.RecordKill(id);
        RoundLog.RecordDamage(id, new CSteamID(76561198000000069UL), 5f);

        RoundLog.Clear();

        Assert.Equal(0, RoundLog.KillsBy(id));
        Assert.Empty(RoundLog.DamageBy(id));
    }
}
