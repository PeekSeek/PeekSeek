using System.Collections.Generic;
using System.Xml.Serialization;

namespace PeekSeekSiege.Core.Models;

/// One of the five things a player can take into a round. Defenders pick from the
/// defence list, attackers from the raid list, and the pick is spent when the round
/// ends — this is the per-round decision the base itself no longer is.
///
/// Name and Blurb are what the board shows. Keep the blurb to one short line: it has
/// to read at a glance while a five-second timer runs.
public class SiegeOption {
    [XmlAttribute]
    public string Name;

    [XmlAttribute]
    public string Blurb;

    /// The item whose picture the tile shows, or 0 for the first item in the list.
    /// Only worth setting when the first item is not what the option is about: a
    /// grenade is a poor picture of a watchtower.
    [XmlAttribute]
    public ushort Icon;

    [XmlArray]
    public List<SiegeItem> Items = [];

    public ushort IconItemId => Icon != 0 ? Icon : Items.Count > 0 ? Items[0].ItemId : (ushort)0;
}
