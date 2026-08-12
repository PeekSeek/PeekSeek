using PeekSeekLobby.Core;
using Rocket.API.Collections;
using Rocket.Core.Logging;
using Rocket.Core.Plugins;

namespace PeekSeekLobby;

public class PeekSeekLobby : RocketPlugin<PeekSeekLobbyConfiguration> {
    public static PeekSeekLobby Instance { get; private set; }

    public static PeekSeekLobbyConfiguration Config { get => Instance.Configuration.Instance; }

    protected override void Load() {
        Instance = this;
        Logger.Log($"PeekSeekLobby {Assembly.GetName().Version} is now loaded.");

        LobbyManager.Init();
    }

    /// Rocket plugins are MonoBehaviours, same heartbeat the main PeekSeek plugin
    /// drives its match loop from — LobbyManager decides how often that's worth
    /// acting on.
    private void FixedUpdate() {
        LobbyManager.Tick();
    }

    protected override void Unload() {
        Logger.Log("PeekSeekLobby is now unloaded.");
        LobbyManager.Shutdown();
    }

    public override TranslationList DefaultTranslations => new TranslationList() {
        { "welcome", "Welcome to the PeekSeek lobby! You're in queue for a match — fight while you wait. /noqueue to leave the queue." },
        { "kill_feed", "{0} killed {1} with {2}" },
        { "kill_feed_headshot", "{0} killed {1} with {2} (HEADSHOT)" },
        { "respawn_in", "Respawning in {0}..." },
        { "queued", "You are back in the matchmaking queue." },
        { "not_queued", "You have left the matchmaking queue — you will not be matched." },
        { "no_player_found", "No player found matching \"{0}\"." },
        { "no_stats", "That player has no stats recorded yet." },
        { "kit_options", "Available kits: {0}. /kit <name> to switch." },
        { "kit_invalid", "\"{0}\" isn't a kit you can use. Available: {1}" },
        { "kit_switched", "Switched to your {0} kit." },
        { "discord_prompt", "Join the PeekSeek Discord for matches, tournaments and updates!" },
    };
}
