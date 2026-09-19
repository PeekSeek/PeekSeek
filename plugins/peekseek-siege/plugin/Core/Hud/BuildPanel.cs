using System.Collections.Generic;
using System.Linq;
using SDG.Unturned;
using Logger = Rocket.Core.Logging.Logger;

namespace PeekSeekSiege.Core.Hud;

/// The list beside the screen during the build phase: every piece the team still has,
/// with its count. The housing planner shows what can go down; this shows how many.
///
/// Drawn whole on every change, like the match HUD. The one line it carries that the
/// plugin does not send is the hint about the swap key, which is baked into the
/// prefab because the game replaces `<plugin_0/>` with the player's own binding only
/// when the effect is spawned.
internal class BuildPanel {
    private const int Rows = 16;

    private EffectAsset effect;
    private bool effectLookedUp;

    internal void Show(SteamPlayer viewer, IEnumerable<(ushort Id, int Count)> rows) {
        EffectAsset asset = Effect();
        if (asset == null) return;

        short key = PeekSeekSiege.Config.BuildPanelKey;
        List<(ushort Id, int Count)> shown = rows.Where(row => row.Count > 0).Take(Rows).ToList();

        EffectManager.SendUIEffect(asset, key, viewer.transportConnection, true);
        for (int i = 0; i < Rows; i++) {
            EffectManager.sendUIEffectVisibility(key, viewer.transportConnection, true, $"Row{i}", i < shown.Count);
            if (i >= shown.Count) continue;

            (ushort id, int count) = shown[i];
            EffectManager.sendUIEffectText(key, viewer.transportConnection, true, $"Row{i}_Count", count.ToString());
            EffectManager.sendUIEffectText(key, viewer.transportConnection, true, $"Row{i}_Name", Name(id));
            EffectManager.sendUIEffectImageURL(
                key, viewer.transportConnection, true, $"Row{i}_Icon", string.Format(PeekSeekSiege.Config.ItemIconURL, id));
        }
    }

    internal void Hide(SteamPlayer viewer) {
        EffectAsset asset = Effect();
        if (asset != null) EffectManager.askEffectClearByID(asset.id, viewer.transportConnection);
    }

    private static string Name(ushort id) {
        return (Assets.find(EAssetType.ITEM, id) as ItemAsset)?.itemName ?? id.ToString();
    }

    private EffectAsset Effect() {
        if (effectLookedUp) return effect;
        effectLookedUp = true;

        string configured = PeekSeekSiege.Config.BuildPanelEffectGUID;
        if (string.IsNullOrWhiteSpace(configured)) return null;

        if (!System.Guid.TryParse(configured, out System.Guid guid)) {
            Logger.LogError($"BuildPanelEffectGUID \"{configured}\" is not a GUID. No build panel.");
            return null;
        }

        effect = Assets.find<EffectAsset>(guid);
        if (effect == null) Logger.LogError($"No EffectAsset with GUID {guid} is loaded. No build panel.");

        return effect;
    }
}
