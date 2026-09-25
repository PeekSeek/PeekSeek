using System;
using System.Collections.Generic;
using System.Linq;

namespace PeekSeekRaid.Core.Models;

/// How much bigger a kit gets for every attacker past the first.
///
/// A budget is sized off the team it will be raided by, not the team building it: it
/// is there to hold off the explosives coming at it, and those scale with the other
/// side. `extraPerEnemy` says how much of a piece each extra attacker is worth: all of
/// it for metal, which is what a charge is counted in, half for wood, none for a
/// doorway, because more ways in never made a base harder to take.
public static class TeamSize {
    public static List<RaidItem> ForEnemies(IEnumerable<RaidItem> kit, int enemies, Func<ushort, double> extraPerEnemy) {
        int extra = Math.Max(1, enemies) - 1;
        return kit.Select(item => item.Scaled(1 + extraPerEnemy(item.ItemId) * extra)).ToList();
    }
}
