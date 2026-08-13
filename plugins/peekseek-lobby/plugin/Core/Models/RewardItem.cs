namespace PeekSeekLobby.Core.Models;

/// A consumable topped back up on every kill, capped so nobody can bank a pile
/// of them over a long life. Carried count is what's enforced, not how many are
/// granted, so a player still holding theirs gets nothing extra for the next kill.
public class RewardItem {
    public ushort ItemID { get; set; }
    public byte MaxCarried { get; set; }

    /// Whether players also start each life with this. Off means it has to be
    /// earned: the heal is a reward for a kill, the bounce is standard equipment.
    public bool GrantOnSpawn { get; set; }
}
