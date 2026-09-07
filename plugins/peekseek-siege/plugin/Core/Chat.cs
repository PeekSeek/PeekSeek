using System.Collections.Generic;
using SDG.Unturned;
using UnityEngine;

namespace PeekSeekSiege.Core;

/// How this gamemode talks.
///
/// Every line a Siege server sends goes through here so it arrives looking like the
/// rest of PeekSeek: grey body text with `<color=white>` on the words a player is
/// actually reading for, and the same chat icon a match server uses. Rich text is on,
/// because the translations are written with it.
internal static class Chat {
    /// #a0a0a0, the grey the match servers and the warm-up lobby already send at.
    private static readonly Color Base = new(0.627f, 0.627f, 0.627f);

    internal static void Whisper(SteamPlayer to, string message) {
        ChatManager.serverSendMessage(
            message, Base, null, to, EChatMode.SAY, IconUrl(), true);
    }

    internal static void Tell(IEnumerable<SteamPlayer> audience, string message) {
        foreach (SteamPlayer viewer in audience) Whisper(viewer, message);
    }

    internal static void Announce(string message) {
        ChatManager.serverSendMessage(
            message, Base, null, null, EChatMode.SAY, IconUrl(), true);
    }

    /// A horizontal rule. The board and the round banners lean on this to separate
    /// themselves from the scroll of ordinary chat above them, which is the only
    /// grouping the chat window gives you.
    internal static string Rule => "<color=#4a4a4a>────────────────────────────</color>";

    private static string IconUrl() {
        return string.IsNullOrWhiteSpace(PeekSeekSiege.Config.ChatIconURL)
            ? null
            : PeekSeekSiege.Config.ChatIconURL;
    }
}
