using SDG.Unturned;
using Steamworks;

namespace PeekSeek.Gamemodes;

/// Which end of a round a team is on, for the bar along the top. A gamemode with no
/// such thing answers None for both, and the bar shows nothing where the marker would
/// be, with no gap left for it.
public enum HudRole {
    None,
    Holding,
    Attacking
}

/// The seam a gamemode plugin is driven through.
///
/// PeekSeek Match decides that a match exists, who is on it, which team is which
/// Unturned group, who is a spectator, and when it is over; it keeps the score, the
/// stats and the result the site is owed, and draws the bar along the top. The
/// gamemode decides what happens in between: where people spawn, what they carry,
/// what a round is and who won it. A gamemode plugin registers one of these when it
/// loads (Gamemodes.Register) and is handed every match whose site gamemode it is
/// named for, or every match nothing else claims if it registered as the default.
///
/// Everything a gamemode may ask of Match is on Host.
public interface IGamemode {
    /// What the site calls this gamemode, matched without case against the name on
    /// the match it hands over.
    string Name { get; }

    /// The match is on, and everybody who is going to be here for the start is here.
    /// Whoever joins after this comes through OnPlayerJoined.
    void Begin(GamemodeMatch match);

    /// Every fixed update. The mode decides how often that is worth acting on.
    void Tick();

    void OnPlayerJoined(Player player);

    /// Raised while the leaver is still a client, so anything sent to them still
    /// arrives.
    void OnPlayerLeft(CSteamID steamID);

    /// After the player is already dead, so `player.life.isDead` is true. Whether and
    /// when they come back is the mode's: Match respawns nobody.
    void OnPlayerDied(Player player, CSteamID killer);

    /// Unturned has respawned the player, wherever it put them. A mode that cares
    /// where its players stand answers this, or a respawn lands on a map spawn with
    /// nothing in hand.
    void OnPlayerRevived(Player player);

    /// The match is over, or was stopped. Put the world and the players back.
    void End();

    /// The bar along the top: which end of this round each team is on, what each
    /// player took into it as an item to picture (0 for nothing), and the clock in
    /// seconds, or -1 for no clock. None of these leaves a gap on the bar when it is
    /// absent.
    HudRole RoleOf(int team);

    ushort PickOf(ulong steam64);

    int SecondsLeft { get; }

    /// A word over the clock, or null for the clock alone. The one phase a gamemode
    /// needs to name; everything else the clock and the roles already say.
    string PhaseLabel { get; }

    /// Whether the next round this team wins ends the match, which paints its score
    /// red. A gamemode with its own arithmetic for when a match is over answers it
    /// from that.
    bool IsMatchPoint(int team);
}
