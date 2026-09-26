using System.Xml.Serialization;

namespace PeekSeekLobby.Core.Models;

/// One gamemode tile on the queue screen, as the site describes it. The bit is its
/// place in the queue bitmask, which is its index in the site's whole gamemode list
/// rather than in the handful a player is shown: a gamemode kept out of matchmaking
/// leaves a gap, and closing that gap would queue everybody for the wrong thing.
public class QueueGamemode {
    [XmlAttribute]
    public int Bit;

    [XmlAttribute]
    public string Name;

    /// The line under the name: the kits and the format, the way the site's own
    /// gamemode tile says them.
    [XmlAttribute]
    public string Blurb;

    [XmlAttribute]
    public bool Ranked;
}

/// One region tile, and the code the site knows it by.
public class QueueRegion {
    [XmlAttribute]
    public string Name;

    [XmlAttribute]
    public string Code;
}
