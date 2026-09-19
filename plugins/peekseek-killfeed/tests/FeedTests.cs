using System.Linq;
using PeekSeekKillFeed;
using Xunit;

namespace PeekSeekKillFeed.Tests;

public class FeedTests {
    [Fact]
    public void NewestIsFirstAndOnlyAScreenIsKept() {
        Feed feed = new(rows: 2, lifetime: 10f);
        feed.Add(1, "a", 2, "b", 0, 0f);
        feed.Add(1, "a", 3, "c", 0, 1f);
        feed.Add(1, "a", 4, "d", 0, 2f);

        Assert.Equal(["d", "c"], feed.Rows.Select(kill => kill.VictimName).ToArray());
    }

    /// The badge counts kills in a life, not in a row: two kills a minute apart are
    /// still a double, and a death starts the count again.
    [Fact]
    public void TheStreakIsPerLife() {
        Feed feed = new(rows: 6, lifetime: 10f);

        Assert.Equal(1, feed.Add(1, "a", 2, "b", 0, 0f).Streak);
        Assert.Equal(2, feed.Add(1, "a", 3, "c", 0, 60f).Streak);

        feed.EndLife(1);
        Assert.Equal(1, feed.Add(1, "a", 4, "d", 0, 61f).Streak);
    }

    [Fact]
    public void NobodyAndYourselfCountForNothing() {
        Feed feed = new(rows: 6, lifetime: 10f);

        Assert.Equal(0, feed.Add(0, "", 2, "b", 0, 0f).Streak);
        Assert.Equal(0, feed.Add(2, "b", 2, "b", 0, 0f).Streak);
    }

    [Fact]
    public void RowsExpire() {
        Feed feed = new(rows: 6, lifetime: 5f);
        feed.Add(1, "a", 2, "b", 0, 0f);
        feed.Add(1, "a", 3, "c", 0, 4f);

        Assert.True(feed.Expire(6f));
        Assert.Single(feed.Rows);
        Assert.False(feed.Expire(6f));
    }

    [Theory]
    [InlineData(0, "")]
    [InlineData(1, "")]
    [InlineData(2, "II")]
    [InlineData(4, "IV")]
    [InlineData(9, "IX")]
    [InlineData(10, "X")]
    [InlineData(14, "X")]
    public void TheBadgeIsRomanFromTwoToTen(int streak, string badge) {
        Assert.Equal(badge, Feed.Badge(streak));
    }

    [Theory]
    [InlineData("Ferret", 14, "Ferret")]
    [InlineData("justin23@hotmail.com", 14, "justin23@hotm…")]
    [InlineData("", 14, "")]
    [InlineData(null, 14, "")]
    public void NamesAreCutWithAnEllipsis(string name, int max, string shown) {
        Assert.Equal(shown, Feed.Fit(name, max));
    }
}
