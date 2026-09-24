using PeekSeek.Gamemodes;
using SDG.Unturned;
using Steamworks;

namespace PeekSeekArena;

/// The arena round, as a gamemode.
///
/// Unturned's own LevelManager runs the round: CLEAR wipes the map, WARMUP counts down,
/// SPAWN drops everybody in and PLAY shrinks the circle until one group is left. The
/// patches in Patches/ are what make that loop PeekSeek's: teams come down on the
/// configured spawns in rotation, spectators are not counted, and a round ends through
/// Match so it is scored and reported. This class is the little that is not inside
/// those patches.
internal class Arena : IGamemode {
    public string Name => "Arena";

    /// Whether this is the gamemode running right now. The patches hang on LevelManager
    /// for the life of the plugin and must do nothing while another gamemode has the
    /// server, or while the match is over and everybody is looking at the score.
    internal bool InPlay => running && Host.InPlay;

    private bool running;

    public void Begin(GamemodeMatch match) {
        running = true;

        foreach (Player player in Host.Players()) Host.Kit(player);

        LevelManager.arenaState = EArenaState.CLEAR;
    }

    public void Tick() { }

    public void OnPlayerJoined(Player player) {
        Host.Kit(player);
    }

    public void OnPlayerLeft(CSteamID steamID) { }

    /// The dead come straight back to the lobby; Match remembers they are out of the
    /// round, so the overlay and the bar keep saying so until the next one.
    public void OnPlayerDied(Player player, CSteamID killer) {
        player.life.ServerRespawn(false);
    }

    public void OnPlayerRevived(Player player) {
        Host.Kit(player);
    }

    public void End() {
        running = false;
        LevelManager.arenaState = EArenaState.LOBBY;
    }

    /// A round went to the team a player is on. First to the match's rounds wins it.
    internal void RoundWon(ulong steam64) {
        int team = Host.Match.TeamIndexOf(steam64);
        if (team < 0) return;

        Host.RoundWon(team);
        if (Host.Score(team) >= Host.RoundsToWin) Host.MatchWon(team);
    }

    /// The block is the two scores and nothing between them: the arena has no clock,
    /// and no word to put over one.
    public HudRole RoleOf(int team) => HudRole.None;

    public ushort PickOf(ulong steam64) => 0;

    public int SecondsLeft => -1;

    public string PhaseLabel => null;

    public bool IsMatchPoint(int team) => Host.Score(team) + 1 >= Host.RoundsToWin;
}
