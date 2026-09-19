using Rocket.Core.Logging;
using Rocket.Core.Plugins;

namespace PeekSeekKillFeed;

/// A kill feed at the top right of everybody's screen: who killed whom with what,
/// the weapon a white silhouette, a roman numeral from a player's second kill of a
/// life, and a red hairline round any row you are in.
///
/// Its own plugin rather than a part of Siege because every PeekSeek server wants
/// one and only the debug server runs Siege. It knows nothing about rounds; a life
/// is a life, which on Siege is the same thing.
public class PeekSeekKillFeed : RocketPlugin<KillFeedConfiguration> {
    public static PeekSeekKillFeed Instance { get; private set; }

    public static KillFeedConfiguration Config { get => Instance.Configuration.Instance; }

    private KillFeed feed;

    /// Rocket loads every DLL under Plugins/, subfolders included, so a stale copy left
    /// beside the current one comes up as a second plugin sharing this one's statics.
    /// Whoever loads first owns the feed.
    protected override void Load() {
        if (Instance != null) {
            Logger.LogWarning("PeekSeekKillFeed is already loaded; ignoring this copy.");
            return;
        }

        Instance = this;
        feed = new KillFeed();
        feed.Hook();
        Logger.Log($"PeekSeekKillFeed {Assembly.GetName().Version} is now loaded.");
    }

    private void FixedUpdate() {
        if (Instance != this) return;
        feed.Tick();
    }

    protected override void Unload() {
        if (Instance != this) return;

        feed.Unhook();
        feed.Clear();
        Instance = null;
    }
}
