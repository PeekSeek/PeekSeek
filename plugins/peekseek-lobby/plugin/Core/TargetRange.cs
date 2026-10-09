using System.Collections.Generic;
using PeekSeekLobby.Core.Models;
using Rocket.Unturned.Player;
using SDG.Unturned;
using Steamworks;
using UnityEngine;
using Logger = Rocket.Core.Logging.Logger;

namespace PeekSeekLobby.Core;

/// The solo warm-up: a row of mannequins that drop in one hit, timed, with a
/// personal best to chase.
///
/// It exists for the player who lands in an empty arena. A free-for-all with
/// nobody in it gives them nothing to do and they leave inside a minute, which is
/// often the same minute somebody else would have joined in. This is the something
/// to do, and it gets out of the way the moment it is not needed: the second a real
/// player connects the targets come down and the arena is an arena again.
internal static class TargetRange {
    /// A run and the mannequins standing for it. The run itself is kept free of
    /// UnityEngine so it can be tested; the transforms are the half that cannot be.
    private sealed class ActiveRange {
        internal RangeRun Run;
        internal readonly List<Transform> Targets = [];
    }

    private static readonly Dictionary<ulong, ActiveRange> Running = [];

    /// Best time per player, for as long as they stay connected. Not written to the
    /// site: a warm-up drill is not a stat, and a number that only has to outlive
    /// the session does not need a table behind it.
    private static readonly Dictionary<ulong, float> Bests = [];

    internal static void Init() {
        BarricadeManager.onDamageBarricadeRequested += OnBarricadeDamaged;
    }

    internal static void Shutdown() {
        BarricadeManager.onDamageBarricadeRequested -= OnBarricadeDamaged;
        StopAll(null);
        Bests.Clear();
    }

    internal static bool AnyRunning => Running.Count > 0;

    /// Starts a run, or clears one already going, the command is the same either
    /// way, so somebody who wants out does not have to shoot eight mannequins to
    /// get there.
    internal static void Toggle(UnturnedPlayer player) {
        ulong steam64 = player.CSteamID.m_SteamID;
        SteamPlayer client = player.Player.channel.owner;

        if (Running.ContainsKey(steam64)) {
            Stop(steam64);
            LobbyManager.Whisper(client, PeekSeekLobby.Instance.Translate("range_stopped"));
            return;
        }

        ItemBarricadeAsset target = TargetAsset();
        if (!PeekSeekLobby.Config.RangeEnabled || target == null) {
            LobbyManager.Whisper(client, PeekSeekLobby.Instance.Translate("range_unavailable"));
            return;
        }

        // The whole point is holding somebody who is on their own. With company
        // here there is a real fight to have, and a row of mannequins in the middle
        // of it is cover for whoever put them there.
        if (Provider.clients.Count > 1) {
            LobbyManager.Whisper(client, PeekSeekLobby.Instance.Translate(
                "range_not_alone", Provider.clients.Count - 1));
            return;
        }

        Start(player, steam64, client, target);
    }

    private static void Start(UnturnedPlayer player, ulong steam64, SteamPlayer client, ItemBarricadeAsset target) {
        PeekSeekLobbyConfiguration config = PeekSeekLobby.Config;
        ActiveRange active = new();

        Vector3 origin = player.Player.transform.position;
        float facing = player.Player.transform.rotation.eulerAngles.y;

        foreach (float offset in RangeRun.TargetAngles(config.RangeTargetCount, config.RangeSpreadDegrees)) {
            float yaw = facing + offset;
            Vector3 spot = origin + (Quaternion.Euler(0f, yaw, 0f) * Vector3.forward * config.RangeDistance);

            // Turned to look back down the line at the player, so every mannequin
            // presents the same silhouette however far round the arc it sits.
            Transform model = BarricadeManager.dropNonPlantedBarricade(
                new Barricade(target),
                spot,
                Quaternion.Euler(0f, yaw + 180f, 0f),
                0UL,
                0UL);

            if (model != null) active.Targets.Add(model);
        }

        if (active.Targets.Count == 0) {
            Logger.LogWarning(
                $"Target range could not place any of item {config.RangeTargetItemId}, "
                + "check RangeTargetItemId is a barricade this map allows.");
            LobbyManager.Whisper(client, PeekSeekLobby.Instance.Translate("range_unavailable"));
            return;
        }

        active.Run = new RangeRun(Time.realtimeSinceStartup, active.Targets.Count);
        Running[steam64] = active;

        LobbyManager.Whisper(client, PeekSeekLobby.Instance.Translate(
            "range_started", active.Targets.Count));
    }

