using System;
using System.Collections.Generic;
using System.Linq;
using PeekSeek.Core.Web;
using PeekSeek.Gamemodes;
using SDG.Unturned;
using Steamworks;

namespace PeekSeek.Hud;

/// One tile on a board: a name, one line under it, and either a picture or the items
/// it stands for, drawn a row each with a count.
public class BoardOption {
    public string Name;
    public string Blurb;

    /// The item whose picture the tile shows, or 0 for the first item in the list.
    public ushort Icon;

    public List<GamemodeItem> Items = [];

    public ushort IconItemId => Icon != 0 ? Icon : Items.Count > 0 ? Items[0].ItemId : (ushort)0;
}

/// Which of the prefab's panels a board is drawn on. `Pick` is the five-tile board a
/// round's powerups go on, each item pictured from the site; `Shape` is the two-tile
/// board a base's footprint is chosen on, with the whole starting base drawn on each
/// tile and baked into the bundle; `Swap` is the six-tile board a piece's material is
/// changed on; `Attack` is the attacker's screen, the map of the site under attack
/// with a dot on each spawn and the five raid tiles under it. The name is the prefix
/// of every child the plugin addresses on that panel (`Pick_Timer`, `Shape0_Taken`)
/// and of its buttons (`BoardPick2`, `BoardSwap0`), so one class draws all of them.
public enum BoardFace {
    Pick,
    Shape,
    Swap,
    Attack
}

/// The boards a player answers.
///
/// A server without the bundle gets one loud line in its log and every board answers
/// itself with its first option; a client without it is kicked by the game's own asset
/// check before a board reaches them. There is no chat form: a board is pictures, and
/// a list of numbers in chat was a worse board that nobody was ever going to see.
///
/// State is per player rather than per board, because the two sides of a round choose
/// at the same moment from different lists. One shared list would have shown both
/// teams whichever board happened to be opened last.
public class PickBoard {
    private sealed class Seat {
        internal List<BoardOption> Options;
        internal string Headline;
        internal BoardFace Face;

        /// Told the option the moment it is taken, for a board whose answer is an
        /// action rather than a kit to hand out later.
        internal Action<BoardOption> OnAnswer;

        /// The option taken, or -1 while the board is up and unanswered.
        internal int Pick = -1;

        /// The attack screen's second question: how many dots are on the map, 0 on a
        /// board that asks no spawn, and whose map it is, which decides which dots.
        internal int Spawns;
        internal int Site;
        internal int SpawnPick = -1;

        internal bool Answered => Pick >= 0 && (Spawns == 0 || SpawnPick >= 0);

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

    /// How many item rows a powerup tile has.
    private const int ItemRows = 4;

    /// Buttons are named with this in front so a stray click on some other plugin's UI
    /// cannot land here.
    private const string ButtonPrefix = "Board";

    private readonly Dictionary<ulong, Seat> seats = [];

    private EffectAsset effect;
    private bool effectLookedUp;

    public bool IsOpen => seats.Values.Any(seat => seat.Up);

    /// The option a player ended up with, defaulting to the first on their own board
    /// when they never answered. Defaulting rather than blocking is deliberate: a round
    /// must never wait on somebody who alt-tabbed.
    public BoardOption Chosen(ulong steam64) {
        if (!seats.TryGetValue(steam64, out Seat seat) || seat.Options.Count == 0) return null;

        int index = seat.Pick >= 0 ? seat.Pick : 0;
        return seat.Options[Math.Min(index, seat.Options.Count - 1)];
    }

    /// The spawn an attacker ended up with, defaulting to the first the same way, or
    /// -1 for a player whose board never asked.
    public int SpawnChosen(ulong steam64) {
        if (!seats.TryGetValue(steam64, out Seat seat) || seat.Spawns == 0) return -1;

        return Math.Max(0, seat.SpawnPick);
    }

    public bool HasAnswered(ulong steam64) {
        return seats.TryGetValue(steam64, out Seat seat) && seat.Answered;
    }

    /// The tile a player took, zero-based, or -1. For a board whose options mean
    /// something other than items, like the shape of a base.
    public int PickIndex(ulong steam64) {
        return seats.TryGetValue(steam64, out Seat seat) ? seat.Pick : -1;
    }

    /// Clear every seat, before a round's boards go up. A player who has left since the
    /// last round should not still be holding one.
    public void Reset() {
        seats.Clear();
    }

    /// Put a board up for one player. `headline` is the one line above the options
    /// that says why they are choosing; `marked` is a tile shown as already taken,
    /// which is how the swap board shows what a piece is made of now.
    public void Open(SteamPlayer viewer, List<BoardOption> choices, string headline, BoardFace face = BoardFace.Pick,
                     Action<BoardOption> onAnswer = null, int marked = -1) {
        Seat seat = new() { Options = choices, Headline = headline, Face = face, OnAnswer = onAnswer };
        seats[viewer.playerID.steamID.m_SteamID] = seat;

        Draw(viewer, seat, marked);
    }

