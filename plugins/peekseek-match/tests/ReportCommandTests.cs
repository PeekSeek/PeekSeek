using Xunit;

namespace PeekSeek.Tests;

/// How /report splits what somebody typed.
///
/// RocketMod hands a command its arguments already split on spaces, so the reason is
/// everything after the player name rather than the single word that follows it.
/// Making people write /report thomas "walling through B" to get a whole sentence
/// through is the kind of thing that stops reports being written at all.
public class ReportCommandTests {
    /// The same expression ReportCommand.Execute uses to rebuild the reason.
    private static string Reason(string[] command) =>
        string.Join(" ", command, 1, command.Length - 1);

    [Fact]
    public void EverythingAfterTheNameIsTheReason() {
        Assert.Equal("walling through B site", Reason(["thomas", "walling", "through", "B", "site"]));
    }

    [Fact]
    public void OneWordStillWorks() {
        Assert.Equal("aimbot", Reason(["thomas", "aimbot"]));
    }

    [Fact]
    public void PunctuationSurvives() {
        Assert.Equal("snapping through smoke, twice!",
            Reason(["idkxd", "snapping", "through", "smoke,", "twice!"]));
    }
}
