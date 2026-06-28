using OrderAlert.Core.Services;

namespace OrderAlert.Core.Tests;

public sealed class ScheduleWindowTests
{
    [Theory]
    [InlineData("08:00", "22:00", "12:00", true)]
    [InlineData("08:00", "22:00", "23:00", false)]
    [InlineData("22:00", "06:00", "23:00", true)]
    [InlineData("22:00", "06:00", "05:59", true)]
    [InlineData("22:00", "06:00", "12:00", false)]
    public void Contains_supports_normal_and_cross_midnight_windows(
        string start,
        string end,
        string current,
        bool expected)
    {
        var window = new ScheduleWindow(
            TimeOnly.Parse(start),
            TimeOnly.Parse(end));

        Assert.Equal(expected, window.Contains(TimeOnly.Parse(current)));
    }

    [Fact]
    public void NextRun_moves_to_the_next_opening_when_outside_the_window()
    {
        var window = new ScheduleWindow(new TimeOnly(8, 0), new TimeOnly(22, 0));
        var now = new DateTimeOffset(2026, 6, 23, 23, 0, 0, TimeSpan.FromHours(8));

        Assert.Equal(
            new DateTimeOffset(2026, 6, 24, 8, 0, 0, TimeSpan.FromHours(8)),
            window.NextRun(now, null, TimeSpan.FromMinutes(15)));
    }

    [Fact]
    public void NextRun_uses_the_interval_after_the_last_run()
    {
        var window = new ScheduleWindow(new TimeOnly(8, 0), new TimeOnly(22, 0));
        var now = new DateTimeOffset(2026, 6, 23, 10, 0, 0, TimeSpan.FromHours(8));

        Assert.Equal(
            now.AddMinutes(10),
            window.NextRun(now, now.AddMinutes(-5), TimeSpan.FromMinutes(15)));
    }
}
