using System;
using System.Linq;
using SDG.Unturned;

namespace PeekSeek.Hud;

/// Every sound PeekSeek plays, in the order ui/tools/sounds.py gives them effect ids.
public enum Sound {
    Kill1, Kill2, Kill3, Kill4, Kill5,
    RoundWin, RoundLoss, MatchPoint, MatchWin, MatchLoss,
    QueueJoin, QueueLeave, MatchFound,
    Count1, Count2, Count3, Count4, Count5, Go,
    Time10s, PickLock
}

/// Plays a sound to one player. A sound is an effect in the kill feed's bundle, which the
/// lobby and every match server carry, so this file is compiled into both plugins.
///
/// The ids are fixed rather than configured: Rocket does not add a new field to a config
/// a server already has, so a configured id would ship switched off. A server whose
/// bundle predates the sounds finds no asset and plays nothing, which is also what keeps
/// its clients from being kicked for an asset they do not have.
public static class Sounds {
    private const ushort FirstId = 43130;

    public static void Play(SteamPlayer viewer, Sound sound) {
        if (viewer?.player == null) return;
        if (Assets.find(EAssetType.EFFECT, (ushort)(FirstId + (int)sound)) is not EffectAsset asset) return;

        TriggerEffectParameters effect = new(asset) { position = viewer.player.transform.position, reliable = true };
        effect.SetRelevantPlayer(viewer);
        EffectManager.triggerEffect(effect);
    }

    public static void PlayToAll(Sound sound) {
        foreach (SteamPlayer viewer in Provider.clients.ToList()) Play(viewer, sound);
    }

    /// The kill ladder: one rung higher per kill this round, the fifth and on the top one.
    public static void Kill(SteamPlayer killer, int killsThisRound) {
        Play(killer, Sound.Kill1 + Math.Min(Math.Max(killsThisRound, 1), 5) - 1);
    }
}
