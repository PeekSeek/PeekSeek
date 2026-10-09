using System.Collections.Generic;
using SDG.NetTransport;
using SDG.Unturned;

namespace PeekSeek.Hud;

/// One short thing said at the bottom of a player's screen for as long as it is
/// true: a word, up to three pips counting something down, and a second word in red.
/// Raid says OFF BASE with it, the grace running out in the pips and the damage in
/// red after. Drawn whole on every change; there is nothing on it to drift.
public class Tag {
    private const int Pips = 3;

    private EffectAsset effect;
    private readonly HashSet<ulong> up = [];

    /// `pips` lit of three, or -1 for none; `hurt` in red after the word, or null.
    public void Show(SteamPlayer viewer, string text, int pips, string hurt) {
        EffectAsset asset = Effect();
        if (asset == null) return;

        short key = PeekSeek.Config.TagKey;
        ITransportConnection to = viewer.transportConnection;
        if (up.Add(viewer.playerID.steamID.m_SteamID)) EffectManager.SendUIEffect(asset, key, to, true);

        EffectManager.sendUIEffectText(key, to, true, "Tag_Text", text ?? "");
        for (int k = 0; k < Pips; k++) {
            EffectManager.sendUIEffectVisibility(key, to, true, $"Tag_Pip{k}", pips >= 0 && k < pips);
        }
        EffectManager.sendUIEffectVisibility(key, to, true, "Tag_Hurt", hurt != null);
        if (hurt != null) EffectManager.sendUIEffectText(key, to, true, "Tag_Hurt", hurt);
    }

    public void Hide(SteamPlayer viewer) {
        if (!up.Remove(viewer.playerID.steamID.m_SteamID)) return;

        EffectAsset asset = Effect();
        if (asset != null) EffectManager.askEffectClearByID(asset.id, viewer.transportConnection);
    }

    public void Forget(ulong steam64) => up.Remove(steam64);

    private EffectAsset Effect() {
        if (effect != null) return effect;

        effect = Effects.Find(PeekSeek.Config.TagEffectGUID, "TagEffectGUID", "No tag.");
        return effect;
    }
}
