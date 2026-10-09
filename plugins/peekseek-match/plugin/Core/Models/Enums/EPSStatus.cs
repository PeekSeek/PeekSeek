namespace PeekSeek.Core.Models.Enums;

public enum EPSStatus {
    /// Free. Asking the site for work.
    READY,

    /// Holding a match whose players are still choosing. Nobody may join yet, the
    /// match data that says who is even on it has not arrived.
    WAITING,

    /// Running a match.
    IN_PROGRESS,

    /// The match has been won and the result screen is up; the roster is cleared out
    /// once DelayKickAfterWin has passed.
    DELAY
}
