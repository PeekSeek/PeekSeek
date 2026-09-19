using System;
using System.Collections.Generic;
using System.Linq;
using PeekSeekSiege.Core.Models;
using SDG.Unturned;
using Steamworks;
using Logger = Rocket.Core.Logging.Logger;

namespace PeekSeekSiege.Core;

/// Which of the prefab's two panels a board is drawn on. `Pick` is the five-tile board
/// a round's options go on, each tile's picture fetched from the site; `Shape` is the
/// two-tile board the footprint is chosen on, with the whole starting base drawn on
/// each tile and baked into the bundle. The name is the prefix of every child the
/// plugin addresses on that panel (`Pick_Timer`, `Shape0_Taken`) and of its buttons
/// (`SiegePick2`, `SiegeShape0`), so one board class draws both.
internal enum BoardFace {
    Pick,
    Shape
}

/// The board a player answers between rounds, and the one they answer before building.
///
/// There are two ways to draw it and only one set of rules behind them. Unturned cannot
/// draw a custom UI from plugin code: EffectManager.SendUIEffect takes an EffectAsset,
/// and an EffectAsset only exists inside a workshop bundle the client has already
/// downloaded. So the board is written against whichever is available. Give it an effect
/// GUID and it draws a real Unity layout with clickable tiles; leave the GUID empty and
/// it prints the same options to chat and takes /pick instead.
///
/// The chat form is not a placeholder that gets deleted later. A player whose client is
/// still downloading the bundle, or who has UI effects failing for any other reason, has
/// to be able to answer the board, and this is the answer for them.
///
/// State is per player rather than per board, because the two sides of a round choose at
/// the same moment from different lists: the defenders are looking at traps while the
/// attackers look at breaching tools. One shared list would have shown both teams
/// whichever board happened to be opened last.
internal class PickBoard {
    private sealed class Seat {
        internal List<SiegeOption> Options;
        internal string Headline;
        internal BoardFace Face;

        /// The option taken, or -1 while the board is up and unanswered.
        internal int Pick = -1;
    }

    /// How many tiles each panel has. Tiles past the options offered are hidden, so a
    /// three-option list on the five-tile panel does not show two tiles of whatever the
    /// prefab was saved with.
    private static int Tiles(BoardFace face) => face == BoardFace.Shape ? 2 : 5;

    private readonly Dictionary<ulong, Seat> seats = [];

    private EffectAsset effect;
    private bool effectLookedUp;

    internal bool IsOpen { get; private set; }

    /// The option a player ended up with, defaulting to the first on their own board
    /// when they never answered. Defaulting rather than blocking is deliberate: a round
    /// must never wait on somebody who alt-tabbed.
    internal SiegeOption Chosen(ulong steam64) {
        if (!seats.TryGetValue(steam64, out Seat seat) || seat.Options.Count == 0) return null;

        int index = seat.Pick >= 0 ? seat.Pick : 0;
        return seat.Options[Math.Min(index, seat.Options.Count - 1)];
    }

    internal bool HasAnswered(ulong steam64) {
        return seats.TryGetValue(steam64, out Seat seat) && seat.Pick >= 0;
    }

    /// The tile a player took, zero-based, or -1. For a board whose options mean
    /// something other than items, like the shape of a base.
    internal int PickIndex(ulong steam64) {
        return seats.TryGetValue(steam64, out Seat seat) ? seat.Pick : -1;
    }

    /// Clear every seat, before a round's boards go up. A player who has left since the
    /// last round should not still be holding one.
    internal void Reset() {
        seats.Clear();
        IsOpen = true;
    }

    /// Put the board up for one player. `headline` is the one line above the options
    /// that says why they are choosing: which round it is and which side they are on.
    internal void Open(SteamPlayer viewer, List<SiegeOption> choices, string headline, BoardFace face = BoardFace.Pick) {
        Seat seat = new() { Options = choices, Headline = headline, Face = face };
        seats[viewer.playerID.steamID.m_SteamID] = seat;

        Draw(viewer, seat);
    }

    /// Record a pick, 1-based as the board shows it. Returns the option taken, or null
    /// when that number is not on this player's board.
    internal SiegeOption Answer(SteamPlayer player, int oneBased) {
        if (!IsOpen) return null;
        if (!seats.TryGetValue(player.playerID.steamID.m_SteamID, out Seat seat)) return null;
        if (oneBased < 1 || oneBased > seat.Options.Count) return null;

        seat.Pick = oneBased - 1;
        Confirm(player, seat);
        return seat.Options[seat.Pick];
    }

    /// Answer from a clicked Unity button. Buttons are named `SiegePick0` and
    /// `SiegeShape0` upward so a stray click on some other plugin's UI cannot land
    /// here, and a click on the panel this player was not shown is not an answer.
    internal SiegeOption AnswerButton(SteamPlayer player, string buttonName) {
        if (!seats.TryGetValue(player.playerID.steamID.m_SteamID, out Seat seat)) return null;

        string prefix = ButtonPrefix + seat.Face;
        if (buttonName == null || !buttonName.StartsWith(prefix)) return null;
        if (!int.TryParse(buttonName.Substring(prefix.Length), out int index)) return null;

        return Answer(player, index + 1);
    }

