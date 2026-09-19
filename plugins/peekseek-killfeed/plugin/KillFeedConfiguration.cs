using Rocket.API;

namespace PeekSeekKillFeed;

public class KillFeedConfiguration : IRocketPluginConfiguration {
    /// The Unity UI the feed is drawn with, as the GUID of an EffectAsset in the
    /// PeekSeek Kill Feed workshop item: `Assets/PeekSeekKillFeed/Bundles/Effects/
    /// KillFeed/KillFeed.dat` in the Siege repo's `ui/` project. Left empty, the plugin
    /// does nothing at all; there is no chat form of a kill feed worth having.
    public string EffectGUID;

    /// The channel the feed is sent on. Any small number no other plugin on the server
    /// is using; it only has to be stable, so the feed can be redrawn and cleared.
    public short EffectKey;

    /// Where a weapon's picture comes from, with `{0}` standing for the item id. The
    /// site serves every item for its loadout editor.
    public string ItemIconURL;

    /// How many rows the prefab has, how long a row stays, and how many characters
    /// of a name fit its column.
    public int Rows;
    public float RowSeconds;
    public int NameLength;

    public void LoadDefaults() {
        EffectGUID = "c4d5e6f708192a3b4c5d6e7f8091a2b3";
        EffectKey = 27320;
        ItemIconURL = "https://peekseek.net/static/unturned/{0}.png";
        Rows = 6;
        RowSeconds = 7f;
        NameLength = 14;
    }
}
