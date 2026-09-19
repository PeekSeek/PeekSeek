using System;
using System.Linq;
using Rocket.Unturned.Events;
using Rocket.Unturned.Player;
using SDG.Unturned;
using Steamworks;
using UnityEngine;
using Logger = Rocket.Core.Logging.Logger;

namespace PeekSeekKillFeed;

/// The feed on everybody's screen: what Unturned tells us about a death turned into a
/// row, and the rows redrawn whole for every viewer whenever they change.
///
/// Whole rather than patched, like the Siege HUD: six rows of five messages a few
/// times a minute is nothing, and it means the server never has to know what a client
/// still has on screen. The weapon pictures are cached by URL on the client, so a
/// redraw fetches nothing.
internal class KillFeed {
    private readonly Feed feed;
    private EffectAsset effect;
    private bool effectLookedUp;
    private float nextExpiry;

    internal KillFeed() {
        KillFeedConfiguration config = PeekSeekKillFeed.Config;
        feed = new Feed(config.Rows, config.RowSeconds);
    }

    internal void Hook() {
        UnturnedPlayerEvents.OnPlayerDeath += OnDeath;
        UnturnedPlayerEvents.OnPlayerRevive += OnRevive;
        Provider.onServerDisconnected += OnLeft;
    }

    internal void Unhook() {
        UnturnedPlayerEvents.OnPlayerDeath -= OnDeath;
        UnturnedPlayerEvents.OnPlayerRevive -= OnRevive;
        Provider.onServerDisconnected -= OnLeft;
    }

    /// Once a second, let old rows go.
    internal void Tick() {
        if (Time.realtimeSinceStartup < nextExpiry) return;
        nextExpiry = Time.realtimeSinceStartup + 1f;

        if (feed.Expire(Time.realtimeSinceStartup)) Redraw();
    }

    private void OnDeath(UnturnedPlayer victim, EDeathCause cause, ELimb limb, CSteamID murderer) {
        ulong victimId = victim.CSteamID.m_SteamID;
        SteamPlayer killer = murderer.m_SteamID == 0 || murderer.m_SteamID == victimId ? null : PlayerTool.getSteamPlayer(murderer);

        feed.EndLife(victimId);
        feed.Add(
            killer?.playerID.steamID.m_SteamID ?? 0,
            killer?.playerID.characterName ?? "",
            victimId,
            victim.CharacterName,
            Weapon(cause, killer?.player),
            Time.realtimeSinceStartup);

        Redraw();
    }

    /// A respawn is a new life as far as the streak is concerned; a death already
    /// ended the last one, so this only matters for a life that ended some other way.
    private void OnRevive(UnturnedPlayer player, Vector3 position, byte angle) {
        feed.EndLife(player.CSteamID.m_SteamID);
    }

    private void OnLeft(CSteamID steamID) {
        feed.EndLife(steamID.m_SteamID);
    }

    /// The item pictured for a kill. Unturned says how somebody died, not with what,
    /// so the gun and the melee are whatever the killer had in their hands at the
    /// moment, and the rest is the one item that can cause each of those deaths.
    /// 254 frag, 519 rocket launcher, 1241 demolition charge, 1101 landmine, 386
    /// barbed wire, 1373 sentry.
    private static ushort Weapon(EDeathCause cause, Player killer) {
        ushort inHand = killer?.equipment?.itemID ?? 0;

        return cause switch {
            EDeathCause.GUN or EDeathCause.MELEE => inHand,
            EDeathCause.GRENADE => killer?.equipment?.asset is ItemThrowableAsset ? inHand : (ushort)254,
            EDeathCause.MISSILE => killer?.equipment?.asset is ItemGunAsset ? inHand : (ushort)519,
            EDeathCause.CHARGE => 1241,
            EDeathCause.LANDMINE => 1101,
            EDeathCause.SHRED => 386,
            EDeathCause.SENTRY => 1373,
            _ => 0
        };
    }

    // ---- drawing --------------------------------------------------------------

    private void Redraw() {
        EffectAsset asset = Effect();
        if (asset == null) return;

        foreach (SteamPlayer viewer in Provider.clients.ToList()) Draw(viewer, asset);
    }

    private void Draw(SteamPlayer viewer, EffectAsset asset) {
        KillFeedConfiguration config = PeekSeekKillFeed.Config;
        short key = config.EffectKey;

        if (!feed.Any) {
            EffectManager.askEffectClearByID(asset.id, viewer.transportConnection);
            return;
        }

        ulong me = viewer.playerID.steamID.m_SteamID;
        EffectManager.SendUIEffect(asset, key, viewer.transportConnection, true);

        for (int i = 0; i < config.Rows; i++) {
            Kill kill = feed.Rows.ElementAtOrDefault(i);
            EffectManager.sendUIEffectVisibility(key, viewer.transportConnection, true, $"Row{i}", kill != null);
            if (kill == null) continue;

            EffectManager.sendUIEffectText(key, viewer.transportConnection, true, $"Row{i}_Killer", Feed.Fit(kill.KillerName, config.NameLength));
            EffectManager.sendUIEffectText(key, viewer.transportConnection, true, $"Row{i}_Victim", Feed.Fit(kill.VictimName, config.NameLength));
            EffectManager.sendUIEffectText(key, viewer.transportConnection, true, $"Row{i}_Streak", Feed.Badge(kill.Streak));
            EffectManager.sendUIEffectImageURL(key, viewer.transportConnection, true, $"Row{i}_Weapon",
                kill.Weapon == 0 ? "" : string.Format(config.ItemIconURL, kill.Weapon));
            EffectManager.sendUIEffectVisibility(key, viewer.transportConnection, true, $"Row{i}_Mine", Feed.Involves(kill, me));
        }
    }

    internal void Clear() {
        EffectAsset asset = Effect();
        if (asset == null) return;

        foreach (SteamPlayer viewer in Provider.clients.ToList()) {
            EffectManager.askEffectClearByID(asset.id, viewer.transportConnection);
        }
    }

    private EffectAsset Effect() {
        if (effectLookedUp) return effect;
        effectLookedUp = true;

        string configured = PeekSeekKillFeed.Config.EffectGUID;
        if (string.IsNullOrWhiteSpace(configured)) return null;

        if (!Guid.TryParse(configured, out Guid guid)) {
            Logger.LogError($"EffectGUID \"{configured}\" is not a GUID. No kill feed.");
            return null;
        }

        effect = Assets.find<EffectAsset>(guid);
        if (effect == null) {
            Logger.LogError($"No EffectAsset with GUID {guid} is loaded. Is the PeekSeek Kill Feed workshop item on the server? No kill feed.");
        }

        return effect;
    }
}