    /// Put the attack screen up for one player: the map of site `site` with `spawns`
    /// dots on it, and the raid options.
    public void OpenAttack(SteamPlayer viewer, List<BoardOption> choices, int site, int spawns) {
        Seat seat = new() { Options = choices, Face = BoardFace.Attack, Site = site, Spawns = spawns };
        seats[viewer.playerID.steamID.m_SteamID] = seat;

        Draw(viewer, seat, -1);
    }

    /// Take one player's board down without an answer.
    public void CloseFor(SteamPlayer viewer) {
        if (!seats.TryGetValue(viewer.playerID.steamID.m_SteamID, out Seat seat) || !seat.Up) return;

        seat.Up = false;
        EffectAsset asset = Effect();
        if (asset == null) return;

        EffectManager.askEffectClearByID(asset.id, viewer.transportConnection);
        Modal(viewer, false);
    }

    public bool IsShowing(SteamPlayer viewer, BoardFace face) {
        return seats.TryGetValue(viewer.playerID.steamID.m_SteamID, out Seat seat) && seat.Up && seat.Face == face;
    }

    /// Record a pick, 1-based as the board shows it. Returns the option taken, or null
    /// when that number is not on this player's board.
    public BoardOption Answer(SteamPlayer player, int oneBased) {
        if (!seats.TryGetValue(player.playerID.steamID.m_SteamID, out Seat seat) || !seat.Up) return null;
        if (oneBased < 1 || oneBased > seat.Options.Count) return null;

        // A tile lit for an earlier answer goes dark: a board that stays up between its
        // answers lets a player change their mind.
        if (seat.Pick >= 0 && seat.Pick != oneBased - 1 && Effect() != null) {
            Visible(player, $"{seat.Face}{seat.Pick}_Taken", false);
        }

        seat.Pick = oneBased - 1;
        BoardOption taken = seat.Options[seat.Pick];
        Confirm(player, seat, $"{seat.Face}{seat.Pick}_Taken");
        seat.OnAnswer?.Invoke(taken);
        return taken;
    }

    /// Record where an attacker comes in, 1-based as the dots are numbered. False when
    /// that number is not a dot on this player's map, or their board asks no spawn.
    public bool AnswerSpawn(SteamPlayer player, int oneBased) {
        if (!seats.TryGetValue(player.playerID.steamID.m_SteamID, out Seat seat) || !seat.Up || seat.Spawns == 0) return false;
        if (oneBased < 1 || oneBased > seat.Spawns) return false;

        seat.SpawnPick = oneBased - 1;
        Confirm(player, seat, null);

        // The whole team sees who is coming in where, so every attacker's map is redrawn,
        // not just the one that was clicked.
        foreach (KeyValuePair<ulong, Seat> entry in seats) {
            if (entry.Value.Face != BoardFace.Attack || !entry.Value.Up) continue;

            SteamPlayer viewer = PlayerTool.getSteamPlayer(new CSteamID(entry.Key));
            if (viewer != null) DrawSpawns(viewer, entry.Value);
        }
        return true;
    }

    /// Answer from a clicked Unity button. Tiles are named `BoardPick0`, `BoardShape0`,
    /// `BoardSwap0` and `BoardAttack0` upward, the map's dots `BoardSpawn00` (site, then
    /// dot), and a click on a panel this player was not shown is not an answer.
    internal BoardOption AnswerButton(SteamPlayer player, string buttonName) {
        if (buttonName == null || !seats.TryGetValue(player.playerID.steamID.m_SteamID, out Seat seat)) return null;

        string dots = ButtonPrefix + "Spawn" + seat.Site;
        if (seat.Spawns > 0 && buttonName.StartsWith(dots)) {
            if (int.TryParse(buttonName.Substring(dots.Length), out int dot)) AnswerSpawn(player, dot + 1);
            return null;
        }

        string prefix = ButtonPrefix + seat.Face;
        if (!buttonName.StartsWith(prefix)) return null;
        if (!int.TryParse(buttonName.Substring(prefix.Length), out int index)) return null;

        return Answer(player, index + 1);
    }

