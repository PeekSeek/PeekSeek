using System;
using System.Collections.Generic;
using System.Linq;
using PeekSeekSiege.Core.Models;
using SDG.Unturned;
using Steamworks;
using Logger = Rocket.Core.Logging.Logger;

namespace PeekSeekSiege.Core;

/// Which of the prefab's panels a board is drawn on. `Pick` is the five-tile board a
/// round's options go on, each tile's picture fetched from the site; `Shape` is the
/// two-tile board the footprint is chosen on, with the whole starting base drawn on
/// each tile and baked into the bundle; `Swap` is the six-tile board a builder changes
/// a piece's material on; `Attack` is the attacker's screen, the map of the site under
/// attack with a dot on each spawn and the five raid options under it. The name is the
/// prefix of every child the plugin addresses on that panel (`Pick_Timer`,
/// `Shape0_Taken`) and of its buttons (`SiegePick2`, `SiegeSwap0`), so one board class
/// draws all of them.
internal enum BoardFace {
    Pick,
    Shape,
    Swap,
    Attack
}

/// The boards a player answers: the round's options, the footprint before building,
/// the material of a piece while building, and where an attacker comes in.
///
/// There are two ways to draw a board and only one set of rules behind them. Unturned
/// cannot draw a custom UI from plugin code: EffectManager.SendUIEffect takes an
/// EffectAsset, and an EffectAsset only exists inside a workshop bundle the client has
/// already downloaded. So the board is written against whichever is available. Give it
/// an effect GUID and it draws a real Unity layout with clickable tiles; leave the GUID
/// empty and it prints the same options to chat and takes /pick instead.
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

        /// Told the option the moment it is taken, for a board whose answer is an
        /// action rather than a kit to hand out later.
        internal System.Action<SiegeOption> OnAnswer;

        /// The option taken, or -1 while the board is up and unanswered.
        internal int Pick = -1;

        /// The attack screen's second question. `Spawns` is how each spawn is said in
        /// chat ("north-west"), null on a board that asks no spawn; `Site` is whose map
        /// is shown, which decides which dots are on it.
        internal List<string> Spawns;
        internal int Site;
        internal int SpawnPick = -1;

        internal bool Answered => Pick >= 0 && (Spawns == null || SpawnPick >= 0);

        /// Whether the board is on the player's screen. An answer is only taken from a
        /// board that is up; the answer itself is kept until the next Reset, because the
        /// round hands the kit out after the board has gone.
        internal bool Up = true;
    }

    /// How many tiles each panel has. Tiles past the options offered are hidden, so a
    /// three-option list on the five-tile panel does not show two tiles of whatever the
    /// prefab was saved with.
    private static int Tiles(BoardFace face) => face switch {
        BoardFace.Shape => 2,
        BoardFace.Swap => 6,
        _ => 5
    };

    /// How many faces fit beside a dot on the map: a full team.
    private const int FacesPerSpawn = 5;

    private readonly Dictionary<ulong, Seat> seats = [];

    private EffectAsset effect;
    private bool effectLookedUp;

    internal bool IsOpen => seats.Values.Any(seat => seat.Up);

    /// The option a player ended up with, defaulting to the first on their own board
    /// when they never answered. Defaulting rather than blocking is deliberate: a round
    /// must never wait on somebody who alt-tabbed.
    internal SiegeOption Chosen(ulong steam64) {
        if (!seats.TryGetValue(steam64, out Seat seat) || seat.Options.Count == 0) return null;

        int index = seat.Pick >= 0 ? seat.Pick : 0;
        return seat.Options[Math.Min(index, seat.Options.Count - 1)];
    }

    /// The spawn an attacker ended up with, defaulting to the first the same way, or
    /// -1 for a player whose board never asked.
    internal int SpawnChosen(ulong steam64) {
        if (!seats.TryGetValue(steam64, out Seat seat) || seat.Spawns == null) return -1;

        return Math.Max(0, seat.SpawnPick);
    }

    internal bool HasAnswered(ulong steam64) {
        return seats.TryGetValue(steam64, out Seat seat) && seat.Answered;
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
    }

    /// Put a board up for one player. `headline` is the one line above the options
    /// that says why they are choosing; `marked` is a tile shown as already taken,
    /// which is how the swap board shows what a piece is made of now.
    internal void Open(SteamPlayer viewer, List<SiegeOption> choices, string headline, BoardFace face = BoardFace.Pick,
                       System.Action<SiegeOption> onAnswer = null, int marked = -1) {
        Seat seat = new() { Options = choices, Headline = headline, Face = face, OnAnswer = onAnswer };
        seats[viewer.playerID.steamID.m_SteamID] = seat;

        Draw(viewer, seat, marked);
    }

    /// Put the attack screen up for one player: the map of site `site` with a dot per
    /// spawn, and the raid options. `spawns` is each spawn as chat says it, in dot order.
    internal void OpenAttack(SteamPlayer viewer, List<SiegeOption> choices, string headline, int site, List<string> spawns) {
        Seat seat = new() { Options = choices, Headline = headline, Face = BoardFace.Attack, Site = site, Spawns = spawns };
        seats[viewer.playerID.steamID.m_SteamID] = seat;

        Draw(viewer, seat, -1);
    }

    /// Take one player's board down without an answer.
    internal void CloseFor(SteamPlayer viewer) {
        if (!seats.TryGetValue(viewer.playerID.steamID.m_SteamID, out Seat seat) || !seat.Up) return;

        seat.Up = false;
        EffectAsset asset = Effect();
        if (asset == null) return;

        EffectManager.askEffectClearByID(asset.id, viewer.transportConnection);
        Modal(viewer, false);
    }

    internal bool IsShowing(SteamPlayer viewer, BoardFace face) {
        return seats.TryGetValue(viewer.playerID.steamID.m_SteamID, out Seat seat) && seat.Up && seat.Face == face;
    }

    /// Record a pick, 1-based as the board shows it. Returns the option taken, or null
    /// when that number is not on this player's board.
    internal SiegeOption Answer(SteamPlayer player, int oneBased) {
        if (!seats.TryGetValue(player.playerID.steamID.m_SteamID, out Seat seat) || !seat.Up) return null;
        if (oneBased < 1 || oneBased > seat.Options.Count) return null;

        // A tile lit for an earlier answer goes dark: a board that stays up between its
        // answers lets a player change their mind.
        if (seat.Pick >= 0 && seat.Pick != oneBased - 1 && Effect() != null) {
            EffectManager.sendUIEffectVisibility(
                PeekSeekSiege.Config.PickUIKey, player.transportConnection, true, $"{seat.Face}{seat.Pick}_Taken", false);
        }

        seat.Pick = oneBased - 1;
        SiegeOption taken = seat.Options[seat.Pick];
        Confirm(player, seat, $"{seat.Face}{seat.Pick}_Taken",
            PeekSeekSiege.Instance.Translate("pick_locked", taken.Name));
        seat.OnAnswer?.Invoke(taken);
        return taken;
    }

    /// Record where an attacker comes in, 1-based as the dots are numbered. False when
    /// that number is not a dot on this player's map, or their board asks no spawn.
    internal bool AnswerSpawn(SteamPlayer player, int oneBased) {
        if (!seats.TryGetValue(player.playerID.steamID.m_SteamID, out Seat seat) || !seat.Up || seat.Spawns == null) return false;
        if (oneBased < 1 || oneBased > seat.Spawns.Count) return false;

        seat.SpawnPick = oneBased - 1;
        Confirm(player, seat, null, PeekSeekSiege.Instance.Translate("spawn_locked", seat.Spawns[seat.SpawnPick]));

        // The whole team sees who is coming in where, so every attacker's map is redrawn,
        // not just the one that was clicked.
        foreach (KeyValuePair<ulong, Seat> entry in seats) {
            if (entry.Value.Face != BoardFace.Attack || !entry.Value.Up) continue;

            SteamPlayer viewer = PlayerTool.getSteamPlayer(new CSteamID(entry.Key));
            if (viewer != null) DrawSpawns(viewer, entry.Value);
        }
        return true;
    }

    /// Answer from a clicked Unity button. Tiles are named `SiegePick0`, `SiegeShape0`,
    /// `SiegeSwap0` and `SiegeAttack0` upward, the map's dots `SiegeSpawn00` (site,
    /// then dot), so a stray click on some other plugin's UI cannot land here, and a
    /// click on a panel this player was not shown is not an answer.
    internal SiegeOption AnswerButton(SteamPlayer player, string buttonName) {
        if (buttonName == null || !seats.TryGetValue(player.playerID.steamID.m_SteamID, out Seat seat)) return null;

        string dots = ButtonPrefix + "Spawn" + seat.Site;
        if (seat.Spawns != null && buttonName.StartsWith(dots)) {
            if (int.TryParse(buttonName.Substring(dots.Length), out int dot)) AnswerSpawn(player, dot + 1);
            return null;
        }

        string prefix = ButtonPrefix + seat.Face;
        if (!buttonName.StartsWith(prefix)) return null;
        if (!int.TryParse(buttonName.Substring(prefix.Length), out int index)) return null;

        return Answer(player, index + 1);
    }

    /// Every board down, answers kept.
    internal void Close() {
        foreach (Seat seat in seats.Values) seat.Up = false;

        EffectAsset asset = Effect();
        if (asset == null) return;

        foreach (SteamPlayer viewer in Provider.clients) {
            EffectManager.askEffectClearByID(asset.id, viewer.transportConnection);
            Modal(viewer, false);
        }
    }

    /// Repaint the seconds left. Called about once a second while a timed board is up,
    /// so it only touches the one field that changed rather than resending the layout.
    internal void Countdown(int secondsLeft) {
        if (Effect() == null) return;

        foreach (KeyValuePair<ulong, Seat> entry in seats) {
            if (!entry.Value.Up || entry.Value.Face == BoardFace.Swap) continue;

            SteamPlayer viewer = PlayerTool.getSteamPlayer(new CSteamID(entry.Key));
            if (viewer == null) continue;

            // Red for the last five seconds: the one thing on the board that is jeopardy,
            // so the one place the overlay's red belongs.
            string clock = secondsLeft <= 5 ? $"<color=#ff4f4f>{secondsLeft}</color>" : secondsLeft.ToString();
            EffectManager.sendUIEffectText(
                PeekSeekSiege.Config.PickUIKey, viewer.transportConnection, true, $"{entry.Value.Face}_Timer", clock);
        }
    }

    private const string ButtonPrefix = "Siege";

    private void Draw(SteamPlayer viewer, Seat seat, int marked) {
        EffectAsset asset = Effect();
        if (asset == null) {
            DrawToChat(viewer, seat);
            return;
        }

        short key = PeekSeekSiege.Config.PickUIKey;
        string face = seat.Face.ToString();
        EffectManager.SendUIEffect(asset, key, viewer.transportConnection, true);
        EffectManager.sendUIEffectVisibility(key, viewer.transportConnection, true, face, true);

        // The attack screen has no headline: the map says which base, and the HUD says
        // which round. Everything else has one line above the tiles.
        if (seat.Face != BoardFace.Attack) {
            EffectManager.sendUIEffectText(key, viewer.transportConnection, true, $"{face}_Headline", seat.Headline);
        }

        for (int i = 0; i < Tiles(seat.Face); i++) {
            SiegeOption option = seat.Options.ElementAtOrDefault(i);
            EffectManager.sendUIEffectVisibility(key, viewer.transportConnection, true, $"{ButtonPrefix}{face}{i}", option != null);
            if (option == null) continue;

            EffectManager.sendUIEffectText(key, viewer.transportConnection, true, $"{face}{i}_Name", option.Name);
            EffectManager.sendUIEffectText(key, viewer.transportConnection, true, $"{face}{i}_Blurb", option.Blurb);
            EffectManager.sendUIEffectVisibility(key, viewer.transportConnection, true, $"{face}{i}_Taken", i == marked);

            // The footprint tiles carry their own picture; the others fetch one.
            if (seat.Face != BoardFace.Shape) {
                EffectManager.sendUIEffectImageURL(
                    key, viewer.transportConnection, true, $"{face}{i}_Icon", IconUrl(option));
            }
        }

        if (seat.Spawns != null) {
            EffectManager.sendUIEffectVisibility(key, viewer.transportConnection, true, $"Attack_Map{seat.Site}", true);
            DrawSpawns(viewer, seat);
        }

        // A tile cannot be clicked without a cursor, and Unturned only shows one while a
        // plugin says it is modal. Released the moment they pick, or when the board goes.
        Modal(viewer, true);
    }

    /// The dots as this viewer sees them: their own lit, and beside every dot the faces
    /// of the teammates coming in there. Teammates are the other attack seats on the
    /// same map, since only one side attacks at a time.
    private void DrawSpawns(SteamPlayer viewer, Seat seat) {
        short key = PeekSeekSiege.Config.PickUIKey;

        for (int dot = 0; dot < seat.Spawns.Count; dot++) {
            string name = $"Spawn{seat.Site}{dot}";
            EffectManager.sendUIEffectVisibility(key, viewer.transportConnection, true, $"{name}_On", dot == seat.SpawnPick);

            List<ulong> there = seats
                .Where(entry => entry.Value.Face == BoardFace.Attack && entry.Value.Site == seat.Site && entry.Value.SpawnPick == dot)
                .Select(entry => entry.Key)
                .ToList();

            for (int k = 0; k < FacesPerSpawn; k++) {
                bool shown = k < there.Count;
                EffectManager.sendUIEffectVisibility(key, viewer.transportConnection, true, $"{name}_Face{k}", shown);
                if (!shown) continue;

                EffectManager.sendUIEffectImageURL(key, viewer.transportConnection, true, $"{name}_Face{k}",
                    RemoteAPI.AvatarFor(there[k]) ?? PeekSeekSiege.Config.DefaultAvatarURL);
            }
        }
    }

    private static string IconUrl(SiegeOption option) {
        return option.IconItemId == 0 ? "" : string.Format(PeekSeekSiege.Config.ItemIconURL, option.IconItemId);
    }

    private static void Modal(SteamPlayer viewer, bool on) {
        viewer.player.setPluginWidgetFlag(EPluginWidgetFlags.Modal, on);
    }

    /// Mark what was taken, and take the board down once everything on it is answered.
    /// `taken` is the tile's selected fill to light, or null for an answer with no tile.
    ///
    /// The board goes the moment the last answer is in. Left up as a receipt, its panel
    /// sat under the locked cursor and the game read every click as "over UI": nothing
    /// fired, nothing could be picked up, for as long as the pick phase ran. The
    /// receipt is the pick icon on the HUD. The attack screen stays up between its two
    /// answers, lit where it has been answered, because half a screen is no receipt.
    private void Confirm(SteamPlayer player, Seat seat, string taken, string said) {
        EffectAsset asset = Effect();

        if (asset == null) {
            Chat.Whisper(player, said);
        } else if (taken != null) {
            EffectManager.sendUIEffectVisibility(PeekSeekSiege.Config.PickUIKey, player.transportConnection, true, taken, true);
        }

        if (!seat.Answered) return;
        seat.Up = false;

        if (asset == null) return;
        EffectManager.askEffectClearByID(asset.id, player.transportConnection);
        Modal(player, false);
    }

    /// The board as lines of chat. Same information, same order, same numbers as the
    /// tiles, so a player who moves between the two is reading the same board.
    private static void DrawToChat(SteamPlayer viewer, Seat seat) {
        Chat.Whisper(viewer, seat.Headline);

        if (seat.Spawns != null) {
            for (int i = 0; i < seat.Spawns.Count; i++) {
                Chat.Whisper(viewer, PeekSeekSiege.Instance.Translate("pick_spawn_option", i + 1, seat.Spawns[i]));
            }
        }

        for (int i = 0; i < seat.Options.Count; i++) {
            Chat.Whisper(viewer, PeekSeekSiege.Instance.Translate(
                "pick_option", i + 1, seat.Options[i].Name, seat.Options[i].Blurb));
        }

        Chat.Whisper(viewer, PeekSeekSiege.Instance.Translate(seat.Spawns != null ? "pick_how_attack" : "pick_how"));
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
