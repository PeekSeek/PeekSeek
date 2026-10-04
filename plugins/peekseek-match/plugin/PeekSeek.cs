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
        string olderFile = Core.ConfigDefaults.Sentence(Config.FillInWhatOlderFilesLack(), "PeekSeek.configuration.xml");
        if (olderFile != null) Logger.Log(olderFile);
        Logger.Log($"PeekSeek {Assembly.GetName().Version} is now loaded.");
        OnLoad?.Invoke();

        CoreDumps.Swept swept = CoreDumps.Sweep(Directory);
        if (swept.Cleared > 0) {
            Logger.Log($"Cleared {swept.Cleared} crash dump(s), freeing {swept.FreedMegabytes} MB.");
        }
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

    /// Highlighting is <color=white> against the grey base chat colour, the same
    /// convention the warm-up lobby uses, so a player moving between the two reads the
    /// same kind of line in the same shape.
    ///
    /// Chat is the last place an answer goes. What is left here is said once, on
    /// arrival, to one person: what they are playing, and that they are a spectator.
    /// Rounds, deaths, the match and the ping are on screen, where they stay.
    public override TranslationList DefaultTranslations => new TranslationList() {
        // Front-loaded on purpose: the mode and the map first, then the rules that
        // change how it plays, then what is happening right now.
        { "welcome_match", "<color=white>{0}</color> on <color=white>{1}</color>. First to <color=white>{2}</color> rounds. Bullet drop <color=white>{3}</color>, {4} movement." },
        { "welcome_waiting", "Waiting for the rest of the players. The round starts the moment everyone is here." },
        { "welcome_spectator", "You are <color=white>spectating</color>. Freecam and player stats are turned on for you." }
    };
}
