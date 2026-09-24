using System.Linq;
using SDG.Unturned;
using UnityEngine;

namespace PeekSeek.Hud;

/// What a server draws the banner with. Handed in rather than read off a plugin's
/// config because this file is compiled into two plugins.
public class BannerSettings {
    public string EffectGUID;
    public short EffectKey;

    /// How long a banner stays up.
    public float Seconds;
}

/// The overlay's banner, in game: a small label over a large name and one line under
/// it, a quarter of the way down the screen, for a few seconds after a round or a
/// match. Whoever is on the server sees it; the words are the stream's, so a viewer
/// and a player read the same thing. Red is by rich text, for the match-winning one.
public class Banner {
    private readonly BannerSettings settings;
    private EffectAsset effect;
    private bool effectLookedUp;
    private float until;

    public Banner(BannerSettings settings) {
        this.settings = settings;
    }

    /// Put the banner up for everybody. `line` may be null for a banner with nothing
    /// under the name.
    public void Show(string label, string name, string line = null) {
        EffectAsset asset = Effect();
        if (asset == null) return;

        until = Time.realtimeSinceStartup + settings.Seconds;
        foreach (SteamPlayer viewer in Provider.clients.ToList()) Draw(viewer, asset, label, name, line);
    }

    /// The same, for one player: a banner whose words depend on who is reading it.
    public void ShowTo(SteamPlayer viewer, string label, string name, string line = null) {
        EffectAsset asset = Effect();
        if (asset == null) return;

        until = Time.realtimeSinceStartup + settings.Seconds;
        Draw(viewer, asset, label, name, line);
    }

    /// Once a second is often enough: a banner a few hundred milliseconds long is not
    /// a banner.
    public void Tick() {
        if (until == 0 || Time.realtimeSinceStartup < until) return;
        Clear();
    }

    public void Clear() {
        until = 0;
        EffectAsset asset = Effect();
        if (asset == null) return;

        foreach (SteamPlayer viewer in Provider.clients.ToList()) {
            EffectManager.askEffectClearByID(asset.id, viewer.transportConnection);
        }
    }

    private void Draw(SteamPlayer viewer, EffectAsset asset, string label, string name, string line) {
        short key = settings.EffectKey;
        EffectManager.SendUIEffect(asset, key, viewer.transportConnection, true);
        EffectManager.sendUIEffectText(key, viewer.transportConnection, true, "Banner_Label", label ?? "");
        EffectManager.sendUIEffectText(key, viewer.transportConnection, true, "Banner_Name", name ?? "");
        EffectManager.sendUIEffectText(key, viewer.transportConnection, true, "Banner_Line", line ?? "");
    }

    private EffectAsset Effect() {
        if (effectLookedUp) return effect;
        effectLookedUp = true;

        effect = Effects.Find(settings.EffectGUID, "BannerEffectGUID", "No banner.");
        return effect;
    }
}
