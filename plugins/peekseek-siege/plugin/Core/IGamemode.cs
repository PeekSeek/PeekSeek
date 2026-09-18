using System.Collections.Generic;
using SDG.Unturned;
using Steamworks;

namespace PeekSeekSiege.Core;

/// One team, as a gamemode needs to see it: a name, the Unturned group its players
/// are in, and who those players are.
///
/// The group id is passed in rather than created here because the main PeekSeek
/// plugin already makes groups when it starts a match, and two plugins both calling
/// GroupManager.addGroup for the same team would put teammates in different groups.
internal class GamemodeTeam {
    internal string Name;
    internal ulong GroupId;
    internal List<ulong> Players = [];

    internal bool Has(ulong steam64) => Players.Contains(steam64);
}

/// Everything a gamemode is told about the match it is running.
internal class GamemodeMatch {
    internal int Id;
    internal List<GamemodeTeam> Teams = [];

    /// Whether this is one person testing the whole loop on their own. Solo skips
    /// every rule that only makes sense with an opponent, and is the only reason a
    /// match with one player is allowed to start at all.
    internal bool Solo;

    internal GamemodeTeam TeamOf(ulong steam64) {
        return Teams.Find(team => team.Has(steam64));
    }

    internal int TeamIndexOf(ulong steam64) {
        return Teams.FindIndex(team => team.Has(steam64));
    }
}

/// The seam the main PeekSeek plugin will drive gamemodes through.
///
/// PeekSeek's match server today has one hard-coded loop inside PSManager. When
/// gamemodes land there, the shape it needs is this: hand the mode a match, tick it,
/// tell it about players arriving, leaving and dying, and end it. Siege is written
/// against that interface now so the eventual move is a registration line rather than
/// a rewrite — nothing in Siege reaches for PSManager, and nothing outside SiegeHost
/// knows how this match was started.
internal interface IGamemode {
    string Name { get; }

    void Begin(GamemodeMatch match);

    /// Called every fixed update. The mode decides what is worth acting on.
    void Tick();

    void OnPlayerJoined(Player player);

    void OnPlayerLeft(CSteamID steamID);

    /// Called after the player is already dead, so `player.life.isDead` is true.
    void OnPlayerDied(Player player, CSteamID killer);

    /// Called when Unturned has respawned the player, wherever it put them. A mode that
    /// cares where its players stand has to answer this, or a respawn lands on a map
    /// spawn with nothing in hand.
    void OnPlayerRevived(Player player);

    void End();
}
