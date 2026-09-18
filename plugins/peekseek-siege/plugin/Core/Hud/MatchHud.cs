using System.Collections.Generic;
using System.Linq;
using SDG.NetTransport;
using SDG.Unturned;
using Steamworks;
using Logger = Rocket.Core.Logging.Logger;

namespace PeekSeekSiege.Core.Hud;

/// One player's card on the HUD: who they are, what they carry, whether they are still
/// in the round. Everything a gamemode has to say about a player, and nothing it has
/// to be Siege to say.
internal class HudSeat {
    internal ulong Steam64;
    internal string Name;
    internal string AvatarUrl;
    internal ushort GunItemId;
    internal ushort PickItemId;
    internal bool Alive = true;
}

internal class HudTeam {
    internal string Name;
    internal int Score;
    internal List<HudSeat> Seats = [];
}

/// What the HUD shows at any moment. A gamemode builds one of these whenever something
/// about the match changes and hands it over; the HUD works out who sees what.
internal class HudState {
    internal HudTeam[] Teams = [new HudTeam(), new HudTeam()];

    /// The small label over the clock: "Round 3", "Match point", "Overtime".
    internal string Label;

    /// True when the label is the kind that should be red: match point, final.
    internal bool Jeopardy;

    internal int SecondsLeft;
}

/// The bar along the top of the screen during a match: score and clock in the middle,
/// a card per player to either side, the viewer's own team always on the left.
///
/// This is the in-game twin of the stream overlay's notch and cards, and it is built
/// against the same idea as the overlay's mount-versus-patch split: `Show` sends the
/// whole thing once, and every later change touches only the field that changed. A
/// full resend every second would reload every avatar every second.
///
/// Nothing here knows what a round is. Siege drives it today; the main match plugin
/// can drive it tomorrow with the same three calls.
internal class MatchHud {
    private const int Seats = 5;

    private EffectAsset effect;
    private bool effectLookedUp;

    /// Who is currently looking at the HUD, and what they were last shown, so a change
    /// can be sent as the difference rather than as everything.
    private readonly Dictionary<ulong, HudState> shown = [];

    internal bool IsAvailable => Effect() != null;

    /// Put the HUD up for everybody, or bring it up to date if it already is. Cheap to
    /// call after any change: only fields that differ from what a viewer last saw are
    /// sent, and a viewer who has nothing new gets nothing.
    internal void Show(HudState state) {
        if (Effect() == null) return;

        foreach (SteamPlayer viewer in Provider.clients.ToList()) {
            ulong steam64 = viewer.playerID.steamID.m_SteamID;
            HudState mine = FromTheViewpointOf(state, steam64);

            if (shown.TryGetValue(steam64, out HudState last) && SameRoster(last, mine)) {
                Patch(viewer, last, mine);
            } else {
                Draw(viewer, mine);
            }

            shown[steam64] = mine;
        }
    }

    /// The clock alone, once a second. Kept apart from Show so the per-second call is
    /// one text message per viewer and nothing else.
    internal void Countdown(int secondsLeft) {
        if (Effect() == null) return;

        foreach (KeyValuePair<ulong, HudState> entry in shown.ToList()) {
            HudState last = entry.Value;
            if (last.SecondsLeft == secondsLeft) continue;
            last.SecondsLeft = secondsLeft;

            SteamPlayer viewer = PlayerTool.getSteamPlayer(new CSteamID(entry.Key));
            if (viewer == null) continue;

            Send(viewer, "Timer", Clock(secondsLeft));
        }
    }

    internal void Hide() {
        EffectAsset asset = Effect();
        shown.Clear();
        if (asset == null) return;

        foreach (SteamPlayer viewer in Provider.clients.ToList()) {
            EffectManager.askEffectClearByID(asset.id, viewer.transportConnection);
        }
    }

    /// A viewer who has just arrived and is not in `shown` yet gets everything on the
    /// next Show; one who has left should not keep a stale entry that Countdown would
    /// try to address.
    internal void Forget(ulong steam64) {
        shown.Remove(steam64);
    }

    // ---- drawing --------------------------------------------------------------

    private void Draw(SteamPlayer viewer, HudState state) {
        short key = PeekSeekSiege.Config.MatchHudKey;
        EffectManager.SendUIEffect(Effect(), key, viewer.transportConnection, true);

        for (int team = 0; team < 2; team++) {
            Send(viewer, $"Team{team}_Name", state.Teams[team].Name);
            Send(viewer, $"Score{team}", state.Teams[team].Score.ToString());
        }
        Send(viewer, "Mid", Label(state));
        Send(viewer, "Timer", Clock(state.SecondsLeft));

        for (int team = 0; team < 2; team++) {
            string side = team == 0 ? "L" : "R";
            for (int i = 0; i < Seats; i++) {
                HudSeat seat = state.Teams[team].Seats.ElementAtOrDefault(i);
                Visible(viewer, $"{side}{i}", seat != null);
                if (seat == null) continue;

                DrawSeat(viewer, $"{side}{i}", seat);
            }
        }
    }

