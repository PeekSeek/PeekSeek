namespace PeekSeekLobby.Core.Models;

/// A consumable handed out on spawn and topped back up on every kill, capped so
/// nobody can bank a pile of them over a long life. Carried count is what's
/// enforced, not how many are granted, so a player who still holds theirs gets
/// nothing extra for the next kill.
public class RewardItem {
    public ushort ItemID { get; set; }
    public byte MaxCarried { get; set; }
}
