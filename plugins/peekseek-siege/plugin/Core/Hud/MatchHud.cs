using System;
using System.Collections.Generic;
using System.Linq;
using SDG.NetTransport;
using SDG.Unturned;
using Steamworks;
using Logger = Rocket.Core.Logging.Logger;

namespace PeekSeekSiege.Core.Hud;

/// One player's face on the HUD: who they are, what they picked, whether they are still
/// in the round. Everything a gamemode has to say about a player, and nothing it has
/// to be Siege to say.
internal class HudSeat {
    internal ulong Steam64;
    internal string AvatarUrl;
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

    /// The small label under the clock: "Round 3", "Match point", "Overtime".
    internal string Label;

    /// True when the label is the kind that should be red: match point, final.
    internal bool Jeopardy;

    /// Which team is holding this round, 0 or 1, or -1 when nobody is: the shield goes
    /// beside their score and the swords beside the other.
    internal int DefendingTeam = -1;

    internal int SecondsLeft;

    /// How much of the flag is standing, 0 to 1, or -1 for no flag: the bar under the
    /// clock, in ten steps.
    internal float FlagHealth = -1f;
}

/// The bar along the top of the screen during a match: score and clock in the middle,
/// a face per player to either side, the viewer's own team always on the left. Up for
/// everybody, for the whole match; there is no way to turn it off, because it also
/// stands in front of the game's arena text and compass, which cannot be turned off.
///
/// Every change redraws the whole bar for every viewer. That is thirty small messages
/// a handful of times a round, and it is what makes the HUD unable to drift: the
/// server never has to know what a client still has. The earlier version sent only
/// the fields that changed and kept a record per viewer, and the record survived a
/// reconnect, so a player who came back got patches to a HUD they no longer had.
/// Pictures are not re-downloaded by a redraw; the client caches them by URL.
///
/// Nothing here knows what a round is. Siege drives it today; the main match plugin
/// can drive it tomorrow with the same three calls.
internal class MatchHud {
    private const int Seats = 5;

    private EffectAsset effect;
    private bool effectLookedUp;

    /// Who has been drawn to since the match started, so the once-a-second clock is
    /// not sent to a client that has nothing to put it on, and how many steps of the
    /// flag bar they were last shown, so a bar that has not moved is not resent.
    private readonly Dictionary<ulong, int> drawn = [];

    private const int FlagSteps = 10;

    internal bool IsAvailable => Effect() != null;

    /// Put the HUD up for everybody, or bring it up to date if it already is.
    internal void Show(HudState state) {
        if (Effect() == null) return;

        foreach (SteamPlayer viewer in Provider.clients.ToList()) {
            Draw(viewer, FromTheViewpointOf(state, viewer.playerID.steamID.m_SteamID));
            drawn[viewer.playerID.steamID.m_SteamID] = Steps(state.FlagHealth);
        }
    }

    /// The clock and the flag bar, once a second. Kept apart from Show so the
    /// per-second call is a text message per viewer and the bar only when it moved.
    internal void Countdown(int secondsLeft, float flagHealth) {
        if (Effect() == null) return;

        int steps = Steps(flagHealth);
        foreach (SteamPlayer viewer in Provider.clients.ToList()) {
            ulong steam64 = viewer.playerID.steamID.m_SteamID;
            if (!drawn.TryGetValue(steam64, out int shown)) continue;

            Send(viewer, "Timer", Clock(secondsLeft));
            if (shown == steps) continue;

            Flag(viewer, steps);
            drawn[steam64] = steps;
        }
    }

    internal void Hide() {
        EffectAsset asset = Effect();
        drawn.Clear();
        if (asset == null) return;

        foreach (SteamPlayer viewer in Provider.clients.ToList()) {
            EffectManager.askEffectClearByID(asset.id, viewer.transportConnection);
        }
    }

    /// A player who has left takes their HUD with them. Called after the leaving
    /// redraw, because Unturned raises the disconnect while they are still a client.
    internal void Forget(ulong steam64) {
        drawn.Remove(steam64);
    }

    // ---- drawing --------------------------------------------------------------

    private void Draw(SteamPlayer viewer, HudState state) {
        short key = PeekSeekSiege.Config.MatchHudKey;
        EffectManager.SendUIEffect(Effect(), key, viewer.transportConnection, true);

        for (int team = 0; team < 2; team++) {
            Send(viewer, $"Team{team}_Name", state.Teams[team].Name);
            Send(viewer, $"Score{team}", state.Teams[team].Score.ToString());
            Visible(viewer, $"Shield{team}", state.DefendingTeam == team);
            Visible(viewer, $"Swords{team}", state.DefendingTeam >= 0 && state.DefendingTeam != team);
        }
        Send(viewer, "Mid", state.Jeopardy ? $"<color=#ff4f4f>{state.Label}</color>" : state.Label);
        Send(viewer, "Timer", Clock(state.SecondsLeft));
        Flag(viewer, Steps(state.FlagHealth));

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

    /// A face is two pictures of the same download, one at full strength and one at
    /// half, because nothing a server sends can set an alpha but it can choose which
    /// of two images is on. Out of the round is the half one.
    private static void DrawSeat(SteamPlayer viewer, string slot, HudSeat seat) {
        Picture(viewer, $"{slot}_Avatar", seat.AvatarUrl);
        Picture(viewer, $"{slot}_Faded", seat.AvatarUrl);
        Visible(viewer, $"{slot}_Avatar", seat.Alive);
        Visible(viewer, $"{slot}_Faded", !seat.Alive);
        Picture(viewer, $"{slot}_Pick", ItemIcon(seat.PickItemId));
    }

    /// The viewer's own team on the left, the same way every shooter does it. Somebody
    /// who is on neither team sees the teams in the order the gamemode gave them.
    private static HudState FromTheViewpointOf(HudState state, ulong viewer) {
        bool onSecond = state.Teams[1].Seats.Any(seat => seat.Steam64 == viewer);

        return new HudState {
            Teams = onSecond ? [state.Teams[1], state.Teams[0]] : [state.Teams[0], state.Teams[1]],
            Label = state.Label,
            Jeopardy = state.Jeopardy,
            DefendingTeam = state.DefendingTeam < 0 ? -1 : onSecond ? 1 - state.DefendingTeam : state.DefendingTeam,
            SecondsLeft = state.SecondsLeft,
            FlagHealth = state.FlagHealth
        };
    }

    /// The flag bar: `steps` of its ten segments lit, and the whole thing away when
    /// there is no flag.
    private static void Flag(SteamPlayer viewer, int steps) {
        Visible(viewer, "Flag", steps >= 0);
        for (int i = 0; i < FlagSteps; i++) Visible(viewer, $"Flag{i}", i < steps);
    }

    /// Health as lit segments: anything standing lights at least one, and -1 for no
    /// flag at all.
    private static int Steps(float health) {
        if (health < 0f) return -1;

        return Math.Max(1, (int)Math.Ceiling(health * FlagSteps));
    }

    // ---- the wire ---------------------------------------------------------------

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
    /// which is the right look for "no pick yet".
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
