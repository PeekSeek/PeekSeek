using System.Linq;
using Rocket.Unturned.Events;
using Rocket.Unturned.Player;
using SDG.Unturned;
using Steamworks;
using UnityEngine;

namespace PeekSeek.Hud;

/// What a server draws the feed with. Handed in rather than read off a plugin's
/// config because this file is compiled into two plugins, the match server's and the
/// lobby's, and each keeps these in its own configuration.
public class KillFeedSettings {
    /// The GUID of the KillFeed effect in the PeekSeek Kill Feed workshop item. Left
    /// empty, the feed does nothing at all; there is no chat form of a kill feed worth
    /// having.
    public string EffectGUID;

    /// The channel the feed is sent on. Any small number no other plugin on the server
    /// is using; it only has to be stable, so the feed can be redrawn and cleared.
    public short EffectKey;

    /// Where a weapon's picture comes from, with `{0}` standing for the item id.
    public string ItemIconURL;

    /// How many rows the prefab has, how long a row stays, and how many characters
    /// of a name fit its column.
    public int Rows;
    public float RowSeconds;
    public int NameLength;
}

/// A kill feed at the top right of everybody's screen: who killed whom with what, the
/// weapon a white silhouette, a skull after it for a headshot, a roman numeral from a
/// player's second kill of a life, and a red hairline round any row you are in. A
/// death nobody dealt is the name on its own, with the thing that did it if the game
/// can name one.
///
/// A row is exactly as wide as what is in it. The prefab lays each row out as a run
/// of parts a fixed gap apart and sizes it to them, so a long name pushes the row
/// wider rather than off it, and a part that is switched off, the streak, the weapon,
/// the skull, the killer, takes no space. The plugin only ever says which parts are on
/// and what they say.
///
/// It knows nothing about matches or rounds. A streak is kills in one life, which in a
/// round-based mode is the same thing as a round, and in the lobby is what a streak
/// means anyway. The rows are redrawn whole for every viewer whenever they change:
/// six rows of a few messages a few times a minute is nothing, and it means the server
/// never has to know what a client still has on screen.
public class KillFeed {
    private readonly KillFeedSettings settings;
    private readonly Feed feed;
    private EffectAsset effect;
    private float nextExpiry;

    public KillFeed(KillFeedSettings settings) {
        this.settings = settings;
        feed = new Feed(settings.Rows, settings.RowSeconds);
    }

    public void Hook() {
        UnturnedPlayerEvents.OnPlayerDeath += OnDeath;
        UnturnedPlayerEvents.OnPlayerRevive += OnRevive;
        Provider.onServerDisconnected += OnLeft;
    }

    public void Unhook() {
        UnturnedPlayerEvents.OnPlayerDeath -= OnDeath;
        UnturnedPlayerEvents.OnPlayerRevive -= OnRevive;
        Provider.onServerDisconnected -= OnLeft;
    }

    /// A new round: every streak starts again.
    public void NewRound() => feed.EndRound();

    /// Once a second, let old rows go.
    public void Tick() {
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
            limb == ELimb.SKULL && killer != null,
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
    public static ushort Weapon(EDeathCause cause, Player killer) {
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
        short key = settings.EffectKey;

        if (!feed.Any) {
            EffectManager.askEffectClearByID(asset.id, viewer.transportConnection);
            return;
        }

        ulong me = viewer.playerID.steamID.m_SteamID;
        EffectManager.SendUIEffect(asset, key, viewer.transportConnection, true);

        for (int i = 0; i < settings.Rows; i++) {
            Kill kill = feed.Rows.ElementAtOrDefault(i);
            EffectManager.sendUIEffectVisibility(key, viewer.transportConnection, true, $"Row{i}", kill != null);
            if (kill == null) continue;

            // A death with nobody on the other end has no killer and no streak, and the
            // row closes up to the name and whatever did it.
            bool duel = kill.Killer != 0;
            string badge = Feed.Badge(kill.Streak);
            EffectManager.sendUIEffectVisibility(key, viewer.transportConnection, true, $"Row{i}_Killer", duel);
            if (duel) EffectManager.sendUIEffectText(key, viewer.transportConnection, true, $"Row{i}_Killer", Feed.Fit(kill.KillerName, settings.NameLength));
            EffectManager.sendUIEffectVisibility(key, viewer.transportConnection, true, $"Row{i}_Streak", badge.Length > 0);
            if (badge.Length > 0) EffectManager.sendUIEffectText(key, viewer.transportConnection, true, $"Row{i}_Streak", badge);
            EffectManager.sendUIEffectVisibility(key, viewer.transportConnection, true, $"Row{i}_Weapon", kill.Weapon != 0);
            if (kill.Weapon != 0) {
                EffectManager.sendUIEffectImageURL(key, viewer.transportConnection, true, $"Row{i}_Weapon", string.Format(settings.ItemIconURL, kill.Weapon));
            }
            EffectManager.sendUIEffectVisibility(key, viewer.transportConnection, true, $"Row{i}_Headshot", kill.Headshot);
            EffectManager.sendUIEffectText(key, viewer.transportConnection, true, $"Row{i}_Victim", Feed.Fit(kill.VictimName, settings.NameLength));
            EffectManager.sendUIEffectVisibility(key, viewer.transportConnection, true, $"Row{i}_Mine", Feed.Involves(kill, me));
        }
    }

    public void Clear() {
        EffectAsset asset = Effect();
        if (asset == null) return;

        foreach (SteamPlayer viewer in Provider.clients.ToList()) {
            EffectManager.askEffectClearByID(asset.id, viewer.transportConnection);
        }
    }

    private EffectAsset Effect() {
        if (effect != null) return effect;

        effect = Effects.Find(settings.EffectGUID, "KillFeedEffectGUID", "No kill feed.");
        return effect;
    }
}
