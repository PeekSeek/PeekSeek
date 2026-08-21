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
    /// Every message answers a question a player in a match actually has: what am I
    /// playing, how long until it starts, what is the score, why did that shot do less
    /// than it should have. Ordinary kills stay silent — the stream overlay carries the
    /// kill feed — so what is left is either a moment worth marking for everyone or a
    /// private line to the one person it happened to.
    public override TranslationList DefaultTranslations => new TranslationList() {
        // Front-loaded on purpose: the mode and the map first, then the rules that
        // change how it plays, then what is happening right now.
        { "welcome_match", "<color=white>{0}</color> on <color=white>{1}</color>. First to <color=white>{2}</color> rounds. Bullet drop <color=white>{3}</color>, {4} movement." },
        { "welcome_waiting", "Waiting for the rest of the players. The round starts the moment everyone is here." },
        { "welcome_spectator", "You are <color=white>spectating</color>. Freecam and player stats are turned on for you." },

        { "lobby_countdown", "<color=white>{0}s</color> to join. If only one team is here when that runs out, that team takes the match." },
        { "lobby_everyone_here", "Everyone is here. <color=white>Starting now.</color>" },

        { "round_start", "<color=white>Round {0}.</color> {1}" },
        { "round_won", "<color=white>{0}</color> won round {1}. {2}" },
        { "match_won", "<color=white>{0}</color> wins the match {1}. Well played." },
        { "match_forfeit", "<color=white>{0}</color> takes the match. Nobody from the other side turned up." },
        { "match_over", "Match over. Sending you back to the lobby." },

        { "ace", "<color=white>{0}</color> aced the round." },
        { "mvp", "MVP <color=white>{0}</color>. <color=white>{1}</color> kills, <color=white>{2}%</color> headshots." },
        { "revenge", "Revenge. You got <color=white>{0}</color> back." },
        { "close_call", "You finished the round on <color=white>{0} HP</color>. That was close." },
        { "round_damage", "Your round: {0}" },
        // Ping scaling is invisible otherwise: the shooter just sees their hits do less
        // than they should and concludes the server is broken.
        { "damage_reduced", "Your damage is <color=white>cut to {0}%</color>. Your ping is <color=white>{1}ms</color>." },

        { "death_fall", "You fell to your death." },
        { "death_drown", "You drowned." },
        { "death_burn", "You burned to death." },
        { "death_freeze", "You froze to death." },
        { "death_bleed", "You bled out." },
        { "death_suicide", "You ended it yourself." },
        { "death_explosive", "You didn't survive the blast." },
        { "death_generic", "You died." }
    };
}
