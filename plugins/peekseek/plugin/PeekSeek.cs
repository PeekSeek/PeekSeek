using PeekSeek.Core;
using Rocket.API.Collections;
using Rocket.Core.Logging;
using Rocket.Core.Plugins;

namespace PeekSeek;

public class PeekSeek : RocketPlugin<PeekSeekConfiguration> {
    public static PeekSeek Instance { get; private set; }

    public static PeekSeekConfiguration Config { get => Instance.Configuration.Instance; }

    public delegate void OnLoadHandler();
    public OnLoadHandler OnLoad;

    public delegate void OnUnloadHandler();
    public OnUnloadHandler OnUnload;

    protected override void Load() {
        Instance = this;
        Logger.Log($"PeekSeek {Assembly.GetName().Version} is now loaded.");
        OnLoad?.Invoke();

        PSManager.Init();
    }

    /// Rocket plugins are MonoBehaviours, so this is the match loop's heartbeat.
    /// PSManager decides how often that is worth acting on.
    private void FixedUpdate() {
        PSManager.Tick();
    }

    protected override void Unload() {
        OnUnload?.Invoke();
        Logger.Log($"PeekSeek is now unloaded.");

        PSManager.Shutdown();
    }

    public override TranslationList DefaultTranslations => new TranslationList() {
        { "kill","{0} was killed by {1} in the {2}" },
        { "death","{0} died" },
        { "welcome","Welcome to a PeekSeek.net match!" },
        { "win","{0} won the round" },
        { "damage_reduced", "Your damage was reduced because you have higher ping!"},
        { "lobby_timeout_warning", "{0}s left to join. The match will only continue if there is more than 1 team active, otherwise the win is granted to the team online." },
        { "match_won", "{0} has won the match!" }
    };
}
