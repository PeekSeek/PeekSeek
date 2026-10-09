using SDG.Unturned;

namespace PeekSeek;

/// Stamina that never runs out, and no bar for it. Compiled into the lobby as well.
///
/// There is no switch for infinite stamina, so it is topped back up faster than a
/// sprint can visibly drain it, which is indistinguishable from having none. With the
/// bar hidden nobody sees the top-ups either.
public static class Stamina {
    public const float TopUpIntervalSeconds = 0.5f;

    /// Stamina is a byte out of 100. serverModifyStamina replicates, so a bar that is
    /// only a sliver down is left alone rather than sent a packet nobody can see.
    private const byte TopUpBelow = 90;

    public static void HideBar(Player player) {
        player.setPluginWidgetFlag(EPluginWidgetFlags.ShowStamina, false);
    }

    public static void Refill() {
        foreach (SteamPlayer client in Provider.clients) {
            if (client?.player == null || client.player.life.isDead) continue;
            if (client.player.life.stamina >= TopUpBelow) continue;

            client.player.life.serverModifyStamina(byte.MaxValue);
        }
    }
}
