using System;
using System.Collections.Generic;
using SDG.Unturned;
using Logger = Rocket.Core.Logging.Logger;

namespace PeekSeek.Hud;

/// The one lookup every screen starts with.
///
/// Unturned cannot draw a custom UI from plugin code: EffectManager.SendUIEffect takes
/// an EffectAsset, and an EffectAsset only exists inside a workshop bundle the client
/// has already downloaded. So each screen is a GUID in the config, and a GUID that is
/// set but does not resolve is worth one loud line in the console, because the
/// symptom otherwise is a screen that silently never appears.
///
/// A miss is never final. A plugin ticks from the moment Rocket loads it, which is
/// minutes before the workshop items are installed and their assets are loaded, so a
/// screen asked for early finds nothing that is there by the time anybody could see
/// it. Callers therefore keep the asset once it is found and ask again until then,
/// and this complains once per screen rather than on every attempt.
internal static class Effects {
    private static readonly HashSet<string> complained = [];

    internal static EffectAsset Find(string configured, string field, string consequence) {
        if (string.IsNullOrWhiteSpace(configured)) return null;

        if (!Guid.TryParse(configured, out Guid guid)) {
            if (complained.Add(field)) Logger.LogError($"{field} \"{configured}\" is not a GUID. {consequence}");
            return null;
        }

        EffectAsset asset = Assets.find<EffectAsset>(guid);
        if (asset == null && complained.Add(field)) {
            Logger.LogError($"No EffectAsset with GUID {guid} is loaded yet. If the assets are still loading this "
                + $"resolves itself; otherwise, is the workshop item in WorkshopDownloadConfig.json? {consequence}");
        }

        return asset;
    }
}
