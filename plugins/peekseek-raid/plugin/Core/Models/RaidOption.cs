using System.Collections.Generic;
using System.Linq;
using System.Xml.Serialization;
using PeekSeek.Hud;

namespace PeekSeekRaid.Core.Models;

/// One powerup a player can take into a round. Defenders pick from the defence pool,
/// attackers from the raid pool, the board shows two of each class, and the pick is
/// spent when the round ends: this is the per-round decision the base itself no longer
/// is.
///
/// Name and Blurb are what the board shows. Keep the blurb to one short line: it has
/// to read at a glance while a five-second timer runs.
public class RaidOption {
    [XmlAttribute]
    public string Name;

    [XmlAttribute]
    public string Blurb;

    [XmlAttribute]
    public PowerupClass Category;

    /// A picture for a powerup that is not an item, like the Insertion heli: a file
    /// beside the item pictures on the site, named without its extension.
    [XmlAttribute]
    public string Picture;

    /// What picking it does beyond the items, for the few powerups that are about the
    /// player rather than what they carry.
    [XmlAttribute]
    public Twist Twist;

    /// The item whose picture the tile shows, or 0 for the first item in the list.
    /// Only worth setting when the first item is not what the option is about: a
    /// grenade is a poor picture of a watchtower.
    [XmlAttribute]
    public ushort Icon;

    [XmlArray]
    public List<RaidItem> Items = [];

    public ushort IconItemId => Icon != 0 ? Icon : Items.Count > 0 ? Items[0].ItemId : (ushort)0;

    /// The option as a tile on one of Match's boards.
    public BoardOption ToBoard() {
        return new BoardOption {
            Name = Name,
            Blurb = Blurb,
            Icon = Icon,
            Items = Items.Select(item => item.ToGamemodeItem()).ToList()
        };
    }
}
