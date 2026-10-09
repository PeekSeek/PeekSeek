using System.Collections.Generic;

namespace PeekSeekLobby.Core.Models;

/// Paid out for reaching exactly KillsRequired kills in a row without dying and
/// without a long gap between them. The cooldown is per reward, so a player who
/// keeps hitting the same streak collects it at most once every CooldownSeconds
/// rather than every time.
public class StreakReward {
    public byte KillsRequired { get; set; }
    public float CooldownSeconds { get; set; }

    /// Announced to everyone when the streak lands. {0} is the player's name, {1}
    /// the kill count. Leave it empty to pay the streak out silently.
    public string szChatMessage { get; set; }

    public List<StreakItem> Items { get; set; }
}

/// One item id and how many of it. A magazine is fitted straight into the gun the
/// player is holding rather than dropped in their pack, and only if that gun can
/// chamber it, see LobbyManager.AttachMagazine.
public class StreakItem {
    public ushort ItemID { get; set; }
    public byte Amount { get; set; }
}
