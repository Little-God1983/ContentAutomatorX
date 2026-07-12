using Cronos;

namespace ContentAutomatorX.Application.Scheduling;

public static class CronDue
{
    public static bool IsDue(string cron, DateTimeOffset? lastRun, DateTimeOffset now)
    {
        if (lastRun is null) return true;
        try
        {
            var expression = CronExpression.Parse(cron);
            var next = expression.GetNextOccurrence(lastRun.Value, TimeZoneInfo.Utc);
            return next is not null && next <= now;
        }
        catch (CronFormatException)
        {
            return false;
        }
    }
}
