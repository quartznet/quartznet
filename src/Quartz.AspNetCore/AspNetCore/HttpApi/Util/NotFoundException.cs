namespace Quartz.AspNetCore.HttpApi.Util;

internal sealed class NotFoundException : Exception
{
    public NotFoundException(string message) : base(message)
    {
    }

    public NotFoundException(string? message, Exception? innerException) : base(message, innerException)
    {
    }

    // Keep in sync with Quartz.HttpClient.HttpClientExtensions.CheckResponseStatusCode
    public static NotFoundException ForScheduler(string schedulerName) => new($"Unknown scheduler {schedulerName}");

    public static NotFoundException ForCalendar(string calendarName) => new($"Unknown calendar {calendarName}");

    public static NotFoundException ForJob(JobKey key) => new($"Unknown job {key}");

    public static NotFoundException ForTrigger(TriggerKey key) => new($"Unknown trigger {key}");
}