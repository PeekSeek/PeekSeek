using System.Collections.Generic;
using System.Linq;
using PeekSeek.Gamemodes;
using SDG.Unturned;

namespace PeekSeek.Hud;

/// One player's face on the bar: who they are, what they took into the round,
/// whether they are still in it, and how much of them is left.
internal class HudSeat {
    internal ulong Steam64;
    internal string AvatarUrl;
    internal ushort PickItemId;
    internal bool Alive = true;

    /// 0 to 100. The bar only draws it between those: full health has nothing to say
    /// and a dead face already says it.
    internal int Health = 100;
}

internal class HudTeam {
    internal string Name;
    internal int Score;
    internal HudRole Role = HudRole.None;
    internal List<HudSeat> Seats = [];
}

/// What the bar shows at any moment. Match builds one whenever something about the
/// match changes and hands it over; the bar works out who sees what.
internal class HudState {
    internal HudTeam[] Teams = [new HudTeam(), new HudTeam()];

    /// Seconds on the clock, or -1 for no clock, which leaves no gap where it was.
    internal int SecondsLeft = -1;

    /// Who may see everybody's health rather than only their own team's.
    internal HashSet<ulong> Spectators = [];
}

/// The bar along the top of the screen during a match: score and clock on one block
/// in the middle, a face per player to either side with their pick on its corner and
/// their health under it, the viewer's own team always on the left. Up for everybody
/// for the whole match; it also stands in front of the game's own arena text, which
/// cannot be turned off.
///
/// Every change redraws the whole bar for every viewer, which is what makes it unable
/// to drift: the server never has to know what a client still has. The one exception
/// is the tick, which sends only the clock and any health that moved, against a
/// record of what each viewer was last sent; a redraw resets that record. Pictures
/// are not re-downloaded by a redraw; the client caches them by URL.
///
/// Nothing on the block is placed by a pixel count. The score line is a layout row,
/// so a marker or a clock a gamemode does not have is not a hole in the row: the
/// rest closes up round it.
internal class MatchHud {
    private const int Seats = 5;
    private const int HealthSteps = 10;

    /// At or under this the bar is amber rather than green.
    private const int LowHealth = 50;

    private EffectAsset effect;
    private bool effectLookedUp;

    /// What each viewer was last sent that the tick may resend: the clock, and every
    /// seat's health as steps lit and which colour. Absent means never drawn to.
    private readonly Dictionary<ulong, Drawn> drawn = [];

    private sealed class Drawn {
        internal string Clock;
        internal readonly Dictionary<string, (int Steps, bool Low)> Health = [];
    }

    internal bool IsAvailable => Effect() != null;

    /// Put the bar up for everybody, or bring it up to date if it already is.
    internal void Show(HudState state) {
        if (Effect() == null) return;

        foreach (SteamPlayer viewer in Provider.clients.ToList()) {
            Draw(viewer, state);
        }
    }

