using System.Xml.Serialization;
using PeekSeek.Gamemodes;

namespace PeekSeekRaid.Core.Models;

/// One item to hand a player, by id and count. `State` is the comma-separated byte
/// string the site's kit config already uses for attachments and ammo, so an item
/// copied out of mmconfig.json can be pasted straight into this plugin's config.
public class RaidItem {
    [XmlAttribute]
    public ushort ItemId;

    [XmlAttribute]
    public byte Amount = 1;

    [XmlAttribute]
    public string State = "";

    public RaidItem() { }

    public RaidItem(ushort itemId, byte amount) {
        ItemId = itemId;
        Amount = amount;
    }

    /// The attachment/ammo bytes for this item, or an empty array when the config
    /// left State blank — which is every item that has no profile to stamp on.
    public byte[] StateBytes() {
        if (string.IsNullOrWhiteSpace(State)) return [];

        string[] parts = State.Split(',');
        byte[] bytes = new byte[parts.Length];
        for (int i = 0; i < parts.Length; i++) {
            byte.TryParse(parts[i].Trim(), out bytes[i]);
        }
        return bytes;
    }

    public GamemodeItem ToGamemodeItem() {
        return new GamemodeItem { ItemId = ItemId, Amount = Amount, State = StateBytes() };
    }
}
