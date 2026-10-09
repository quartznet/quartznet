namespace Quartz.AspNetCore.HttpApi.Util;

internal sealed class NotFoundException : Exception
{
    public NotFoundException(string message) : base(message)
    {
    }

    public NotFoundException(string? message, Exception? innerException) : base(message, innerException)
    {
    }

    // Keep in sync with Quartz.HttpClientExtensions.EnsureSuccess
    public static NotFoundException ForScheduler(string schedulerName) => new($"Unknown scheduler {schedulerName}");

    /// <summary>
    /// The refusal for a bare name that only targets hold, which names them: the scheduler exists in
    /// this process's listing, under keys the API cannot address.
    /// </summary>
    public static NotFoundException ForScheduler(string schedulerName, IReadOnlyList<string> targetsHolding)
    {
        if (targetsHolding.Count == 0)
        {
            return ForScheduler(schedulerName);
        }

        return new NotFoundException(
            $"Unknown scheduler {schedulerName}: the name is held only by "
            + $"{(targetsHolding.Count == 1 ? "target" : "targets")} {string.Join(", ", targetsHolding)}, "
            + "which this API does not address by name.");
    }

    public static NotFoundException ForCalendar(string calendarName) => new($"Unknown calendar {calendarName}");

    public static NotFoundException ForJob(JobKey key) => new($"Unknown job {key}");

    public static NotFoundException ForTrigger(TriggerKey key) => new($"Unknown trigger {key}");

    public static NotFoundException ForExecution(string entryId) => new($"Unknown execution {entryId}");

    public static NotFoundException ForJobRunStatus(JobKey key) => new($"No recorded run of job {key}");
}