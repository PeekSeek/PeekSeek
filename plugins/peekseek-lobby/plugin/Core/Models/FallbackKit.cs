using System.Collections.Generic;
using System.Linq;

namespace PeekSeekLobby.Core.Models;

/// A configured item, in the same [id, amount, state] terms the website's kit
/// data uses, but written out as XML fields so it can live in the plugin's own
/// configuration file.
public class FallbackItem {
    public ushort ItemID { get; set; }
    public byte Amount { get; set; }

    /// Comma-separated attachment/ammo bytes, exactly as the website encodes
    /// them (see metagun.py). Empty for an item with no state, like clothing.
    public string State { get; set; }
}

/// The loadout handed out when the website can't be reached. Without this a
/// failed fetch left players standing in the arena with nothing at all, no
/// gun, no clothes, which is worse than a slightly-wrong kit.
public class FallbackKit {
    public string Category { get; set; }
    public List<FallbackItem> Items { get; set; }
    public FallbackItem Magazine { get; set; }

    internal KitLoadout ToLoadout() {
        return new KitLoadout {
            Items = (Items ?? []).Select(ToItem).ToList(),
            Magazine = Magazine != null ? ToItem(Magazine) : null
        };
    }

    private static PSItem ToItem(FallbackItem item) {
        return new PSItem {
            ItemID = item.ItemID,
            Amount = item.Amount,
            State = string.IsNullOrEmpty(item.State)
                ? []
                : item.State.Split(',').Select(byte.Parse).ToArray()
        };
    }
}
