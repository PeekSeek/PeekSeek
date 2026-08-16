using System.Collections.Generic;
using System.Linq;

namespace PeekSeekLobby.Core.Models;

/// Which of the players who want a match are actually offered one right now.
///
/// A lobby with people in it is the best advertising this project has: the Unturned
/// browser sorts on player count, and a server showing two to five is one people
/// join on sight, where an empty one is invisible. Every 1v1 that starts takes two
/// of those people out of the window at once, so matchmaking left to itself will
/// happily strip the lobby back to nobody and switch off the thing bringing the next
/// player in.
///
/// So the lobby keeps a floor. Only the surplus above it is offered up, longest
/// waiting first. The hold is not allowed to be permanent — a player who came for a
/// match and is still standing there minutes later is a player about to quit, and
/// keeping the window lit by holding people hostage in it loses the very people it
/// is meant to attract — so waiting long enough releases you regardless.
internal static class QueueHold {
    /// The players who may be queued this tick.
    ///
    /// `waiting` is everyone who wants a match, against the moment they started
    /// wanting one. Nobody is dropped for good: whoever is held here is simply not
    /// offered on this tick and is first in line on the next.
    internal static HashSet<ulong> Offered(
        Dictionary<ulong, float> waiting,
        int lobbyPopulation,
        int minPopulation,
        float maxHoldSeconds,
        float now) {
        // Held longest-waiting-first, so the hold is a queue rather than a lottery
        // that a player can lose several times over.
        List<KeyValuePair<ulong, float>> byWait = waiting.OrderBy(entry => entry.Value).ToList();

        int surplus = lobbyPopulation - minPopulation;
        HashSet<ulong> offered = [];

        for (int i = 0; i < byWait.Count; i++) {
            bool withinSurplus = i < surplus;
            bool waitedTooLong = maxHoldSeconds > 0 && now - byWait[i].Value >= maxHoldSeconds;
            if (withinSurplus || waitedTooLong) offered.Add(byWait[i].Key);
        }

        return offered;
    }
}
