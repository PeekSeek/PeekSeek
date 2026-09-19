using PeekSeekSiege.Core;
using Rocket.API.Collections;
using Rocket.Core.Logging;
using Rocket.Core.Plugins;

namespace PeekSeekSiege;

public class PeekSeekSiege : RocketPlugin<PeekSeekSiegeConfiguration> {
    public static PeekSeekSiege Instance { get; private set; }

    public static PeekSeekSiegeConfiguration Config { get => Instance.Configuration.Instance; }

    /// Rocket loads every DLL under Plugins/, subfolders included, so a stale copy left
    /// beside the current one comes up as a second plugin sharing this one's statics —
    /// two ticks a frame, two of every announcement, and two gamemodes fighting over
    /// the same players. Whoever loads first owns the server.
    protected override void Load() {
        if (Instance != null) {
            Logger.LogWarning(
                "PeekSeekSiege is already loaded — ignoring this copy. Look for a "
                + "leftover PeekSeekSiege.dll elsewhere under Rocket/Plugins.");
            return;
        }

        Instance = this;
        Logger.Log($"PeekSeekSiege {Assembly.GetName().Version} is now loaded.");

        SiegeHost.Init();
    }

    /// Rocket plugins are MonoBehaviours, so this is the match loop's heartbeat. The
    /// gamemode decides how often that is worth acting on.
    private void FixedUpdate() {
        if (Instance != this) return;
        SiegeHost.Tick();
    }

    protected override void Unload() {
        if (Instance != this) return;

        Logger.Log("PeekSeekSiege is now unloaded.");
        SiegeHost.Shutdown();
        Instance = null;
    }

    /// Highlighting is <color=white> against the grey base colour, the same convention
    /// the match servers and the warm-up lobby use, so a player who moves between them
    /// reads the same kind of line in the same shape.
    ///
    /// Every line here answers a question somebody actually has at that moment. During
    /// the build phase that is "how long have I got and what am I allowed to do"; during
    /// a round it is "which side am I, where is the clock, why am I taking damage". The
    /// two that matter most are the off-base warning and the leash, because both are
    /// rules this gamemode invented and neither is visible on screen.
    public override TranslationList DefaultTranslations => new TranslationList() {
        { "idle_welcome", "<color=white>PeekSeek Siege.</color> Nothing is running. An admin starts a match with <color=white>/start</color>." },
        { "welcome", "<color=white>Siege</color> is in the <color=white>{0}</color> phase. Round <color=white>{1}</color>, score <color=white>{2}-{3}</color>." },

        // Said once, at the top, before anybody has done anything. It is the only place
        // the shape of the whole match is explained, so it says the shape and nothing else.
        { "match_intro", "<color=white>Siege.</color> Build a base, then take turns cracking each other's. <color=white>{0}</color> rounds each side. You get <color=white>{1}</color> to build." },

        { "build_start", "<color=white>Build.</color> {0} at <color=white>{1}</color>. The planner is in your hands and the whole kit is on you. You cannot leave your plot, and a floor only goes down against one that is already there." },
        { "build_shape", "<color=white>{0}</color> build on a <color=white>{1}</color> footprint." },

        // The footprint board. Three tiles; the first answer on a team is the team's.
        { "board_shape", "<color=white>Your footprint.</color> What the base starts from. First pick on your team counts." },
        { "shape_square", "Square" },
        { "shape_square_blurb", "Nine metal floors, three by three. Walls and roofs only." },
        { "shape_hexagonal", "Hexagonal" },
        { "shape_hexagonal_blurb", "Six triangles round the flag, a square on every side. Triangle pieces in the kit." },
        { "build_countdown", "<color=white>{0}s</color> of building left." },
        { "build_done", "<color=white>Bases locked.</color> {0} and {1} pieces. Nothing about them changes from here." },

        // The small label over the HUD clock. Short, because the slot is, and the last
        // two are the ones drawn in red.
        { "hud_shaping", "Footprint" },
        { "hud_building", "Build" },
        { "hud_round", "Round {0}" },
        { "hud_overtime", "Overtime" },
        { "hud_match_point", "Match point" },
        { "hud_final", "Final" },

        { "round_start", "<color=white>Round {0}.</color> <color=white>{1}</color> defends. Score <color=white>{2}-{3}</color>." },
        { "round_live", "<color=white>Go.</color> {0} seconds. Attackers take the flag down, defenders keep it up." },
        { "round_countdown", "<color=white>{0}s</color> left." },
        { "round_won_wipe", "<color=white>{0}</color> take round {1}. <color=white>{2}-{3}</color>." },
        { "round_won_clock", "<color=white>{0}</color> hold round {1}. The flag is still standing. <color=white>{2}-{3}</color>." },
        { "round_won_flag", "<color=white>{0}</color> took the flag down. Round {1} theirs. <color=white>{2}-{3}</color>." },
        { "round_won_skipped", "Round {1} given to <color=white>{0}</color>. <color=white>{2}-{3}</color>." },
        { "overtime", "Level after the swap. <color=white>Overtime</color>: {0} rounds each until somebody is ahead." },
        { "match_won", "<color=white>{0}</color> win it <color=white>{1}-{2}</color>. Well played." },
        { "match_stopped", "Match <color=white>stopped</color>." },

        // The board. The headline says which side you are and how long you have; the
        // options are numbered so a one-character answer is one people actually give.
        { "board_defend", "<color=white>Round {0}. You are defending {1}.</color> Pick what you hold it with." },
        { "board_attack", "<color=white>Round {0}. You are taking {1}.</color> Pick what you crack it with." },
        { "pick_option", "<color=white>{0}</color>  {1} — {2}" },
        { "pick_how", "Type <color=white>/pick</color> and a number." },
        { "pick_locked", "Locked in: <color=white>{0}</color>." },
        { "pick_no_such", "That is not on the board." },
        { "pick_closed", "There is nothing to pick right now." },

        // The two invented rules. Both are said the moment they bite, because neither
        // is something a player can see coming.
        { "off_base", "<color=white>Get back on your base.</color> Open ground is killing you." },
        { "leashed", "That is far enough. The other team is building where you were about to look." },

        { "start_already_running", "A match is already running. <color=white>/stop</color> first." },
        { "start_nobody_here", "Nobody is on the server." },
        { "start_need_two", "Siege needs two players, or <color=white>/start solo</color> to test it on your own." },
        { "start_no_sites", "This server has fewer than two sites configured. Check <color=white>Sites</color> in the config." },
        { "start_nothing_running", "No match is running." },

        { "siege_state", "Phase <color=white>{0}</color>, round <color=white>{1}</color>, <color=white>{2}s</color> left. <color=white>{3}</color> defends." },
        { "side_solo_only", "<color=white>/siege side</color> is for solo tests only." },

        { "find_usage", "<color=white>/siege find <text></color> looks an item up by name." },
        { "find_none", "Nothing is called anything like \"{0}\"." },
        { "find_hit", "<color=white>{0}</color> — id <color=white>{1}</color>" },
        { "find_more", "...and <color=white>{0}</color> more." }
    };
}
