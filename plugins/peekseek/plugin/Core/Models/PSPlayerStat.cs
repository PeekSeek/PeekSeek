namespace PeekSeek.Core.Models;

internal class PSPlayerStat {
    internal ulong SteamID;
    internal byte Wins;
    internal byte Kills;
    internal byte Deaths;
    internal byte Headshots;
    /// Raw damage dealt/taken this match, from every DamageTool.damagePlayerRequested this
    /// player was on either side of. Kept as float since ping reduction scales it
    /// fractionally; rounded only when reported.
    internal float DamageDealt;
    internal float DamageTaken;
    /// Set on death and cleared when the next round spawns. Tracked here because a
    /// killed player is respawned immediately, so nothing on PlayerLife or ArenaPlayer
    /// still says they are out of the round a frame later.
    internal bool IsDeadThisRound;
}
