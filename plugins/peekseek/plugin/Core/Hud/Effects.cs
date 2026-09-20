using System;
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
internal static class Effects {
    internal static EffectAsset Find(string configured, string field, string consequence) {
        if (string.IsNullOrWhiteSpace(configured)) return null;

        if (!Guid.TryParse(configured, out Guid guid)) {
            Logger.LogError($"{field} \"{configured}\" is not a GUID. {consequence}");
            return null;
        }

        EffectAsset asset = Assets.find<EffectAsset>(guid);
        if (asset == null) {
            Logger.LogError($"No EffectAsset with GUID {guid} is loaded. Is the PeekSeek UI workshop item in "
                + $"WorkshopDownloadConfig.json? {consequence}");
        }

        return asset;
    }
}
