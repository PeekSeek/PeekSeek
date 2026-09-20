using System.Collections.Generic;
using System.Linq;
using PeekSeek.Hud;
using SDG.Unturned;
using Steamworks;
using UnityEngine;

namespace PeekSeekLobby.Core;

/// The bar along the top of the lobby: the PeekSeek mark on the block and a face per
/// player round it, the first on the left, the second on the right, and so on, six a
/// side, in the order people arrived. A face at full strength is somebody out in the
/// arena; at half strength somebody who is not: dead, waiting at the spawn, which the
/// game calls being safe, or away. Nothing else, because a lobby has no teams, no
/// picks and no round.
///
/// Redrawn whole for every viewer whenever a face changes, like the match bar, and
/// checked twice a second, because stepping out of the safezone is not an event.
internal class LobbyBar {
    private const int Seats = 6;
    private const float CheckIntervalSeconds = 0.5f;

    private readonly List<ulong> arrived = [];
    private readonly Dictionary<ulong, string> avatars = [];
    private readonly Dictionary<ulong, bool> lastShownPlaying = [];

    private EffectAsset effect;
    private bool effectLookedUp;
    private float nextCheck;

    internal void Joined(ulong steam64) {
        if (!arrived.Contains(steam64)) arrived.Add(steam64);
        Show();
    }

    internal void Left(ulong steam64) {
        arrived.Remove(steam64);
        avatars.Remove(steam64);
        lastShownPlaying.Remove(steam64);
        Show();
    }

    internal void Picture(ulong steam64, string url) {
        avatars[steam64] = url;
        Show();
    }

    /// Whether a face is drawn at full strength: alive, out of the safezone, and here.
    private static bool IsPlaying(Player player, bool afk) {
        return !afk && !player.life.isDead && !player.movement.isSafe;
    }

    /// Redraw when anybody's state has moved since the last draw.
    internal void Tick(System.Func<ulong, bool> isAfk) {
        if (Time.realtimeSinceStartup < nextCheck) return;
        nextCheck = Time.realtimeSinceStartup + CheckIntervalSeconds;

        bool changed = false;
        foreach (SteamPlayer client in Provider.clients) {
            if (client.player == null) continue;

            ulong steam64 = client.playerID.steamID.m_SteamID;
            bool playing = IsPlaying(client.player, isAfk(steam64));
            if (lastShownPlaying.TryGetValue(steam64, out bool shown) && shown == playing) continue;

            changed = true;
        }

        if (changed) Show(isAfk);
    }

    internal void Show() => Show(_ => false);

    internal void Show(System.Func<ulong, bool> isAfk) {
        EffectAsset asset = Effect();
        if (asset == null) return;

        List<SteamPlayer> present = arrived
            .Select(steam64 => PlayerTool.getSteamPlayer(new CSteamID(steam64)))
            .Where(client => client?.player != null)
            .ToList();

        foreach (SteamPlayer viewer in Provider.clients.ToList()) {
            Draw(viewer, asset, present, isAfk);
        }

        lastShownPlaying.Clear();
        foreach (SteamPlayer client in present) {
            lastShownPlaying[client.playerID.steamID.m_SteamID] = IsPlaying(client.player, isAfk(client.playerID.steamID.m_SteamID));
        }
    }

    internal void Hide() {
        EffectAsset asset = Effect();
        if (asset == null) return;

        foreach (SteamPlayer viewer in Provider.clients.ToList()) {
            EffectManager.askEffectClearByID(asset.id, viewer.transportConnection);
        }
    }

    private void Draw(SteamPlayer viewer, EffectAsset asset, List<SteamPlayer> present, System.Func<ulong, bool> isAfk) {
        short key = PeekSeekLobby.Config.LobbyBarKey;
        EffectManager.SendUIEffect(asset, key, viewer.transportConnection, true);

        for (int i = 0; i < Seats * 2; i++) {
            string slot = $"{(i % 2 == 0 ? "L" : "R")}{i / 2}";
            SteamPlayer seat = present.ElementAtOrDefault(i);
            EffectManager.sendUIEffectVisibility(key, viewer.transportConnection, true, slot, seat != null);
            if (seat == null) continue;

            ulong steam64 = seat.playerID.steamID.m_SteamID;
            string picture = avatars.TryGetValue(steam64, out string url) ? url : PeekSeekLobby.Config.DefaultAvatarURL;
            bool playing = IsPlaying(seat.player, isAfk(steam64));

            EffectManager.sendUIEffectImageURL(key, viewer.transportConnection, true, $"{slot}_Avatar", picture);
            EffectManager.sendUIEffectImageURL(key, viewer.transportConnection, true, $"{slot}_Faded", picture);
            EffectManager.sendUIEffectVisibility(key, viewer.transportConnection, true, $"{slot}_Avatar", playing);
            EffectManager.sendUIEffectVisibility(key, viewer.transportConnection, true, $"{slot}_Faded", !playing);
        }
    }

    private EffectAsset Effect() {
        if (effectLookedUp) return effect;
        effectLookedUp = true;

        effect = Effects.Find(PeekSeekLobby.Config.LobbyBarEffectGUID, "LobbyBarEffectGUID", "No lobby bar.");
        return effect;
    }
}
