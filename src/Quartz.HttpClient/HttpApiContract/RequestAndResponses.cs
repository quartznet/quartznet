// ReSharper disable ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract - Can be null when received from Web API
// ReSharper disable NullCoalescingConditionIsAlwaysNotNullAccordingToAPIContract

namespace Quartz.HttpApiContract;

// When updating this, make same changes also into Quartz.AspNetCore.HttpApi.OpenApi.AddCalendarRequest
internal record AddCalendarRequest(string CalendarName, ICalendar Calendar, bool Replace, bool UpdateTriggers) : IValidatable
{
    public IEnumerable<string> Validate()
    {
        if (string.IsNullOrWhiteSpace(CalendarName))
        {
            yield return "Missing calendar name";
        }

        if (Calendar is null)
        {
            yield return "Missing calendar details missing";
        }
    }
}

internal record AddJobRequest(JobDetailDto Job, bool Replace, bool? StoreNonDurableWhileAwaitingScheduling) : IValidatable
{
    public IEnumerable<string> Validate() => Job is null ? new[] { "Missing job details" } : Job.Validate();
}

internal record ExistsResponse(bool Exists);

internal record GroupPausedResponse(bool Paused);

internal record DeleteCalendarResponse(bool CalendarFound);

internal record DeleteJobResponse(bool JobFound);

internal record DeleteJobsRequest(KeyDto[] Jobs) : IValidatable
{
    public IEnumerable<string> Validate() => Jobs is null ? new[] { "Missing job keys" } : Jobs.SelectMany(x => x.Validate());
}

internal record DeleteJobsResponse(bool AllJobsFound);

internal record InterruptResponse(bool Interrupted);

// When updating this, make same changes also into Quartz.AspNetCore.HttpApi.OpenApi.ScheduleJobRequest
internal record ScheduleJobRequest(ITrigger Trigger, JobDetailDto? Job) : IValidatable
{
    public IEnumerable<string> Validate()
    {
        if (Trigger is null)
        {
            yield return "Missing trigger details";
        }

        if (Job is not null)
        {
            foreach (var errorMessage in Job.Validate())
            {
                yield return errorMessage;
            }
        }
    }
}

internal record ScheduleJobResponse(DateTimeOffset FirstFireTimeUtc);

// When updating these, make same changes also into Quartz.AspNetCore.HttpApi.OpenApi.ScheduleJobsRequest/ScheduleJobsRequestItem
internal record ScheduleJobsRequest(ScheduleJobsRequestItem[] JobsAndTriggers, bool Replace) : IValidatable
{
    public IEnumerable<string> Validate() => JobsAndTriggers is null ? new[] { "Missing jobs and triggers" } : JobsAndTriggers.SelectMany(x => x.Validate());
}

internal record ScheduleJobsRequestItem(JobDetailDto Job, ITrigger[] Triggers) : IValidatable
{
    public IEnumerable<string> Validate()
    {
        if (Job is null)
        {
            yield return "Missing job details";
        }
        else
        {
            foreach (var errorMessage in Job.Validate())
            {
                yield return errorMessage;
            }
        }

        if (Triggers is null)
        {
            yield return "Missing triggers";
        }
    }
}

internal record TriggerJobRequest(JobDataMap JobData);

// When updating these, make same changes also into Quartz.AspNetCore.HttpApi.OpenApi.RescheduleJobRequest
internal record RescheduleJobRequest(ITrigger NewTrigger) : IValidatable
{
    public IEnumerable<string> Validate()
    {
        if (NewTrigger is null)
        {
            yield return "Missing new trigger details";
        }
    }
}

internal record RescheduleJobResponse(DateTimeOffset? FirstFireTimeUtc);

internal record UnscheduleJobResponse(bool TriggerFound);

internal record UnscheduleJobsRequest(KeyDto[] Triggers) : IValidatable
{
    public IEnumerable<string> Validate() => Triggers is null ? new[] { "Missing trigger keys" } : Triggers.SelectMany(x => x.Validate());
}

internal record UnscheduleJobsResponse(bool AllTriggersFound);