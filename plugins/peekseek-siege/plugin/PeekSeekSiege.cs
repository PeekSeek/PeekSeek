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

        // The footprint board. Two tiles; the first answer on a team is the team's.
        { "board_shape", "Footprint. The first pick on the team counts." },
        { "shape_square", "Square" },
        { "shape_square_blurb", "Nine metal floors, three by three. Walls and roofs only." },
        { "shape_hexagonal", "Hexagonal" },
        { "shape_hexagonal_blurb", "Six triangles round the middle, a square on every side. Triangle pieces in the kit." },

        // The swap board, while building: the piece you salvaged in every other
        // material, and a way to take it down. {0} is what is left of that material,
        // {1} the piece's health.
        { "swap_left", "{0} left. {1} health." },
        { "swap_none", "None left. {1} health." },
        { "swap_none_left", "No <color=white>{0}</color> left." },
        { "swap_remove", "Remove" },
        { "swap_remove_blurb", "Take it down. It goes back on the pile." },

        // The one line a round is allowed: who took it and how. The score is on the HUD
        // and the reason is the thing it cannot show.
        { "round_won_wipe", "<color=white>{0}</color> take round {1}. Nobody left." },
        { "round_won_clock", "<color=white>{0}</color> hold round {1}. The flag is still up." },
        { "round_won_flag", "<color=white>{0}</color> take round {1}. The flag is down." },
        { "round_won_skipped", "Round {1} given to <color=white>{0}</color>." },
        { "match_won", "<color=white>{0}</color> win it <color=white>{1}-{2}</color>. Well played." },
        { "match_stopped", "Match <color=white>stopped</color>." },

        // Said to the one defender handed the flag, because nothing on screen says it
        // is them, and again if they carry it off the base.
        { "flag_bearer", "<color=white>You have the flag.</color> Stand it anywhere on your base. {0} seconds, or it goes on a free tile." },
        { "flag_leashed", "<color=white>Stay on your base</color> until the flag is down." },

        // The defenders' board says the one word it is; the tiles say the rest, and
        // the HUD already says which round and which side.
        { "board_defend", "Powerup" },

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
