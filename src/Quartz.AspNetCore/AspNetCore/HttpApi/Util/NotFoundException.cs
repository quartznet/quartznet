namespace Quartz.AspNetCore.HttpApi.Util;

internal class NotFoundException : Exception
{
    private NotFoundException(string message) : base(message)
    {
    }

    // Keep in sync with Quartz.HttpClient.HttpClientExtensions
    public static NotFoundException ForScheduler(string schedulerName) => new($"Unknown scheduler {schedulerName}");

    public static NotFoundException ForCalendar(string calendarName) => new($"Unknown calendar {calendarName}");

    public static NotFoundException ForJob(JobKey key) => new($"Unknown job {key}");

    public static NotFoundException ForTrigger(TriggerKey key) => new($"Unknown trigger {key}");
}