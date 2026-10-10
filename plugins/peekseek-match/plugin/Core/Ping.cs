using SDG.Unturned;
using UnityEngine;

namespace PeekSeek.Core;

/// What a player's connection costs them, in milliseconds.
internal static class Ping {
    /// Unturned's own measurement, as the plugin used before 2.12. Reading Steam's
    /// connection status instead coincided with hits not registering in matches, so
    /// this is back on the figure the ping bands were set against.
    internal static int Milliseconds(SteamPlayer player) {
        return Mathf.RoundToInt(player.ping * 1000f);
    }
}
