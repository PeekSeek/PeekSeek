using System;
using System.Collections.Generic;
using PeekSeekSiege.Core.Models;
using SDG.Unturned;
using Steamworks;
using Logger = Rocket.Core.Logging.Logger;

namespace PeekSeekSiege.Core;

/// The five-option board a player answers between rounds.
///
/// There are two ways to draw it and only one set of rules behind them. Unturned
/// cannot draw a custom UI from plugin code: EffectManager.SendUIEffect takes an
/// EffectAsset, and an EffectAsset only exists inside a workshop bundle the client has
/// already downloaded. So the board is written against whichever is available. Give it
/// an effect GUID and it draws a real Unity layout with five clickable tiles; leave the
/// GUID empty and it prints the same five options to chat and takes /pick instead.
///
/// The chat form is not a placeholder that gets deleted later. A player whose client is
/// still downloading the bundle, or who has UI effects failing for any other reason,
/// has to be able to answer the board, and this is the answer for them.
internal class PickBoard {
    /// Everyone the board is currently up for, and what they have locked in. Absent
    /// means the board is not up for them; -1 means it is up and unanswered.
    private readonly Dictionary<ulong, int> picks = [];

    private List<SiegeOption> options = [];
    private string headline = "";
    private EffectAsset effect;
    private bool effectLookedUp;

    internal bool IsOpen { get; private set; }

    /// The option a player ended up with, defaulting to the first when they never
    /// answered. Defaulting rather than blocking is deliberate: a round must never
    /// wait on somebody who alt-tabbed.
    internal SiegeOption Chosen(ulong steam64) {
        if (options.Count == 0) return null;

        int index = picks.TryGetValue(steam64, out int chosen) && chosen >= 0 ? chosen : 0;
        return options[Math.Min(index, options.Count - 1)];
    }

    internal bool HasAnswered(ulong steam64) {
        return picks.TryGetValue(steam64, out int chosen) && chosen >= 0;
    }

    /// Put the board up for one set of players. `headline` is the one line above the
    /// options that says why they are choosing: which round it is and which side they
    /// are on.
    internal void Open(IEnumerable<SteamPlayer> audience, List<SiegeOption> choices, string headline) {
        options = choices;
        this.headline = headline;
        IsOpen = true;
        picks.Clear();

        foreach (SteamPlayer viewer in audience) {
            picks[viewer.playerID.steamID.m_SteamID] = -1;
            Draw(viewer);
        }
    }

    /// Record a pick, 1-based as the board shows it. Returns the option taken, or null
    /// when the number is not on the board.
    internal SiegeOption Answer(SteamPlayer player, int oneBased) {
        if (!IsOpen) return null;
        if (oneBased < 1 || oneBased > options.Count) return null;

        ulong steam64 = player.playerID.steamID.m_SteamID;
        if (!picks.ContainsKey(steam64)) return null;

        picks[steam64] = oneBased - 1;
        Refresh(player);
        return options[oneBased - 1];
    }

    /// Answer from a clicked Unity button. Buttons are named `SiegePick0` upward so a
    /// stray click on some other plugin's UI cannot land here.
    internal SiegeOption AnswerButton(SteamPlayer player, string buttonName) {
        if (!buttonName.StartsWith(ButtonPrefix)) return null;
        if (!int.TryParse(buttonName.Substring(ButtonPrefix.Length), out int index)) return null;

        return Answer(player, index + 1);
    }

    internal void Close() {
        if (!IsOpen) return;
        IsOpen = false;

        if (Effect() == null) return;
        foreach (SteamPlayer viewer in Provider.clients) {
            EffectManager.askEffectClearByID(Effect().id, viewer.transportConnection);
        }
    }

    /// Repaint the seconds left. Called about once a second while the board is up, so
    /// it only touches the one field that changed rather than resending the layout.
    internal void Countdown(int secondsLeft) {
        if (!IsOpen) return;

        EffectAsset asset = Effect();
        if (asset == null) return;

        foreach (ulong steam64 in picks.Keys) {
            SteamPlayer viewer = PlayerTool.getSteamPlayer(new CSteamID(steam64));
            if (viewer == null) continue;

            EffectManager.sendUIEffectText(
                PeekSeekSiege.Config.PickUIKey, viewer.transportConnection, true,
                "Timer", secondsLeft.ToString());
        }
    }

    private const string ButtonPrefix = "SiegePick";

    private void Draw(SteamPlayer viewer) {
        EffectAsset asset = Effect();
        if (asset == null) {
            DrawToChat(viewer);
            return;
        }

        EffectManager.SendUIEffect(asset, PeekSeekSiege.Config.PickUIKey, viewer.transportConnection, true);
        EffectManager.sendUIEffectText(
            PeekSeekSiege.Config.PickUIKey, viewer.transportConnection, true, "Headline", headline);

        for (int i = 0; i < options.Count; i++) {
            EffectManager.sendUIEffectText(
                PeekSeekSiege.Config.PickUIKey, viewer.transportConnection, true,
                $"Option{i}_Name", options[i].Name);
            EffectManager.sendUIEffectText(
                PeekSeekSiege.Config.PickUIKey, viewer.transportConnection, true,
                $"Option{i}_Blurb", options[i].Blurb);
        }
    }

    /// Mark the taken tile, or say what was taken. Only the state that changed, so a
    /// player who picks does not get the whole board again.
    private void Refresh(SteamPlayer player) {
        int index = picks[player.playerID.steamID.m_SteamID];
        EffectAsset asset = Effect();

        if (asset == null) {
            Chat.Whisper(player, PeekSeekSiege.Instance.Translate("pick_locked", options[index].Name));
            return;
        }

        for (int i = 0; i < options.Count; i++) {
            EffectManager.sendUIEffectVisibility(
                PeekSeekSiege.Config.PickUIKey, player.transportConnection, true,
                $"Option{i}_Taken", i == index);
        }
    }

    /// The board as lines of chat. Same information, same order, same numbers as the
    /// tiles, so a player who switches between the two is reading the same board.
    private void DrawToChat(SteamPlayer viewer) {
        Chat.Whisper(viewer, headline);
        for (int i = 0; i < options.Count; i++) {
            Chat.Whisper(viewer, PeekSeekSiege.Instance.Translate(
                "pick_option", i + 1, options[i].Name, options[i].Blurb));
        }
        Chat.Whisper(viewer, PeekSeekSiege.Instance.Translate("pick_how"));
    }

    /// The configured UI effect, looked up once. A GUID that is set but does not
    /// resolve is worth one loud line in the console, because the symptom otherwise is
    /// a board that silently never appears.
    private EffectAsset Effect() {
        if (effectLookedUp) return effect;
        effectLookedUp = true;

        string configured = PeekSeekSiege.Config.PickUIEffectGUID;
        if (string.IsNullOrWhiteSpace(configured)) return null;

        if (!Guid.TryParse(configured, out Guid guid)) {
            Logger.LogError($"PickUIEffectGUID \"{configured}\" is not a GUID. Falling back to the chat board.");
            return null;
        }

        effect = Assets.find<EffectAsset>(guid);
        if (effect == null) {
            Logger.LogError(
                $"No EffectAsset with GUID {guid} is loaded. Is the UI bundle in "
                + "WorkshopDownloadConfig.json? Falling back to the chat board.");
        }
        return effect;
    }
}
