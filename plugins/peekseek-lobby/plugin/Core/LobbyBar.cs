using System.Collections.Generic;
using System.Linq;
using PeekSeek.Hud;
using SDG.Unturned;
using UnityEngine;

namespace PeekSeekLobby.Core;

/// One player on the bar: who, whether they are out in the arena, whether they have
/// stepped away, and their kills this arena.
internal class LobbyFace {
    internal ulong Steam64;
    internal bool Playing;
    internal bool Away;
    internal int Kills;
}

/// What the bar says at one moment: the faces in the order they go round the block,
/// and the block's two cells. A null cell is off, and the row closes up round it.
internal class LobbyView {
    internal List<LobbyFace> Faces = [];

    /// The queue cell: its word and its clock. QUEUE with the time waited, MATCH
    /// FOUND with the seconds until the pull, AFK with nothing.
    internal string State;
    internal string StateTimer;

    /// The arena cell's clock: the time until the reset, or null while the arena is
    /// not counting.
    internal string ArenaTimer;
}

/// The bar along the top of the lobby: the block in the middle with the queue and the
/// arena's clock on it, and a face per player either side of it, six a side, read left
/// to right by kills: the most on the far left, the fewest on the far right. A face at
/// full strength is somebody out in the arena; at half strength somebody who is not:
/// dead, waiting at the spawn, which the game calls being safe, or away, which gets its
/// own mark. Each face carries its kills on its bottom-left corner.
///
/// Redrawn whole for every viewer whenever a face changes, like the match bar; the
/// block's cells are patched on their own when only a clock moved. Checked twice a
/// second, because stepping out of the safezone is not an event.
internal class LobbyBar {
    private const int Seats = 6;

    private readonly Dictionary<ulong, string> avatars = [];
    private string drawnFaces;
    private string drawnBlock;

    private EffectAsset effect;

    internal void Picture(ulong steam64, string url) {
        avatars[steam64] = url;
        drawnFaces = null;
    }

    internal string AvatarOf(ulong steam64) {
        return avatars.TryGetValue(steam64, out string url) ? url : PeekSeekLobby.Config.DefaultAvatarURL;
    }

    internal void Left(ulong steam64) {
        avatars.Remove(steam64);
        drawnFaces = null;
    }

    /// Draw what has changed since the last call: the whole bar when a face has, the
    /// block's cells when only they have.
    internal void Show(LobbyView view) {
        EffectAsset asset = Effect();
        if (asset == null) return;

        string faces = string.Join(";", view.Faces.Select(face => $"{face.Steam64}:{face.Playing}:{face.Away}:{face.Kills}"));
        string block = $"{view.State}|{view.StateTimer}|{view.ArenaTimer}";

        if (faces != drawnFaces) {
            foreach (SteamPlayer viewer in Provider.clients.ToList()) Draw(viewer, asset, view);
            drawnFaces = faces;
            drawnBlock = block;
            return;
        }

        if (block == drawnBlock) return;
        drawnBlock = block;
        foreach (SteamPlayer viewer in Provider.clients.ToList()) DrawBlock(viewer, view);
    }

    /// Everybody gets the whole bar again, for a viewer who has just arrived.
    internal void Redraw() => drawnFaces = null;

    internal void Hide() {
        EffectAsset asset = Effect();
        drawnFaces = null;
        if (asset == null) return;

        foreach (SteamPlayer viewer in Provider.clients.ToList()) {
            EffectManager.askEffectClearByID(asset.id, viewer.transportConnection);
        }
    }

    private void Draw(SteamPlayer viewer, EffectAsset asset, LobbyView view) {
        short key = PeekSeekLobby.Config.LobbyBarKey;
        EffectManager.SendUIEffect(asset, key, viewer.transportConnection, true);
        DrawBlock(viewer, view);

        List<LobbyFace> faces = view.Faces.Take(Seats * 2).ToList();
        int leftOfBlock = Mathf.Min(Seats, (faces.Count + 1) / 2);
        for (int seat = 0; seat < Seats; seat++) {
            Visible(viewer, $"L{seat}", seat < leftOfBlock);
            Visible(viewer, $"R{seat}", seat < faces.Count - leftOfBlock);
        }

        for (int i = 0; i < faces.Count; i++) {
            // Seats are numbered outward from the block on both sides, so the left
            // side is filled from its far end to put the order in reading order.
            string slot = i < leftOfBlock ? $"L{leftOfBlock - 1 - i}" : $"R{i - leftOfBlock}";
            LobbyFace face = faces[i];

            string picture = avatars.TryGetValue(face.Steam64, out string url) ? url : PeekSeekLobby.Config.DefaultAvatarURL;
            EffectManager.sendUIEffectImageURL(key, viewer.transportConnection, true, $"{slot}_Avatar", picture);
            EffectManager.sendUIEffectImageURL(key, viewer.transportConnection, true, $"{slot}_Faded", picture);
            Visible(viewer, $"{slot}_Avatar", face.Playing);
            Visible(viewer, $"{slot}_Faded", !face.Playing);
            Visible(viewer, $"{slot}_Away", face.Away);
            Visible(viewer, $"{slot}_Kills", face.Kills > 0);
            if (face.Kills > 0) Send(viewer, $"{slot}_Kills", face.Kills.ToString());
        }
    }

    private static void DrawBlock(SteamPlayer viewer, LobbyView view) {
        bool state = view.State != null;
        bool arena = view.ArenaTimer != null;
        Visible(viewer, "State", state);
        Visible(viewer, "State_Timer", state && view.StateTimer != null);
        Visible(viewer, "Gap", state && arena);
        Visible(viewer, "Arena", arena);
        Visible(viewer, "Arena_Timer", arena);
        if (state) Send(viewer, "State", view.State);
        if (state && view.StateTimer != null) Send(viewer, "State_Timer", view.StateTimer);
        if (arena) Send(viewer, "Arena_Timer", view.ArenaTimer);
    }

    /// m:ss, for both clocks.
    internal static string Clock(float seconds) {
        int whole = Mathf.Max(0, Mathf.CeilToInt(seconds));
        return $"{whole / 60}:{whole % 60:00}";
    }

    private static void Send(SteamPlayer viewer, string child, string text) {
        EffectManager.sendUIEffectText(PeekSeekLobby.Config.LobbyBarKey, viewer.transportConnection, true, child, text ?? "");
    }

    private static void Visible(SteamPlayer viewer, string child, bool visible) {
        EffectManager.sendUIEffectVisibility(PeekSeekLobby.Config.LobbyBarKey, viewer.transportConnection, true, child, visible);
    }

    private EffectAsset Effect() {
        if (effect != null) return effect;

        effect = Effects.Find(PeekSeekLobby.Config.LobbyBarEffectGUID, "LobbyBarEffectGUID", "No lobby bar.");
        return effect;
    }
}