    private static void DrawSeat(SteamPlayer viewer, string slot, HudSeat seat) {
        Send(viewer, $"{slot}_Name", seat.Name);
        Picture(viewer, $"{slot}_Avatar", seat.AvatarUrl);
        Picture(viewer, $"{slot}_Gun", ItemIcon(seat.GunItemId));
        Picture(viewer, $"{slot}_Pick", ItemIcon(seat.PickItemId));
        Visible(viewer, $"{slot}_Out", !seat.Alive);
    }

    /// Send only what differs. The roster is the same shape, so slots line up.
    private static void Patch(SteamPlayer viewer, HudState last, HudState next) {
        for (int team = 0; team < 2; team++) {
            if (last.Teams[team].Name != next.Teams[team].Name) Send(viewer, $"Team{team}_Name", next.Teams[team].Name);
            if (last.Teams[team].Score != next.Teams[team].Score) Send(viewer, $"Score{team}", next.Teams[team].Score.ToString());

            string side = team == 0 ? "L" : "R";
            for (int i = 0; i < next.Teams[team].Seats.Count; i++) {
                HudSeat was = last.Teams[team].Seats[i];
                HudSeat now = next.Teams[team].Seats[i];
                string slot = $"{side}{i}";

                if (was.Name != now.Name) Send(viewer, $"{slot}_Name", now.Name);
                if (was.AvatarUrl != now.AvatarUrl) Picture(viewer, $"{slot}_Avatar", now.AvatarUrl);
                if (was.GunItemId != now.GunItemId) Picture(viewer, $"{slot}_Gun", ItemIcon(now.GunItemId));
                if (was.PickItemId != now.PickItemId) Picture(viewer, $"{slot}_Pick", ItemIcon(now.PickItemId));
                if (was.Alive != now.Alive) Visible(viewer, $"{slot}_Out", !now.Alive);
            }
        }

        if (Label(last) != Label(next)) Send(viewer, "Mid", Label(next));
        if (last.SecondsLeft != next.SecondsLeft) Send(viewer, "Timer", Clock(next.SecondsLeft));
    }

    /// Same teams in the same order with the same people: the difference can be patched
    /// in place. Anything else is redrawn, which is what a player joining costs.
    private static bool SameRoster(HudState a, HudState b) {
        for (int team = 0; team < 2; team++) {
            List<HudSeat> x = a.Teams[team].Seats;
            List<HudSeat> y = b.Teams[team].Seats;
            if (x.Count != y.Count) return false;
            for (int i = 0; i < x.Count; i++) {
                if (x[i].Steam64 != y[i].Steam64) return false;
            }
        }
        return true;
    }

    /// The viewer's own team on the left, the same way every shooter does it. Somebody
    /// who is on neither team sees the teams in the order the gamemode gave them.
    private static HudState FromTheViewpointOf(HudState state, ulong viewer) {
        bool onSecond = state.Teams[1].Seats.Any(seat => seat.Steam64 == viewer);

        return new HudState {
            Teams = onSecond ? [state.Teams[1], state.Teams[0]] : [state.Teams[0], state.Teams[1]],
            Label = state.Label,
            Jeopardy = state.Jeopardy,
            SecondsLeft = state.SecondsLeft
        };
    }

    // ---- the wire ---------------------------------------------------------------

    private static string Label(HudState state) {
        return state.Jeopardy ? $"<color=#ff4f4f>{state.Label}</color>" : state.Label;
    }

    private static string Clock(int seconds) {
        return $"{seconds / 60}:{seconds % 60:00}";
    }

    private static string ItemIcon(ushort itemId) {
        return itemId == 0 ? "" : string.Format(PeekSeekSiege.Config.ItemIconURL, itemId);
    }

    private static void Send(SteamPlayer viewer, string child, string text) {
        EffectManager.sendUIEffectText(PeekSeekSiege.Config.MatchHudKey, viewer.transportConnection, true, child, text ?? "");
    }

    private static void Visible(SteamPlayer viewer, string child, bool visible) {
        EffectManager.sendUIEffectVisibility(PeekSeekSiege.Config.MatchHudKey, viewer.transportConnection, true, child, visible);
    }

    /// An empty URL is sent as an empty URL: the game then leaves the image disabled,
    /// which is the right look for "no gun" and "no pick yet".
    private static void Picture(SteamPlayer viewer, string child, string url) {
        ITransportConnection to = viewer.transportConnection;
        EffectManager.sendUIEffectImageURL(PeekSeekSiege.Config.MatchHudKey, to, true, child, url ?? "");
    }

    private EffectAsset Effect() {
        if (effectLookedUp) return effect;
        effectLookedUp = true;

        string configured = PeekSeekSiege.Config.MatchHudEffectGUID;
        if (string.IsNullOrWhiteSpace(configured)) return null;

        if (!System.Guid.TryParse(configured, out System.Guid guid)) {
            Logger.LogError($"MatchHudEffectGUID \"{configured}\" is not a GUID. No HUD.");
            return null;
        }

        effect = Assets.find<EffectAsset>(guid);
        if (effect == null) {
            Logger.LogError($"No EffectAsset with GUID {guid} is loaded. Is the UI workshop item on the server? No HUD.");
        }

        return effect;
    }
}
