using SDG.NetTransport.SteamNetworkingSockets;
using SDG.Unturned;
using Steamworks;
using UnityEngine;

namespace PeekSeekLobby.Core;

/// What a player's connection actually costs them, in milliseconds.
internal static class Ping {
    /// The same round trip the player reads off their own screen.
    ///
    /// Not <see cref="SteamPlayer.ping"/>, which is a different measurement wearing the
    /// same name. Unturned times its own request-and-answer through the game: the server
    /// stamps a packet, the client answers it in whichever frame it next reads messages,
    /// the server notices in whichever frame it next reads messages, and four of those
    /// samples a second apart are averaged. All of that lands on top of the network, so
    /// it reads far higher than the connection really is. A player on a genuine 55ms
    /// showed up here as 121ms.
    ///
    /// Steam is already measuring the real thing for every connection it carries, and
    /// that is the number the client's own display prefers too, so asking Steam is what
    /// makes /ping agree with what the player can see for themselves. Unturned reads the
    /// same connection the same way for a player's address, so a server where this is
    /// unavailable is one where Unturned itself has already given up; the fall back to
    /// its figure is for the servers not running Steam sockets at all.
    internal static int Milliseconds(SteamPlayer player) {
        if (player.transportConnection is TransportConnection_SteamNetworkingSockets connection) {
            SteamNetConnectionRealTimeStatus_t status = default;
            SteamNetConnectionRealTimeLaneStatus_t lanes = default;
            EResult read = SteamGameServerNetworkingSockets.GetConnectionRealTimeStatus(
                connection.steamConnectionHandle, ref status, 0, ref lanes);

            // Steam reports -1 until it has timed a round trip, which is most of a
            // fresh connection's first second.
            if (read == EResult.k_EResultOK && status.m_nPing >= 0) return status.m_nPing;
        }

        return Mathf.RoundToInt(player.ping * 1000f);
    }
}
