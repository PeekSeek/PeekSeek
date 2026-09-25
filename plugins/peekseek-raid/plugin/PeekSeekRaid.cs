using HarmonyLib;
using PeekSeek.Gamemodes;
using PeekSeekRaid.Core;
using Rocket.API.Collections;
using Rocket.Core.Logging;
using Rocket.Core.Plugins;

namespace PeekSeekRaid;

/// PeekSeek RAID: build a base, then take turns cracking each other's. A gamemode for
/// PeekSeek Match, which runs the match, the roster, the score and the screens; this
/// plugin is the rules. See README.md.
public class PeekSeekRaid : RocketPlugin<PeekSeekRaidConfiguration> {
    public static PeekSeekRaid Instance { get; private set; }

    public static PeekSeekRaidConfiguration Config { get => Instance.Configuration.Instance; }

    /// The gamemode, for the test console. Its Match is null between matches.
    internal static Raid Raid { get; private set; }

    /// Raid's own patches: the explosion record in Damage and the throwables in Powerups.
    private Harmony harmony;

    /// Rocket loads every DLL under Plugins/, subfolders included, so a stale copy left
    /// beside the current one comes up as a second plugin sharing this one's statics.
    /// Whoever loads first owns the server.
    protected override void Load() {
        if (Instance != null) {
            Logger.LogWarning(
                "PeekSeekRaid is already loaded; ignoring this copy. Look for a "
                + "leftover PeekSeekRaid.dll elsewhere under Rocket/Plugins.");
            return;
        }

        Instance = this;
        Raid = new Raid();
        harmony = new Harmony("peekseek.raid");
        harmony.PatchAll(Assembly);
        Gamemodes.Register(Raid);
        Logger.Log($"PeekSeekRaid {Assembly.GetName().Version} is now loaded.");
    }

    protected override void Unload() {
        if (Instance != this) return;

        Gamemodes.Unregister(Raid);
        harmony?.UnpatchAll("peekseek.raid");
        Raid = null;
        Instance = null;
    }

    /// Highlighting is <color=white> against the grey base colour, the same convention
    /// the match servers and the warm-up lobby use, so a player who moves between them
    /// reads the same kind of line in the same shape.
    ///
    /// Chat is the last place an answer goes. The phase, the round, the score and the
    /// flag are on the bar; a round's end is the banner; off base is a tag. What is
    /// left here is either a board's text or a line to one person about the one thing
    /// they are holding.
    public override TranslationList DefaultTranslations => new TranslationList() {
        // Said once, at the top, before anybody has done anything. It is the only place
        // the shape of the whole match is explained, so it says the shape and nothing else.
        { "match_intro", "<color=white>Raid.</color> Build a base, then take turns cracking each other's. <color=white>{0}</color> rounds each side. You get <color=white>{1}</color> to build." },

        // The footprint board. Two tiles; the first answer on a team is the team's.
        { "board_shape", "Footprint. The first pick on the team counts." },
        { "shape_square", "Square" },
        { "shape_square_blurb", "Nine metal floors, three by three. Walls and roofs only." },
        { "shape_hexagonal", "Hexagonal" },
        { "shape_hexagonal_blurb", "Six triangles round the middle, a square on every side. Triangle pieces in the kit." },

        // The swap board, while building: the other shapes the piece you salvaged can
        // take, free, and a way to take it down. {0} is the shape's health.
        { "swap_form", "Same piece, another shape. {0} health." },
        { "swap_none_left", "No <color=white>{0}</color> left." },
        { "swap_remove", "Remove" },
        { "swap_remove_blurb", "Take it down. It goes back on the pile." },

        // The banner's line under the winner's name: how the round was won.
        { "round_won_wipe", "{0} wiped out {1}" },
        { "round_won_ace", "{0} aced {1}" },
        { "round_won_clock", "The flag is still up" },
        { "round_won_flag", "{0} destroyed the flag" },
        { "half_time", "Attackers get {0} more charges" },
        { "round_won_skipped", "Given by the console" },

        // Said to the one defender handed the flag, because nothing on screen says it
        // is them, and again if they carry it off the base.
        { "flag_bearer", "<color=white>You have the flag.</color> Stand it anywhere on your base. {0} seconds, or it goes on a free tile." },
        { "flag_door", "The flag goes at least <color=white>{0} m</color> from any door." },
        { "flag_breach", "Too sealed in: <color=white>{0}</color> charges to reach it here. At most <color=white>{1}</color>." },
        { "breach_downgraded", "Too hard to get in. Turned to maple: <color=white>{0}</color>." },
        { "striker_too_close", "Not on your own base. The smoke has to land <color=white>{0} m</color> out." },
        { "wall_breach", "That wall seals the flag in. At most <color=white>{0}</color> charges to reach it." },
        { "flag_leashed", "<color=white>Stay on your base.</color> The attackers are on their way." },
        { "flag_where", "The flag goes on one of the <color=white>starting floors</color>, and not next to a door." },

        // The boards' headlines: which round, which base, and what the board wants.
        { "board_defend", "Round {0}  You defend {1}" },
        { "board_attack", "Round {0}  You attack {1}" },
        { "board_attack_line", "Where you come in, then what you bring" },

        // The leash while building. Said the moment it bites, because it is a rule
        // nobody can see coming.
        { "leashed", "That is far enough. The other team is building where you were about to look." },

        { "raid_nothing_running", "No Raid match is running." },
        { "solo_started", "Playing solo: everybody not here counts as present. <color=white>/nextphase</color> moves it on." },
        { "raidtime_usage", "<color=white>/raidtime <seconds></color> sets the clock of the current phase." },

        { "raid_state", "Phase <color=white>{0}</color>, round <color=white>{1}</color>, <color=white>{2}s</color> left. <color=white>{3}</color> defends." },

        { "find_usage", "<color=white>/raid find <text></color> looks an item up by name." },
        { "find_none", "Nothing is called anything like \"{0}\"." },
        { "find_hit", "<color=white>{0}</color>  id <color=white>{1}</color>" },
        { "find_more", "...and <color=white>{0}</color> more." }
    };
}
