using System.Collections.Generic;
using System.Linq;
using SDG.Unturned;

namespace PeekSeek.Hud;

/// The list beside the screen while a team builds: every piece it still has, with
/// its count. The housing planner shows what can go down; this shows how many.
///
/// Drawn whole on every change, like the bar. The one line it carries that the plugin
/// does not send is baked into the prefab: that salvaging a piece is how it is changed
/// or taken down, which nothing else on screen says.
public class BuildPanel {
    private const int Rows = 16;

    private EffectAsset effect;

    /// Draw the list, with the row for `laying` picked out in ink: the kind the
    /// builder is putting down right now.
    public void Show(SteamPlayer viewer, IEnumerable<(ushort Id, int Count)> rows, ushort laying) {
        EffectAsset asset = Effect();
        if (asset == null) return;

        short key = PeekSeek.Config.BuildPanelKey;
        List<(ushort Id, int Count)> shown = rows.Where(row => row.Count > 0).Take(Rows).ToList();

        EffectManager.SendUIEffect(asset, key, viewer.transportConnection, true);
        for (int i = 0; i < Rows; i++) {
            EffectManager.sendUIEffectVisibility(key, viewer.transportConnection, true, $"Row{i}", i < shown.Count);
            if (i >= shown.Count) continue;

            (ushort id, int count) = shown[i];
            string name = id == laying ? $"<color=#f2f2f2>{Name(id)}</color>" : Name(id);
            EffectManager.sendUIEffectText(key, viewer.transportConnection, true, $"Row{i}_Count", count.ToString());
            EffectManager.sendUIEffectText(key, viewer.transportConnection, true, $"Row{i}_Name", name);
            EffectManager.sendUIEffectImageURL(
                key, viewer.transportConnection, true, $"Row{i}_Icon", string.Format(PeekSeek.Config.ItemIconURL, id));
        }
    }

    public void Hide(SteamPlayer viewer) {
        EffectAsset asset = Effect();
        if (asset != null) EffectManager.askEffectClearByID(asset.id, viewer.transportConnection);
    }

    private static string Name(ushort id) {
        return (Assets.find(EAssetType.ITEM, id) as ItemAsset)?.itemName ?? id.ToString();
    }

    private EffectAsset Effect() {
        if (effect != null) return effect;

        effect = Effects.Find(PeekSeek.Config.BuildPanelEffectGUID, "BuildPanelEffectGUID", "No build panel.");
        return effect;
    }
}
