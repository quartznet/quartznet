namespace Quartz.HttpApiContract;

// When updating this, make same changes also into Quartz.AspNetCore.HttpApi.OpenApi.AddCalendarRequest
internal record AddCalendarRequest(string CalendarName, ICalendar Calendar, bool Replace, bool UpdateTriggers);

internal record AddJobRequest(JobDetailDto Job, bool Replace, bool? StoreNonDurableWhileAwaitingScheduling);

internal record ExistsResponse(bool Exists);

internal record GroupPausedResponse(bool Paused);

internal record DeleteCalendarResponse(bool CalendarFound);

internal record DeleteJobResponse(bool JobFound);

internal record DeleteJobsRequest(KeyDto[] Jobs);
internal record DeleteJobsResponse(bool AllJobsFound);

internal record InterruptResponse(bool Interrupted);

// When updating this, make same changes also into Quartz.AspNetCore.HttpApi.OpenApi.ScheduleJobRequest
internal record ScheduleJobRequest(ITrigger Trigger, JobDetailDto? Job);
internal record ScheduleJobResponse(DateTimeOffset FirstFireTimeUtc);

// When updating these, make same changes also into Quartz.AspNetCore.HttpApi.OpenApi.ScheduleJobsRequest/ScheduleJobsRequestItem
internal record ScheduleJobsRequest(ScheduleJobsRequestItem[] JobsAndTriggers, bool Replace);
internal record ScheduleJobsRequestItem(JobDetailDto Job, ITrigger[] Triggers);

internal record TriggerJobRequest(JobDataMap JobData);

// When updating these, make same changes also into Quartz.AspNetCore.HttpApi.OpenApi.RescheduleJobRequest
internal record RescheduleJobRequest(ITrigger NewTrigger);
internal record RescheduleJobResponse(DateTimeOffset? FirstFireTimeUtc);

internal record UnscheduleJobResponse(bool TriggerFound);

internal record UnscheduleJobsRequest(KeyDto[] Triggers);
internal record UnscheduleJobsResponse(bool AllTriggersFound);