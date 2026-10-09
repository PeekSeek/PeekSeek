using System.Xml.Serialization;
using PeekSeek.Gamemodes;

namespace PeekSeekRaid.Core.Models;

/// One item to hand a player, by id and count. `State` is the comma-separated byte
/// string the site's kit config already uses for attachments and ammo, so an item
/// copied out of a gamemode file can be pasted straight into this plugin's config.
public class RaidItem {
    [XmlAttribute]
    public ushort ItemId;

    [XmlAttribute]
    public byte Amount = 1;

    [XmlAttribute]
    public string State = "";

    /// A gun's attachments and loaded magazine, by item id, for a gun whose state is
    /// easier to say than to spell in bytes. Any of them set and the state is built by
    /// the game's own ItemGunAsset.getState when the item is handed out, with Ammo as
    /// the rounds loaded (0 for a full magazine).
    [XmlAttribute]
    public ushort Sight;

    [XmlAttribute]
    public ushort Tactical;

    [XmlAttribute]
    public ushort Grip;

    [XmlAttribute]
    public ushort Barrel;

    [XmlAttribute]
    public ushort Magazine;

    [XmlAttribute]
    public byte Ammo;

    public bool HasAttachments => Sight != 0 || Tactical != 0 || Grip != 0 || Barrel != 0 || Magazine != 0;

    public RaidItem() { }

    public RaidItem(ushort itemId, byte amount) {
        ItemId = itemId;
        Amount = amount;
    }

    /// The attachment/ammo bytes for this item, or an empty array when the config
    /// left State blank, which is every item that has no profile to stamp on.
    public byte[] StateBytes() {
        if (string.IsNullOrWhiteSpace(State)) return [];

        string[] parts = State.Split(',');
        byte[] bytes = new byte[parts.Length];
        for (int i = 0; i < parts.Length; i++) {
            byte.TryParse(parts[i].Trim(), out bytes[i]);
        }
        return bytes;
    }

    /// The same item at `by` times the amount, rounded down, with a hair of slack so 10
    /// at 0.7 is 7 and not 6.99 to 6.
    public RaidItem Scaled(double by) {
        return new RaidItem(ItemId, (byte)System.Math.Min(byte.MaxValue, System.Math.Floor(Amount * by + 1e-4)));
    }

    public GamemodeItem ToGamemodeItem() {
        return new GamemodeItem { ItemId = ItemId, Amount = Amount, State = StateBytes() };
    }
}
