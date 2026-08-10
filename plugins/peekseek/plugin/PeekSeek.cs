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
        { "damage_reduced", "Your damage was reduced because you have higher ping!"}
    };
}
