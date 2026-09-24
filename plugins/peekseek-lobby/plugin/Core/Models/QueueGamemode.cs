using System.Xml.Serialization;

namespace PeekSeekLobby.Core.Models;

/// One gamemode tile on the queue screen. The order is the bitmask's: the first entry
/// is bit 0, which is the order the site's mmconfig lists its gamemodes in and the
/// convention GamemodeBitmask already uses.
public class QueueGamemode {
    [XmlAttribute]
    public string Name;

    /// The line under the name: the kits and the format, the way the site's own
    /// gamemode tile says them.
    [XmlAttribute]
    public string Blurb;
}

/// One region tile, and the code the site knows it by.
public class QueueRegion {
    [XmlAttribute]
    public string Name;

    [XmlAttribute]
    public string Code;
}
