using System.Collections.Generic;
using System.Linq;

namespace PeekSeek.Gamemodes;

/// One item in a player's kit: the id, how many, and the attachment bytes the site
/// resolved for it.
public class GamemodeItem {
    public ushort ItemId;
    public byte Amount = 1;
    public byte[] State = [];
}

/// One team as a gamemode sees it: a name, the Unturned group its players are in,
/// and who those players are. The group is Match's to make, because two plugins both
/// calling GroupManager.addGroup for one team would put teammates in different
/// groups.
public class GamemodeTeam {
    public string Name;
    public ulong GroupId;
    public List<ulong> Players = [];

    public bool Has(ulong steam64) => Players.Contains(steam64);
}

/// Everything a gamemode is told about the match it is running.
public class GamemodeMatch {
    /// The site's id.
    public int Id;

    public List<GamemodeTeam> Teams = [];

    public List<ulong> Spectators = [];

    /// The kit the site resolved for each player. Empty for a spectator, or for a
    /// player the site sent none for.
    public Dictionary<ulong, List<GamemodeItem>> Kits = [];

    public GamemodeTeam TeamOf(ulong steam64) => Teams.Find(team => team.Has(steam64));

    public int TeamIndexOf(ulong steam64) => Teams.FindIndex(team => team.Has(steam64));

    public List<GamemodeItem> KitFor(ulong steam64) {
        return Kits.TryGetValue(steam64, out List<GamemodeItem> kit) ? kit : [];
    }

    public bool HasPlayer(ulong steam64) => Teams.Any(team => team.Has(steam64));
}
