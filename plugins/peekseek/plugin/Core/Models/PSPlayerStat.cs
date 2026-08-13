namespace PeekSeek.Core.Models;

internal class PSPlayerStat {
    internal ulong SteamID;
    internal byte Wins;
    internal byte Kills;
    internal byte Deaths;
    internal byte Headshots;
    /// Every shot of theirs that connected, and how many of those landed on the head.
    /// Kept apart from Headshots, which counts headshot *kills*: in a mode where
    /// nothing dies to one bullet that number sits at zero for players who hit heads
    /// constantly, so it is the wrong thing to call accuracy. ushort because a long
    /// match easily passes 255 hits.
    internal ushort Hits;
    internal ushort HeadshotHits;
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
