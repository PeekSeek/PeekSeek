using System.Collections.Generic;
using System.Linq;
using SDG.NetTransport;
using SDG.Unturned;
using UnityEngine;

namespace PeekSeek.Hud;

public class DeathCardSettings {
    public string EffectGUID;
    public short EffectKey;

    /// Where a weapon's picture comes from, with `{0}` standing for the item id.
    public string ItemIconURL;

    /// How long the card stays up.
    public float Seconds;
}

/// One death, as the card says it. `Killer` is null for a death nobody dealt, and
/// then `Cause` is the label instead: FELL, BLED OUT, OPEN GROUND.
public class DeathInfo {
    public string Killer;
    public string Cause;
    public ushort Weapon;
    public bool Headshot;

    /// Metres between the two, or -1 for a death with no distance to it.
    public int Distance = -1;

    /// The one sentence under the top line: what they had left and what you did to
    /// them. Null for nothing.
    public string Line;
}

/// The death card at the bottom of everybody's screen, sent to the one it happened
/// to: who, with what, from how far, what they had left and what you did to them.
/// The server has every number for the site's kill log already; this is the same
/// record, shown to the person it is about while it still matters.
///
/// The respawn count lives in the same effect, in the middle of the screen, for a
/// server that brings the dead back on a clock. A match server never turns it on.
public class DeathCard {
    private readonly DeathCardSettings settings;
    private EffectAsset effect;
    private bool effectLookedUp;

    /// Per viewer: when their card comes down, and whether the count is on. Absent
    /// means nothing of this effect is on their screen.
    private sealed class Shown {
        internal float CardUntil;
        internal bool Respawn;
    }

    private readonly Dictionary<ulong, Shown> shown = [];

    public DeathCard(DeathCardSettings settings) {
        this.settings = settings;
    }

    public void Show(SteamPlayer viewer, DeathInfo info) {
        EffectAsset asset = Effect();
        if (asset == null) return;

        Shown state = Sent(viewer, asset);
        state.CardUntil = Time.realtimeSinceStartup + settings.Seconds;

        short key = settings.EffectKey;
        ITransportConnection to = viewer.transportConnection;
        bool duel = info.Killer != null;

        EffectManager.sendUIEffectVisibility(key, to, true, "Card", true);
        EffectManager.sendUIEffectText(key, to, true, "Card_Label", duel ? "KILLED BY" : (info.Cause ?? "DIED").ToUpperInvariant());
        EffectManager.sendUIEffectVisibility(key, to, true, "Card_Killer", duel);
        if (duel) EffectManager.sendUIEffectText(key, to, true, "Card_Killer", info.Killer);
        EffectManager.sendUIEffectVisibility(key, to, true, "Card_Weapon", info.Weapon != 0);
        if (info.Weapon != 0) EffectManager.sendUIEffectImageURL(key, to, true, "Card_Weapon", string.Format(settings.ItemIconURL, info.Weapon));
        EffectManager.sendUIEffectVisibility(key, to, true, "Card_Headshot", info.Headshot);
        EffectManager.sendUIEffectVisibility(key, to, true, "Card_Distance", info.Distance >= 0);
        if (info.Distance >= 0) EffectManager.sendUIEffectText(key, to, true, "Card_Distance", $"{info.Distance} m");
        EffectManager.sendUIEffectText(key, to, true, "Card_Line", info.Line ?? "");
    }

    /// The count in the middle of the screen, with this many seconds on it. Sent
    /// again each second by the caller; the number is all that changes.
    public void ShowRespawn(SteamPlayer viewer, int seconds) {
        EffectAsset asset = Effect();
        if (asset == null) return;

        Shown state = Sent(viewer, asset);
        short key = settings.EffectKey;
        if (!state.Respawn) {
            state.Respawn = true;
            EffectManager.sendUIEffectVisibility(key, viewer.transportConnection, true, "Respawn", true);
        }
        EffectManager.sendUIEffectText(key, viewer.transportConnection, true, "Respawn_Count", seconds.ToString());
    }

    public void HideRespawn(SteamPlayer viewer) {
        if (!shown.TryGetValue(viewer.playerID.steamID.m_SteamID, out Shown state) || !state.Respawn) return;

        state.Respawn = false;
        Settle(viewer, state);
    }

    /// Let cards that have had their time go.
    public void Tick() {
        float now = Time.realtimeSinceStartup;
        foreach (ulong steam64 in shown.Keys.ToList()) {
            Shown state = shown[steam64];
            if (state.CardUntil == 0 || now < state.CardUntil) continue;

            state.CardUntil = 0;
            SteamPlayer viewer = PlayerTool.getSteamPlayer(new Steamworks.CSteamID(steam64));
            if (viewer == null) {
                shown.Remove(steam64);
                continue;
            }
            Settle(viewer, state);
        }
    }

    public void Forget(ulong steam64) => shown.Remove(steam64);

    public void Clear() {
        shown.Clear();
        EffectAsset asset = Effect();
        if (asset == null) return;

        foreach (SteamPlayer viewer in Provider.clients.ToList()) {
            EffectManager.askEffectClearByID(asset.id, viewer.transportConnection);
        }
    }

    /// The effect on the viewer's screen, sent fresh if it was not, with both parts
    /// hidden until something turns one on.
    private Shown Sent(SteamPlayer viewer, EffectAsset asset) {
        ulong steam64 = viewer.playerID.steamID.m_SteamID;
        if (shown.TryGetValue(steam64, out Shown state)) return state;

        state = new Shown();
        shown[steam64] = state;
        EffectManager.SendUIEffect(asset, settings.EffectKey, viewer.transportConnection, true);
        EffectManager.sendUIEffectVisibility(settings.EffectKey, viewer.transportConnection, true, "Card", false);
        EffectManager.sendUIEffectVisibility(settings.EffectKey, viewer.transportConnection, true, "Respawn", false);
        return state;
    }

    /// Hide whatever part went, and take the whole effect down once nothing is left.
    private void Settle(SteamPlayer viewer, Shown state) {
        short key = settings.EffectKey;
        if (state.CardUntil == 0 && !state.Respawn) {
            shown.Remove(viewer.playerID.steamID.m_SteamID);
            EffectManager.askEffectClearByID(Effect().id, viewer.transportConnection);
            return;
        }

        EffectManager.sendUIEffectVisibility(key, viewer.transportConnection, true, "Card", state.CardUntil != 0);
        EffectManager.sendUIEffectVisibility(key, viewer.transportConnection, true, "Respawn", state.Respawn);
    }

    private EffectAsset Effect() {
        if (effectLookedUp) return effect;
        effectLookedUp = true;

        effect = Effects.Find(settings.EffectGUID, "DeathCardEffectGUID", "No death card.");
        return effect;
    }
}