    internal void Close() {
        if (!IsOpen) return;
        IsOpen = false;

        EffectAsset asset = Effect();
        if (asset == null) return;

        foreach (SteamPlayer viewer in Provider.clients) {
            EffectManager.askEffectClearByID(asset.id, viewer.transportConnection);
            Modal(viewer, false);
        }
    }

    /// Repaint the seconds left. Called about once a second while the board is up, so it
    /// only touches the one field that changed rather than resending the layout.
    internal void Countdown(int secondsLeft) {
        if (!IsOpen || Effect() == null) return;

        foreach (ulong steam64 in seats.Keys) {
            SteamPlayer viewer = PlayerTool.getSteamPlayer(new CSteamID(steam64));
            if (viewer == null) continue;

            // Red for the last five seconds: the one thing on the board that is jeopardy,
            // so the one place the overlay's red belongs.
            string clock = secondsLeft <= 5 ? $"<color=#ff4f4f>{secondsLeft}</color>" : secondsLeft.ToString();
            EffectManager.sendUIEffectText(
                PeekSeekSiege.Config.PickUIKey, viewer.transportConnection, true, $"{seats[steam64].Face}_Timer", clock);
        }
    }

    private const string ButtonPrefix = "Siege";

    private void Draw(SteamPlayer viewer, Seat seat) {
        EffectAsset asset = Effect();
        if (asset == null) {
            DrawToChat(viewer, seat);
            return;
        }

        short key = PeekSeekSiege.Config.PickUIKey;
        string face = seat.Face.ToString();
        EffectManager.SendUIEffect(asset, key, viewer.transportConnection, true);
        EffectManager.sendUIEffectVisibility(key, viewer.transportConnection, true, face, true);
        EffectManager.sendUIEffectText(key, viewer.transportConnection, true, $"{face}_Headline", seat.Headline);

        for (int i = 0; i < Tiles(seat.Face); i++) {
            SiegeOption option = seat.Options.ElementAtOrDefault(i);
            EffectManager.sendUIEffectVisibility(key, viewer.transportConnection, true, $"{ButtonPrefix}{face}{i}", option != null);
            if (option == null) continue;

            EffectManager.sendUIEffectText(key, viewer.transportConnection, true, $"{face}{i}_Name", option.Name);
            EffectManager.sendUIEffectText(key, viewer.transportConnection, true, $"{face}{i}_Blurb", option.Blurb);

            // The footprint tiles carry their own picture; only the pick tiles fetch one.
            if (seat.Face == BoardFace.Pick) {
                EffectManager.sendUIEffectImageURL(
                    key, viewer.transportConnection, true, $"{face}{i}_Icon", IconUrl(option));
            }
        }

        // A tile cannot be clicked without a cursor, and Unturned only shows one while a
        // plugin says it is modal. Released the moment they pick, or when the board goes.
        Modal(viewer, true);
    }

    private static string IconUrl(SiegeOption option) {
        return string.Format(PeekSeekSiege.Config.ItemIconURL, option.IconItemId);
    }

    private static void Modal(SteamPlayer viewer, bool on) {
        viewer.player.setPluginWidgetFlag(EPluginWidgetFlags.Modal, on);
    }

    /// Mark the taken tile, or say what was taken. Only the state that changed, so a
    /// player who picks does not get the whole board again.
    private void Confirm(SteamPlayer player, Seat seat) {
        EffectAsset asset = Effect();

        if (asset == null) {
            Chat.Whisper(player, PeekSeekSiege.Instance.Translate(
                "pick_locked", seat.Options[seat.Pick].Name));
            return;
        }

        // The board goes the moment they pick. Left up as a receipt, its panel sat under
        // the locked cursor and the game read every click as "over UI": nothing fired,
        // nothing could be picked up, for as long as the pick phase ran. The receipt is
        // the pick icon on the HUD.
        EffectManager.askEffectClearByID(asset.id, player.transportConnection);
        Modal(player, false);
    }

    /// The board as lines of chat. Same information, same order, same numbers as the
    /// tiles, so a player who moves between the two is reading the same board.
    private static void DrawToChat(SteamPlayer viewer, Seat seat) {
        Chat.Whisper(viewer, Chat.Rule);
        Chat.Whisper(viewer, seat.Headline);

        for (int i = 0; i < seat.Options.Count; i++) {
            Chat.Whisper(viewer, PeekSeekSiege.Instance.Translate(
                "pick_option", i + 1, seat.Options[i].Name, seat.Options[i].Blurb));
        }

        Chat.Whisper(viewer, PeekSeekSiege.Instance.Translate("pick_how"));
    }

    /// The configured UI effect, looked up once. A GUID that is set but does not resolve
    /// is worth one loud line in the console, because the symptom otherwise is a board
    /// that silently never appears.
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
