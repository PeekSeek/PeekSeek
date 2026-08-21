using System;
using System.Collections.Generic;

namespace PeekSeekLobby.Core.Models;

/// What the site is still waiting on a matched player to choose, and what they have
/// chosen so far.
///
/// A gamemode offering one kit or one powerup is not offering a choice — the site
/// resolves those itself and answers with an empty list — so empty means "do not put
/// this in front of them" rather than "they have nothing". That is what lets the lobby
/// prompt without knowing anything about the gamemode.
internal class MatchPicks {
    internal readonly List<string> Kits = [];
    internal readonly List<string> Powerups = [];

    internal string Kit;
    internal string Powerup;

    /// When the lobby stops waiting and locks whatever is left to its first option.
    /// Zero when there is nothing outstanding, or before the window has been started.
    internal float DecideBy;

    /// The last whole second the prompt was repeated on, so it is said on a schedule
    /// rather than every tick inside the same second.
    internal int LastPromptSecond;

    /// One option is not a choice, so it is never prompted for. The site already sends
    /// those lists empty, but the rule lives here as well rather than only there: this
    /// is the side that would put a menu with one item on it in front of somebody.
    internal bool NeedsKit => Kits.Count > 1 && Kit == null;
    internal bool NeedsPowerup => Powerups.Count > 1 && Powerup == null;
    internal bool Outstanding => NeedsKit || NeedsPowerup;

    /// The option a player meant, by its number in the prompt or by its name, or null
    /// for neither.
    ///
    /// Numbers because a one-character answer is one people actually give while a match
    /// is waiting on them; names because somebody who reads "sniper" types "sniper".
    internal string OptionFor(bool powerup, string choice) {
        if (string.IsNullOrEmpty(choice)) return null;

        List<string> options = powerup ? Powerups : Kits;
        if (int.TryParse(choice, out int index)) {
            return index >= 1 && index <= options.Count ? options[index - 1] : null;
        }

        foreach (string option in options) {
            if (option.Equals(choice, StringComparison.OrdinalIgnoreCase)) return option;
        }
        return null;
    }
}
