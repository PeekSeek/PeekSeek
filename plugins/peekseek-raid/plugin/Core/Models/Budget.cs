using System.Collections.Generic;
using System.Linq;

namespace PeekSeekRaid.Core.Models;

/// How many of each piece a team has left to lay.
///
/// A count, not an inventory. The inventory a builder carries is one of every piece
/// with a count above zero, and this is what the panel beside the screen shows and
/// what a placement spends. Kept apart from Unturned so the arithmetic can be tested,
/// and so a swap that refunds a metal floor nobody was ever issued is just a count
/// going from zero to one.
public class Budget {
    private readonly Dictionary<ushort, int> left = [];

    /// Ids in the order they were first seen, which is the order the panel lists them
    /// in: the configured kit first, in its own order, then anything refunded later.
    private readonly List<ushort> order = [];

    public Budget(IEnumerable<RaidItem> supplies) {
        foreach (RaidItem item in supplies) Add(item.ItemId, item.Amount);
    }

    public int Left(ushort id) => left.TryGetValue(id, out int count) ? count : 0;

    public bool Has(ushort id) => Left(id) > 0;

    /// Every id ever in the budget, with what is left of it, in panel order.
    public IEnumerable<(ushort Id, int Count)> Rows => order.Select(id => (id, left[id]));

    public IEnumerable<ushort> Ids => order;

    /// Take one, or refuse.
    public bool Spend(ushort id) {
        if (!Has(id)) return false;

        left[id]--;
        return true;
    }

    public void Refund(ushort id) => Add(id, 1);

    private void Add(ushort id, int amount) {
        if (!left.ContainsKey(id)) {
            left[id] = 0;
            order.Add(id);
        }

        left[id] += amount;
    }
}
