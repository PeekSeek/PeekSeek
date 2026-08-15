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
    /// the convention the competitive PeekSeek servers already use — Unturned's
    /// rich text is enabled on every message these send.
    public override TranslationList DefaultTranslations => new TranslationList() {
        // Front-loaded on purpose: what the server is and the loop you're in
        // first, then the controls, then the things that can wait.
        { "welcome_what", "<color=white>PeekSeek warm-up lobby.</color> Free-for-all deathmatch. Death sends you to spawn, then back into the fight after <color=white>{0}s</color> with <color=white>{1}s</color> protection." },
        { "welcome_what_queue", "<color=white>PeekSeek warm-up lobby.</color> You are <color=white>in queue</color>, fight while you wait. Death sends you to spawn, then back into the fight after <color=white>{0}s</color> with <color=white>{1}s</color> protection." },
        { "welcome_commands", "<color=white>/kit {0}</color> switch loadout. <color=white>/kd</color> your score. <color=white>/ms</color> your latency. <color=white>/afk</color> step out.{1}" },
        { "welcome_extra", "Every kill earns a <color=white>heal</color>. Kills in a row earn <color=white>more</color>. Arena and scores reset every <color=white>{0} min</color>. Discord: <color=white>peekseek.net/discord</color>" },

        { "kill_feed", "<color=white>{0}</color> killed <color=white>{1}</color> with {2}." },
        { "kill_feed_headshot", "<color=white>{0}</color> headshot <color=white>{1}</color> with {2}." },
        { "kill_feed_streak", "<color=white>{0}</color> killed <color=white>{1}</color> with {2}. <color=white>{3} streak</color>." },
        { "kill_feed_headshot_streak", "<color=white>{0}</color> headshot <color=white>{1}</color> with {2}. <color=white>{3} streak</color>." },

        { "deploying_in", "Back into the fight in <color=white>{0}s</color>." },
        { "spawn_protection_over", "Spawn protection <color=white>ended</color>. You can be hurt now." },
        { "target_protected", "<color=white>{0}</color> still has spawn protection." },

        { "leaderboard_header", "<color=white>Round over.</color> Top of the last <color=white>{0} min</color>:" },
        { "leaderboard_entry", "<color=white>{0}.</color> {1}: <color=white>{2}</color>K {3}D, <color=white>{4}%</color> HS" },
        { "leaderboard_empty", "<color=white>Round over.</color> Nobody scored a kill. Arena and scores reset." },
        { "arena_reset", "Arena cleared and scores reset. <color=white>Go.</color>" },

        // The solo range. Said to somebody who is on their own, so it leads with
        // the fact that they are — being first in is the thing they are deciding
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

        // Framed as warming the arena up rather than as a restriction, because that
        // is what it is: they are in the queue, this lobby just is not volunteering
        // them for a match while it is this quiet.
        { "queue_held", "Warming up the arena first. Matches start once there are <color=white>{0}+</color> here, and you are still in the queue." },

        { "queued", "You are back in the matchmaking queue." },
        { "not_queued", "You have <color=white>left the queue</color>. You will not be matched." },
        { "afk_on", "You are <color=white>AFK</color> and stay out of the arena until you type <color=white>/afk</color> again. You are still in the queue." },
        { "afk_off", "You are <color=white>back</color>. Into the arena in <color=white>{0}s</color>." },
        { "queue_disabled", "Matchmaking is <color=white>disabled</color> on this server right now." },
        { "no_player_found", "No player found matching \"{0}\"." },
        { "no_stats", "That player has no stats recorded yet." },
        { "kit_options", "Available kits: <color=white>{0}</color>. Type <color=white>/kit</color> plus a name to switch." },
        { "kit_invalid", "\"{0}\" isn't a kit you can use. Available: <color=white>{1}</color>" },
        { "kit_switched", "Switched to your <color=white>{0}</color> kit." },
        { "discord_prompt", "Join the PeekSeek Discord for matches, tournaments and updates!" },
        // Said the moment a match lands, then counted down, so nobody is teleported
        // out of a fight without warning and without knowing why.
        { "match_found_warning", "<color=white>Match found.</color> You are out of the arena in <color=white>{0}s</color>. Finish up." },
        { "match_countdown", "Leaving for your match in <color=white>{0}</color>..." },

        { "match_found", "<color=white>Match found.</color> Open the page that just appeared to log in and pick your kit." },
        { "match_found_browser", "Open this and log in to select your kit and start your match" },
        { "match_started", "Your match is <color=white>ready</color>. Sending you to the server now." },
    };
}