    /// Every board down, answers kept.
    public void Close() {
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
    public void Countdown(int secondsLeft) {
        if (Effect() == null) return;

        foreach (KeyValuePair<ulong, Seat> entry in seats) {
            if (!entry.Value.Up || entry.Value.Face == BoardFace.Swap) continue;

            SteamPlayer viewer = PlayerTool.getSteamPlayer(new CSteamID(entry.Key));
            if (viewer == null) continue;

            // Red for the last five seconds: the one thing on the board that is jeopardy,
            // so the one place the overlay's red belongs.
            string clock = secondsLeft <= 5 ? $"<color=#ff4f4f>{secondsLeft}</color>" : secondsLeft.ToString();
            Send(viewer, $"{entry.Value.Face}_Timer", clock);
        }
    }

    private void Draw(SteamPlayer viewer, Seat seat, int marked) {
        EffectAsset asset = Effect();
        if (asset == null) return;

        string face = seat.Face.ToString();
        EffectManager.SendUIEffect(asset, PeekSeek.Config.PickUIKey, viewer.transportConnection, true);
        Visible(viewer, face, true);

        // The attack screen has no headline: the map says which base, and the bar says
        // which round. Everything else has one line above the tiles.
        if (seat.Face != BoardFace.Attack) Send(viewer, $"{face}_Headline", seat.Headline);

        for (int i = 0; i < Tiles(seat.Face); i++) {
            BoardOption option = seat.Options.ElementAtOrDefault(i);
            Visible(viewer, $"{ButtonPrefix}{face}{i}", option != null);
            if (option == null) continue;

            Send(viewer, $"{face}{i}_Name", option.Name);
            Visible(viewer, $"{face}{i}_Taken", i == marked);

            // A powerup tile is the items themselves, a row each with its count. The
            // footprint tiles carry their own picture and a line; the swap tiles one
            // picture and a line with what is left.
            if (seat.Face is BoardFace.Pick or BoardFace.Attack) {
                DrawItems(viewer, $"{face}{i}", option);
                continue;
            }

            Send(viewer, $"{face}{i}_Blurb", option.Blurb);
            if (seat.Face == BoardFace.Swap) Picture(viewer, $"{face}{i}_Icon", IconUrl(option.IconItemId));
        }

        if (seat.Spawns > 0) {
            Visible(viewer, $"Attack_Map{seat.Site}", true);
            DrawSpawns(viewer, seat);
        }

        // A tile cannot be clicked without a cursor, and Unturned only shows one while a
        // plugin says it is modal. Released the moment they pick, or when the board goes.
        Modal(viewer, true);
    }

    private static void DrawItems(SteamPlayer viewer, string tile, BoardOption option) {
        for (int k = 0; k < ItemRows; k++) {
            GamemodeItem item = option.Items.ElementAtOrDefault(k);
            Visible(viewer, $"{tile}_Item{k}", item != null);
            if (item == null) continue;

            Send(viewer, $"{tile}_Item{k}_Count", $"{item.Amount}x");
            Picture(viewer, $"{tile}_Item{k}_Icon", IconUrl(item.ItemId));
        }
    }

    /// The dots as this viewer sees them: their own lit, and beside every dot the faces
    /// of the teammates coming in there. Teammates are the other attack seats on the
    /// same map, since only one side attacks at a time.
    private void DrawSpawns(SteamPlayer viewer, Seat seat) {
        for (int dot = 0; dot < seat.Spawns; dot++) {
            string name = $"Spawn{seat.Site}{dot}";
            Visible(viewer, $"{name}_On", dot == seat.SpawnPick);

            List<ulong> there = seats
                .Where(entry => entry.Value.Face == BoardFace.Attack && entry.Value.Site == seat.Site && entry.Value.SpawnPick == dot)
                .Select(entry => entry.Key)
                .ToList();

            for (int k = 0; k < FacesPerSpawn; k++) {
                bool shown = k < there.Count;
                Visible(viewer, $"{name}_Face{k}", shown);
                if (shown) Picture(viewer, $"{name}_Face{k}", RemoteAPI.AvatarFor(there[k]));
            }
        }
    }

    private static string IconUrl(ushort itemId) {
        return itemId == 0 ? "" : string.Format(PeekSeek.Config.ItemIconURL, itemId);
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
    /// receipt is the pick icon on the bar. The attack screen stays up between its two
    /// answers, lit where it has been answered, because half a screen is no receipt.
    private void Confirm(SteamPlayer player, Seat seat, string taken) {
        EffectAsset asset = Effect();
        if (asset == null) return;

        if (taken != null) Visible(player, taken, true);

        if (!seat.Answered) return;
        seat.Up = false;

        EffectManager.askEffectClearByID(asset.id, player.transportConnection);
        Modal(player, false);
    }

    private static void Send(SteamPlayer viewer, string child, string text) {
        EffectManager.sendUIEffectText(PeekSeek.Config.PickUIKey, viewer.transportConnection, true, child, text ?? "");
    }

    private static void Visible(SteamPlayer viewer, string child, bool visible) {
        EffectManager.sendUIEffectVisibility(PeekSeek.Config.PickUIKey, viewer.transportConnection, true, child, visible);
    }

    private static void Picture(SteamPlayer viewer, string child, string url) {
        EffectManager.sendUIEffectImageURL(PeekSeek.Config.PickUIKey, viewer.transportConnection, true, child, url ?? "");
    }

    private EffectAsset Effect() {
        if (effectLookedUp) return effect;
        effectLookedUp = true;

        effect = Effects.Find(PeekSeek.Config.PickUIEffectGUID, "PickUIEffectGUID", "Every board will answer itself.");
        return effect;
    }
}
