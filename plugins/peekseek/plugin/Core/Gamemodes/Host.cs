using System.Collections.Generic;
using PeekSeek.Core;
using PeekSeek.Core.Web;
using PeekSeek.Hud;
using SDG.Unturned;
using Steamworks;

namespace PeekSeek.Gamemodes;

/// Everything a gamemode may ask of the match server it is running under.
///
/// A gamemode never reaches into Match: it is told what it needs through IGamemode and
/// asks for the rest here. That keeps the seam one file wide in each direction, so a
/// gamemode written against it does not change when the server does.
public static class Host {
    /// The match being run, or null between matches.
    public static GamemodeMatch Match => PSManager.Match;

    /// Whether a match is on and the gamemode has it: not over, not waiting, not the
    /// moment after the win when everybody is still looking at the score.
    public static bool InPlay => PSManager.Status == Core.Models.Enums.EPSStatus.IN_PROGRESS && PSManager.Running != null;

    /// Everyone on the server who is playing this match: no spectators, no strangers.
    public static IEnumerable<Player> Players() => PSManager.Players();

    public static bool IsSpectator(ulong steam64) => PSManager.IsSpectator(steam64);

    /// Whether a player has not died since the round began. Kept by Match rather than
    /// read off the player, because a mode that respawns the dead at once still needs
    /// to know they are out of the round.
    public static bool IsAlive(ulong steam64) => StatService.IsAliveThisRound(new CSteamID(steam64));

    /// Everybody is back in play. A gamemode says so as a round starts.
    public static void BeginRound() => StatService.BeginRound();

    public static int Score(int team) => PSManager.Score(team);

    public static int RoundsPlayed => PSManager.RoundsPlayed;

    public static int RoundsToWin => PSManager.Data?.RoundsToWin ?? 0;

    /// The score in team order, "2-1", or with names when there are more than two teams.
    public static string ScoreLine() => PSManager.ScoreLine();

    /// The round goes to a team: scored, announced and sent to the overlay. Say
    /// `announcement` to replace the plain line with how it was won.
    public static void RoundWon(int team, string announcement = null) => PSManager.RoundWon(team, announcement);

    /// The gamemode calls the match. The result goes to the site and everybody goes
    /// home after a moment with the score.
    public static void MatchWon(int team) => PSManager.MatchWon(team);

    /// The only team still on the server takes the match, or null when that is not
    /// the situation. A gamemode asks between rounds, where an empty side is a
    /// forfeit rather than a wipe.
    public static int TeamLeftAlone() {
        Core.Models.PSTeam only = PSManager.Data == null ? null : PSManager.OnlyTeamOnline();
        return only == null ? -1 : PSManager.Data.Teams.IndexOf(only);
    }

    public static void Forfeit(int team) {
        if (PSManager.Data != null && team >= 0 && team < PSManager.Data.Teams.Count) {
            PSManager.NotifyTeamForcedWin(PSManager.Data.Teams[team]);
        }
    }

    /// Nobody is left at all. The match is released unplayed.
    public static void Abandon() => PSManager.AbandonMatch();

    /// Strip a player and dress them in the kit the match holds for them, at full
    /// health.
    public static void Kit(Player player) => PSManager.AssignPlayerKit(player);

    /// Redraw the bar for everybody. A gamemode calls this when something it answers
    /// through IGamemode has changed: a role, a pick.
    public static void RefreshHud() => PSManager.RefreshHud();

    public static PickBoard Board => PSManager.Board;

    public static BuildPanel Panel => PSManager.Panel;

    /// A player's Steam picture, or the placeholder while it is still being fetched.
    public static string AvatarFor(ulong steam64) => RemoteAPI.AvatarFor(steam64);

    /// Chat in the house style: grey, with `<color=white>` on the words that matter,
    /// under the PeekSeek icon.
    public static void Announce(string message) => PSManager.Announce(message);

    public static void Whisper(SteamPlayer to, string message) => PSManager.Whisper(to, message);
}