    /// The clock and any health that moved, for viewers who have the bar. Kept apart
    /// from Show so the frequent call is a handful of messages rather than the lot.
    internal void Tick(HudState state) {
        if (Effect() == null) return;

        foreach (SteamPlayer viewer in Provider.clients.ToList()) {
            ulong me = viewer.playerID.steamID.m_SteamID;
            if (!drawn.TryGetValue(me, out Drawn record)) continue;

            HudState seen = FromTheViewpointOf(state, me);
            string clock = Clock(seen.SecondsLeft);
            if (record.Clock != clock) {
                record.Clock = clock;
                Visible(viewer, "Timer", seen.SecondsLeft >= 0);
                Send(viewer, "Timer", clock);
            }

            ForEachSeat(seen, (slot, seat) => {
                (int Steps, bool Low) health = HealthOf(seat, seen, me);
                if (record.Health.TryGetValue(slot, out (int Steps, bool Low) last) && last == health) return;

                record.Health[slot] = health;
                DrawHealth(viewer, slot, health);
            });
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

    /// A player who has left takes their bar with them. Called after the leaving
    /// redraw, because Unturned raises the disconnect while they are still a client.
    internal void Forget(ulong steam64) {
        drawn.Remove(steam64);
    }

    // ---- drawing --------------------------------------------------------------

    private void Draw(SteamPlayer viewer, HudState state) {
        ulong me = viewer.playerID.steamID.m_SteamID;
        HudState seen = FromTheViewpointOf(state, me);
        Drawn record = new() { Clock = Clock(seen.SecondsLeft) };
        drawn[me] = record;

        EffectManager.SendUIEffect(Effect(), PeekSeek.Config.MatchHudKey, viewer.transportConnection, true);

        for (int team = 0; team < 2; team++) {
            Send(viewer, $"Team{team}_Name", seen.Teams[team].Name);
            Send(viewer, $"Score{team}", seen.Teams[team].Score.ToString());
            Visible(viewer, $"Castle{team}", seen.Teams[team].Role == HudRole.Holding);
            Visible(viewer, $"Swords{team}", seen.Teams[team].Role == HudRole.Attacking);
        }
        Visible(viewer, "Timer", seen.SecondsLeft >= 0);
        Send(viewer, "Timer", record.Clock);

        for (int team = 0; team < 2; team++) {
            string side = team == 0 ? "L" : "R";
            for (int i = 0; i < Seats; i++) {
                HudSeat seat = seen.Teams[team].Seats.ElementAtOrDefault(i);
                Visible(viewer, $"{side}{i}", seat != null);
                if (seat == null) continue;

                (int Steps, bool Low) health = HealthOf(seat, seen, me);
                record.Health[$"{side}{i}"] = health;
                DrawSeat(viewer, $"{side}{i}", seat, health, MaySee(seat, seen, me));
            }
        }
    }

    /// A face is two pictures of the same download, one at full strength and one at
    /// half, because nothing a server sends can set an alpha but it can choose which
    /// of two images is on. Out of the round is the half one. The pick and the health
    /// are only drawn for a viewer who may see them: what the other side brought is
    /// the one thing a raid must not know.
    private static void DrawSeat(SteamPlayer viewer, string slot, HudSeat seat, (int Steps, bool Low) health, bool inside) {
        Picture(viewer, $"{slot}_Avatar", seat.AvatarUrl);
        Picture(viewer, $"{slot}_Faded", seat.AvatarUrl);
        Visible(viewer, $"{slot}_Avatar", seat.Alive);
        Visible(viewer, $"{slot}_Faded", !seat.Alive);

        Visible(viewer, $"{slot}_Pick", inside && seat.PickItemId != 0);
        Picture(viewer, $"{slot}_Pick_Icon", inside ? ItemIcon(seat.PickItemId) : "");

        DrawHealth(viewer, slot, health);
    }

    /// The bar under a face: `steps` of ten lit, in green or in amber, or the whole
    /// thing away. Two bars share the slot, one per colour, because a server can
    /// choose which image is on but not what colour it is.
    private static void DrawHealth(SteamPlayer viewer, string slot, (int Steps, bool Low) health) {
        Visible(viewer, $"{slot}_Health", health.Steps > 0);
        if (health.Steps == 0) return;

        string colour = health.Low ? "Amber" : "Green";
        Visible(viewer, $"{slot}_HealthGreen", !health.Low);
        Visible(viewer, $"{slot}_HealthAmber", health.Low);
        for (int i = 0; i < HealthSteps; i++) {
            Visible(viewer, $"{slot}_{colour}{i}", i < health.Steps);
        }
    }

    /// What one viewer sees of a seat's health, as lit steps: nothing for a face that
    /// is whole or out of the round, and nothing for a seat the viewer may not see
    /// inside.
    private static (int Steps, bool Low) HealthOf(HudSeat seat, HudState seen, ulong viewer) {
        if (!seat.Alive || seat.Health >= 100 || seat.Health <= 0) return (0, false);
        if (!MaySee(seat, seen, viewer)) return (0, false);

        int steps = (seat.Health * HealthSteps + 99) / 100;
        return (steps, seat.Health <= LowHealth);
    }

    /// Whether a viewer sees inside a seat, its pick and its health: their own
    /// teammates', and every seat for a spectator. `seen` is already turned to the
    /// viewer, so their own team, if they have one, is the first.
    private static bool MaySee(HudSeat seat, HudState seen, ulong viewer) {
        if (seen.Spectators.Contains(viewer)) return true;

        return seen.Teams[0].Seats.Contains(seat) && seen.Teams[0].Seats.Any(mate => mate.Steam64 == viewer);
    }

    private static void ForEachSeat(HudState seen, System.Action<string, HudSeat> visit) {
        for (int team = 0; team < 2; team++) {
            string side = team == 0 ? "L" : "R";
            for (int i = 0; i < Seats && i < seen.Teams[team].Seats.Count; i++) {
                visit($"{side}{i}", seen.Teams[team].Seats[i]);
            }
        }
    }

    /// The viewer's own team on the left, the same way every shooter does it. Somebody
    /// who is on neither team sees the teams in the order the match gave them.
    private static HudState FromTheViewpointOf(HudState state, ulong viewer) {
        bool onSecond = state.Teams[1].Seats.Any(seat => seat.Steam64 == viewer);

        return new HudState {
            Teams = onSecond ? [state.Teams[1], state.Teams[0]] : [state.Teams[0], state.Teams[1]],
            SecondsLeft = state.SecondsLeft,
            Spectators = state.Spectators
        };
    }

    // ---- the wire ---------------------------------------------------------------

    private static string Clock(int seconds) {
        return seconds < 0 ? "" : $"{seconds / 60}:{seconds % 60:00}";
    }

    private static string ItemIcon(ushort itemId) {
        return itemId == 0 ? "" : string.Format(PeekSeek.Config.ItemIconURL, itemId);
    }

    private static void Send(SteamPlayer viewer, string child, string text) {
        EffectManager.sendUIEffectText(PeekSeek.Config.MatchHudKey, viewer.transportConnection, true, child, text ?? "");
    }

    private static void Visible(SteamPlayer viewer, string child, bool visible) {
        EffectManager.sendUIEffectVisibility(PeekSeek.Config.MatchHudKey, viewer.transportConnection, true, child, visible);
    }

    /// An empty URL is sent as an empty URL: the game then leaves the image disabled,
    /// which is the right look for "no picture".
    private static void Picture(SteamPlayer viewer, string child, string url) {
        EffectManager.sendUIEffectImageURL(PeekSeek.Config.MatchHudKey, viewer.transportConnection, true, child, url ?? "");
    }

    private EffectAsset Effect() {
        if (effectLookedUp) return effect;
        effectLookedUp = true;

        effect = Effects.Find(PeekSeek.Config.MatchHudEffectGUID, "MatchHudEffectGUID", "No match HUD.");
        return effect;
    }
}