    /// Clears a player's run and everything it put in the world. Safe to call for
    /// somebody who has no run, which is what lets every one of the several things
    /// that should end a run, a match, a second player, disconnecting, just call
    /// it rather than each checking first.
    internal static void Stop(ulong steam64) {
        if (!Running.TryGetValue(steam64, out ActiveRange active)) return;

        foreach (Transform target in active.Targets) Remove(target);
        Running.Remove(steam64);
    }

    /// Ends every run, optionally saying why. Used when somebody joins: the arena
    /// is back on, and the mannequins would otherwise be standing in it.
    internal static void StopAll(string translationKey) {
        foreach (ulong steam64 in new List<ulong>(Running.Keys)) {
            Stop(steam64);

            if (translationKey == null) continue;
            SteamPlayer client = UnturnedPlayer.FromCSteamID(new CSteamID(steam64))?.Player?.channel?.owner;
            if (client != null) LobbyManager.Whisper(client, PeekSeekLobby.Instance.Translate(translationKey));
        }
    }

    /// Drops a player's run and their best time when they leave, so neither is
    /// still here under the same Steam ID hours later.
    internal static void Forget(ulong steam64) {
        Stop(steam64);
        Bests.Remove(steam64);
    }

    private static void OnBarricadeDamaged(
        CSteamID instigator, Transform barricade, ref ushort pendingTotalDamage,
        ref bool shouldAllow, EDamageOrigin origin) {
        if (!Running.TryGetValue(instigator.m_SteamID, out ActiveRange active)) return;

        // Both the membership test and the removal: a mannequin already counted is
        // no longer in the list, so a second bullet arriving in the same frame
        // falls straight through rather than scoring twice.
        if (!active.Targets.Remove(barricade)) return;

        // One hit, one target. This is an aim drill, not a demolition job, so the
        // vanilla damage is cancelled and the mannequin taken out from here, its
        // 200 health never gets a say, and neither does the calibre being used.
        shouldAllow = false;
        Remove(barricade);

        UnturnedPlayer player = UnturnedPlayer.FromCSteamID(instigator);
        if (player == null) return;

        active.Run.Knock();
        if (!active.Run.IsComplete) {
            LobbyManager.Whisper(player.Player.channel.owner,
                PeekSeekLobby.Instance.Translate("range_hit", active.Run.Standing));
            return;
        }

        Finish(instigator.m_SteamID, active, player);
    }

    private static void Finish(ulong steam64, ActiveRange active, UnturnedPlayer player) {
        float elapsed = active.Run.Elapsed(Time.realtimeSinceStartup);
        Bests.TryGetValue(steam64, out float best);
        bool isBest = RangeRun.BeatsBest(elapsed, best);
        if (isBest) Bests[steam64] = elapsed;

        Running.Remove(steam64);

        LobbyManager.Whisper(player.Player.channel.owner, PeekSeekLobby.Instance.Translate(
            isBest ? "range_done_best" : "range_done",
            elapsed.ToString("0.0"),
            best.ToString("0.0")));
    }

    /// The configured target, or null when it is not a barricade this server can
    /// place. Checked before a run starts rather than trusted, because a bad id
    /// otherwise fails as eight silent no-ops and a timer that never ends.
    private static ItemBarricadeAsset TargetAsset() {
        return Assets.find(EAssetType.ITEM, PeekSeekLobby.Config.RangeTargetItemId) as ItemBarricadeAsset;
    }

    private static void Remove(Transform model) {
        if (model == null) return;

        BarricadeDrop drop = BarricadeManager.FindBarricadeByRootTransform(model);
        if (drop == null) return;
        if (!BarricadeManager.tryGetPlant(model, out byte x, out byte y, out ushort plant, out _)) return;

        BarricadeManager.destroyBarricade(drop, x, y, plant);
    }
}
