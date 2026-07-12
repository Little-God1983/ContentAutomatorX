using ContentAutomatorX.Application.Scheduling;

namespace ContentAutomatorX.UnitTests;

public class CronDueTests
{
    private static readonly DateTimeOffset MondayNoon = new(2026, 7, 13, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Null_last_run_is_due_immediately() =>
        Assert.True(CronDue.IsDue("0 8 * * MON", null, MondayNoon));

    [Fact]
    public void Due_when_occurrence_passed_since_last_run() =>
        // last ran Sunday; Monday 08:00 occurrence has passed by Monday noon
        Assert.True(CronDue.IsDue("0 8 * * MON", MondayNoon.AddDays(-1), MondayNoon));

    [Fact]
    public void Not_due_when_already_ran_after_occurrence() =>
        // last ran Monday 09:00; next occurrence is next Monday
        Assert.False(CronDue.IsDue("0 8 * * MON", MondayNoon.AddHours(-3), MondayNoon));

    [Fact]
    public void Invalid_cron_is_never_due() =>
        Assert.False(CronDue.IsDue("not a cron", MondayNoon.AddDays(-1), MondayNoon));
}
