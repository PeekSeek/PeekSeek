namespace PeekSeekSiege.Core;

/// How a number is said to a player. Kept away from anything Unity so a test can read
/// it without a game running.
internal static class Wording {
    /// "5 minutes", "90 seconds", "1 minute". A build phase configured in seconds used
    /// to be announced as "0 minutes" the moment it was shorter than one, which is
    /// every test server.
    internal static string Duration(int seconds) {
        if (seconds % 60 != 0 || seconds < 60) {
            return seconds == 1 ? "1 second" : $"{seconds} seconds";
        }

        int minutes = seconds / 60;
        return minutes == 1 ? "1 minute" : $"{minutes} minutes";
    }
}
