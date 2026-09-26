using System.Collections.Generic;
using System.Linq;
using PeekSeek.Hud;
using PeekSeekLobby.Core.Models;
using SDG.Unturned;

namespace PeekSeekLobby.Core;

/// What one player has chosen on the queue screen, and what they are queued with once
/// they say go. Kept per player rather than per server: two people standing in the
/// same lobby can want different gamemodes in different regions.
internal class QueueChoice {
    internal readonly HashSet<int> Modes = [];
    internal int Region;

    /// The bitmask the site queues on, which is what the lit tiles add up to. A tile
    /// carries the bit the site gave it rather than its place on screen.
    internal ushort Bitmask {
        get {
            List<QueueGamemode> listed = LobbyManager.Gamemodes ?? [];
            ushort mask = 0;
            foreach (int tile in Modes) {
                QueueGamemode mode = listed.ElementAtOrDefault(tile);
                if (mode != null) mask |= (ushort)(1 << mode.Bit);
            }
            return mask;
        }
    }

    /// The region code the site knows, or the server's own when the list is short.
    internal string RegionCode {
        get {
            List<QueueRegion> regions = PeekSeekLobby.Config.QueueRegions ?? [];
            QueueRegion chosen = regions.ElementAtOrDefault(Region);
            return chosen?.Code ?? PeekSeekLobby.Config.szRegion;
        }
    }
}

/// The site's play page, asked in game: which gamemodes you are up for, which region,
/// and a button that says go. A player who has queued on the site is looking at the
/// same tiles with the same selected state, because the one thing worse than asking
/// twice is asking differently.
///
/// Several gamemodes can be lit at once, since the site queues a bitmask; one region
/// is. The screen stays up while they choose and goes when they answer either way.
internal class QueueBoard {
    private const int Tiles = 6;
    private const int Regions = 4;

    /// What each player queues with. Kept after the screen closes, because it is what
    /// they are queued with and what /q opens on the next time.
    private readonly Dictionary<ulong, QueueChoice> choices = [];

    /// Who has the screen up, and what its button last said to them.
    private readonly Dictionary<ulong, string> open = [];
    private EffectAsset effect;

    internal bool IsUp(ulong steam64) => open.ContainsKey(steam64);

    /// This player's choice, or the one the screen opens on for somebody who has not
    /// made one. Only remembered once the site has listed the gamemodes, or a choice
    /// made before then would stay empty for the rest of their session.
    internal QueueChoice ChoiceOf(ulong steam64) {
        if (choices.TryGetValue(steam64, out QueueChoice choice)) return choice;

        choice = Started();
        if (LobbyManager.Gamemodes != null) choices[steam64] = choice;
        return choice;
    }

    /// Put the screen up on what they are already queued with. `line` is what to say
    /// about their ping, or null when it costs them nothing here; `waited` is their
    /// queue clock, or null when they are not queueing.
    internal void Open(SteamPlayer viewer, string line, string waited) {
        EffectAsset asset = Effect();
        if (asset == null) return;

        ulong steam64 = viewer.playerID.steamID.m_SteamID;
        QueueChoice choice = ChoiceOf(steam64);
        open.Remove(steam64);

        short key = PeekSeekLobby.Config.QueueKey;
        EffectManager.SendUIEffect(asset, key, viewer.transportConnection, true);
        Send(viewer, "Queue_Headline", PeekSeekLobby.Instance.Translate("queue_headline"));
        Visible(viewer, "Queue_Hint", false);
        ShowWait(viewer, waited);

        Visible(viewer, "Queue_Line", line != null);
        if (line != null) Send(viewer, "Queue_Line", line);

        List<QueueGamemode> modes = LobbyManager.Gamemodes ?? [];
        for (int i = 0; i < Tiles; i++) {
            QueueGamemode mode = modes.ElementAtOrDefault(i);
            Visible(viewer, $"BoardMode{i}", mode != null);
            if (mode == null) continue;

            Send(viewer, $"Mode{i}_Name", mode.Name);
            Send(viewer, $"Mode{i}_Blurb", mode.Blurb);
        }

        List<QueueRegion> regions = PeekSeekLobby.Config.QueueRegions ?? [];
        for (int i = 0; i < Regions; i++) {
            QueueRegion region = regions.ElementAtOrDefault(i);
            Visible(viewer, $"BoardRegion{i}", region != null);
            if (region == null) continue;

            Send(viewer, $"Region{i}_Name", region.Name);
        }

        DrawChosen(viewer, choice);

        // A tile cannot be clicked without a cursor, and the game only shows one while
        // a plugin says it is modal.
        viewer.player.setPluginWidgetFlag(EPluginWidgetFlags.Modal, true);
    }

