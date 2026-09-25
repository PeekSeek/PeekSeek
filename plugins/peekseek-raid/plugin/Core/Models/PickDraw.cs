using System;
using System.Collections.Generic;
using System.Linq;

namespace PeekSeekRaid.Core.Models;

/// What a player who never picked ends up with. A powerup has one taker a team, so it
/// is one nobody on the team has, at random. Kept apart from Unturned so it can be
/// tested.
public static class PickDraw {
    /// A tile nobody on the team has taken, or -1 when every tile is taken or there are
    /// none.
    public static int Fallback(int tiles, ICollection<int> taken, Random random) {
        List<int> open = Enumerable.Range(0, tiles).Where(i => !taken.Contains(i)).ToList();
        return open.Count == 0 ? -1 : open[random.Next(open.Count)];
    }
}
