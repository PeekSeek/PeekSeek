using PeekSeekLobby.Core;
using Rocket.API.Collections;
using Rocket.Core.Logging;
using Rocket.Core.Plugins;

namespace PeekSeekLobby;

public class PeekSeekLobby : RocketPlugin<PeekSeekLobbyConfiguration> {
    public static PeekSeekLobby Instance { get; private set; }

    public static PeekSeekLobbyConfiguration Config { get => Instance.Configuration.Instance; }

    /// Rocket loads every DLL under Plugins/, subfolders included, so a stale copy
    /// left next to the current one comes up as a second plugin — and since both
    /// carry the same assembly name, that second instance shares LobbyManager's
    /// statics. Letting it run would subscribe every event twice and tick twice a
    /// frame: two heartbeats per player, two welcome messages, two of every kill
    /// feed line. Whoever loads first owns the lobby; the rest do nothing at all.
    protected override void Load() {
        if (Instance != null) {
            Logger.LogWarning(
                "PeekSeekLobby is already loaded — ignoring this copy. Look for a "
                + "leftover PeekSeekLobby.dll elsewhere under Rocket/Plugins.");
            return;
        }

        Instance = this;
        string olderFile = PeekSeek.Core.ConfigDefaults.Sentence(Config.FillInWhatOlderFilesLack(), "PeekSeekLobby.configuration.xml");
        if (olderFile != null) Logger.Log(olderFile);
        Logger.Log($"PeekSeekLobby {Assembly.GetName().Version} is now loaded.");

        LobbyManager.Init();
    }

    /// Rocket plugins are MonoBehaviours, same heartbeat the main PeekSeek plugin
    /// drives its match loop from — LobbyManager decides how often that's worth
    /// acting on.
    private void FixedUpdate() {
        if (Instance != this) return;
        LobbyManager.Tick();
    }

    protected override void Unload() {
        if (Instance != this) return;

        Logger.Log("PeekSeekLobby is now unloaded.");
        LobbyManager.Shutdown();
        Instance = null;
    }

    /// Highlighting is <color=white> against the grey base chat colour, matching
    /// the convention the competitive PeekSeek servers already use.
    ///
    /// Chat is the last place an answer goes. Kills are the feed, a death is the
    /// card, the queue and the arena's clock are the bar, a match is the banner and
    /// the scores are the board. What is left here is one line on arrival, the
    /// replies to commands, and the boards' own text.
    public override TranslationList DefaultTranslations => new TranslationList() {
        { "welcome", "<color=white>PeekSeek warm-up lobby.</color> <color=white>/kit {0}</color> switch loadout. <color=white>/q</color> queue for a match. <color=white>/afk</color> step out. <color=white>/report</color> somebody." },

        { "target_protected", "<color=white>{0}</color> still has spawn protection." },
        { "spawn_protection_over", "Spawn protection <color=white>ended</color>." },

        // The solo range. Said to somebody who is on their own, so it leads with
        // the fact that they are: being first in is the thing they are deciding
        // what to do about.
        { "range_hint", "Nobody else here yet. <color=white>/range</color> puts up a timed target run while you wait." },
        { "range_started", "<color=white>{0} targets.</color> One hit each. <color=white>Go.</color>" },
        { "range_hit", "<color=white>{0}</color> left." },
        { "range_done", "Cleared in <color=white>{0}s</color>. Your best is <color=white>{1}s</color>." },
        { "range_done_best", "Cleared in <color=white>{0}s</color>. <color=white>New personal best.</color>" },
        { "range_stopped", "Targets <color=white>cleared</color>." },
        { "range_not_alone", "The range is for when you are here on your own. There are <color=white>{0}</color> others in the arena to shoot at." },
        { "range_unavailable", "The range isn't set up on this server." },
        { "range_ended_players", "<color=white>Somebody joined.</color> Targets cleared, back to the arena." },

        // Reporting is the one thing here a player does about somebody else, so the
        // wording stays factual: it says what was sent, not what will happen to them.
        { "report_usage", "<color=white>/report <player> <reason></color> sends staff their screen and this round's stats." },
        { "report_no_player", "Nobody here called <color=white>{0}</color>." },
        { "report_self", "You cannot report yourself." },
        { "report_sent", "Reported <color=white>{0}</color>. Staff have been sent their screen and this round's stats." },

        // The queue screen: the site's play page, asked in game.
        { "queue_headline", "Find a match" },
        { "queue_go", "FIND MATCH" },
        { "queue_no", "Not now" },
        { "queue_high_ping", "Your ping is {0} ms. A match cuts your damage from {1} ms, so a closer region will play better." },

        { "afk_on", "You are <color=white>AFK</color> until you type <color=white>/afk</color> again." },
        { "afk_off", "You are <color=white>back</color>." },
        { "queue_disabled", "Matchmaking is <color=white>disabled</color> on this server right now." },
        { "no_player_found", "No player found matching \"{0}\"." },
        { "no_stats", "That player has no stats recorded yet." },
        { "kit_options", "Available kits: <color=white>{0}</color>. Type <color=white>/kit</color> plus a name to switch." },
        { "kit_invalid", "\"{0}\" isn't a kit you can use. Available: <color=white>{1}</color>" },
        { "kit_switched", "Switched to your <color=white>{0}</color> kit." },

        // The banners: a match landing, and a match that ended without them.
        { "match_found_name", "Get ready" },
        { "match_found_line", "Out of the arena in {0} seconds" },
        { "match_over_name", "Back in the arena" },
        { "match_over_line", "That match ended without you" },

        { "board_title", "Lobby" },
        { "match_found_browser", "Open this and log in to select your kit and start your match" },
    };
}