    /// The button, while the screen is up: FIND MATCH, or the time they have queued
    /// in fixed-width digits, the way the site's button counts. Sent only when it
    /// changes, because it is asked for twice a second.
    internal void ShowWait(SteamPlayer viewer, string waited) {
        ulong steam64 = viewer.playerID.steamID.m_SteamID;
        if (open.TryGetValue(steam64, out string drawn) && drawn == waited) return;
        open[steam64] = waited;

        Send(viewer, "Queue_Go", waited == null
            ? PeekSeekLobby.Instance.Translate("queue_go")
            : $"<mspace=0.6em>{waited}</mspace>");
        Send(viewer, "Queue_No", PeekSeekLobby.Instance.Translate(waited == null ? "queue_no" : "queue_leave"));
    }

    /// What the screen opens on: the server's own region, and every ranked mode. A
    /// player who just wants a match can press go.
    private static QueueChoice Started() {
        QueueChoice choice = new();
        List<QueueRegion> regions = PeekSeekLobby.Config.QueueRegions ?? [];
        int here = regions.FindIndex(region => region.Code == PeekSeekLobby.Config.szRegion);
        choice.Region = here < 0 ? 0 : here;

        List<QueueGamemode> modes = LobbyManager.Gamemodes ?? [];
        for (int i = 0; i < modes.Count; i++) {
            if (modes[i].Ranked) choice.Modes.Add(i);
        }
        return choice;
    }

    /// Which tiles are lit. Sent on every click, because a click is the only thing
    /// that changes them.
    private static void DrawChosen(SteamPlayer viewer, QueueChoice choice) {
        for (int i = 0; i < Tiles; i++) Visible(viewer, $"Mode{i}_On", choice.Modes.Contains(i));
        for (int i = 0; i < Regions; i++) Visible(viewer, $"Region{i}_On", choice.Region == i);
    }

    /// A click: true for go, false for not now, and null for anything else, including
    /// a tile, which only changes what is lit.
    internal bool? Answer(SteamPlayer viewer, string button) {
        ulong steam64 = viewer.playerID.steamID.m_SteamID;
        if (button == null || !IsUp(steam64)) return null;
        QueueChoice choice = ChoiceOf(steam64);

        if (button == "BoardQueue") {
            // Nothing lit is not a queue. The site would take a zero bitmask and never
            // match them, which from in game looks exactly like matchmaking being broken.
            if (choice.Modes.Count == 0) return null;
            Close(viewer);
            return true;
        }

        if (button == "BoardQueueNo") {
            Close(viewer);
            return false;
        }

        if (button.StartsWith("BoardMode") && int.TryParse(button.Substring("BoardMode".Length), out int mode)) {
            if (!choice.Modes.Remove(mode)) choice.Modes.Add(mode);
            DrawChosen(viewer, choice);
        } else if (button.StartsWith("BoardRegion") && int.TryParse(button.Substring("BoardRegion".Length), out int region)) {
            choice.Region = region;
            DrawChosen(viewer, choice);
        }
        return null;
    }

    /// The screen goes; the choice stays, because it is what they are queued with.
    internal void Close(SteamPlayer viewer) {
        EffectAsset asset = Effect();
        open.Remove(viewer.playerID.steamID.m_SteamID);
        if (asset != null) EffectManager.askEffectClearByID(asset.id, viewer.transportConnection);
        viewer.player.setPluginWidgetFlag(EPluginWidgetFlags.Modal, false);
    }

    internal void Forget(ulong steam64) {
        choices.Remove(steam64);
        open.Remove(steam64);
    }

    private static void Send(SteamPlayer viewer, string child, string text) {
        EffectManager.sendUIEffectText(PeekSeekLobby.Config.QueueKey, viewer.transportConnection, true, child, text ?? "");
    }

    private static void Visible(SteamPlayer viewer, string child, bool visible) {
        EffectManager.sendUIEffectVisibility(PeekSeekLobby.Config.QueueKey, viewer.transportConnection, true, child, visible);
    }

    private EffectAsset Effect() {
        if (effect != null) return effect;

        effect = Effects.Find(PeekSeekLobby.Config.QueueEffectGUID, "QueueEffectGUID", "Nobody is asked to queue.");
        return effect;
    }
}
