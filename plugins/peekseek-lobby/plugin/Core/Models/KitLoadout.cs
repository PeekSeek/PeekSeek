using System.Collections.Generic;

namespace PeekSeekLobby.Core.Models;

/// One category from the website's /kit/{steam64} response, everything to equip
/// once (gun + outfit), plus the magazine to keep topped up separately. Magazine
/// is null for a gun with no attachment/ammo profile in the site's config.
internal class KitLoadout {
    internal List<PSItem> Items;
    internal PSItem Magazine;
}
