using System.Text.Json;

using Quartz.HttpApiContract;
using Quartz.Impl.Matchers;
using Quartz.Spi;

namespace Quartz.HttpClient;

public class HttpScheduler : IScheduler
{
    private readonly System.Net.Http.HttpClient httpClient;
    private readonly JsonSerializerOptions jsonSerializerOptions;

    public HttpScheduler(string schedulerName, System.Net.Http.HttpClient httpClient, JsonSerializerOptions? jsonSerializerOptions = null)
    {
        if (string.IsNullOrWhiteSpace(schedulerName))
        {
            throw new ArgumentException("Scheduler name required", nameof(schedulerName));
        }

        SchedulerName = schedulerName;

        this.httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        if (!this.httpClient.BaseAddress?.ToString().EndsWith('/') == true)
        {
            throw new ArgumentException("HttpClient's BaseAddress must end in /", nameof(httpClient));
        }

        this.jsonSerializerOptions = jsonSerializerOptions ?? new JsonSerializerOptions(JsonSerializerDefaults.Web);
        this.jsonSerializerOptions.AddQuartzConverters(newtonsoftCompatibilityMode: false);
    }

    public string SchedulerName { get; }

    public string SchedulerInstanceId => GetSchedulerDetailsSync().SchedulerInstanceId;
    public bool IsStarted => GetSchedulerDetailsSync().Status == SchedulerStatus.Running;
    public bool InStandbyMode => GetSchedulerDetailsSync().Status == SchedulerStatus.Standby;
    public bool IsShutdown => GetSchedulerDetailsSync().Status == SchedulerStatus.Shutdown;

    public SchedulerContext Context
    {
        get
        {
            var dto = httpClient.Get<SchedulerContextDto>($"{SchedulerEndpointUrl()}/context", jsonSerializerOptions, CancellationToken.None)
                .AsTask().GetAwaiter().GetResult();

            return dto.AsContext();
        }
    }

    public IJobFactory JobFactory
    {
        set => ThrowHelper.ThrowSchedulerException("Operation not supported for remote schedulers.");
    }

    public IListenerManager ListenerManager
    {
        get
        {
            ThrowHelper.ThrowSchedulerException("Operation not supported for remote schedulers.");
            return null;
        }
    }

    public async ValueTask<bool> IsJobGroupPaused(string groupName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(groupName))
        {
            throw new ArgumentException("Group name required", nameof(groupName));
        }

        var result = await httpClient.Get<GroupPausedResponse>($"{JobEndpointUrl()}/groups/{groupName}/paused", jsonSerializerOptions, cancellationToken).ConfigureAwait(false);
        return result.Paused;
    }

    public async ValueTask<bool> IsTriggerGroupPaused(string groupName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(groupName))
        {
            throw new ArgumentException("Group name required", nameof(groupName));
        }

        var result = await httpClient.Get<GroupPausedResponse>($"{TriggerEndpointUrl()}/groups/{groupName}/paused", jsonSerializerOptions, cancellationToken).ConfigureAwait(false);
        return result.Paused;
    }

    public async ValueTask<SchedulerMetaData> GetMetaData(CancellationToken cancellationToken = default)
    {
        var schedulerDto = await GetSchedulerDetails(cancellationToken).ConfigureAwait(false);
        var metadata = new SchedulerMetaData(
            schedName: schedulerDto.Name,
            schedInst: schedulerDto.SchedulerInstanceId,
            schedType: GetType(),
            isRemote: true,
            started: schedulerDto.Status == SchedulerStatus.Running,
            isInStandbyMode: schedulerDto.Status == SchedulerStatus.Standby,
            shutdown: schedulerDto.Status == SchedulerStatus.Shutdown,
            startTime: schedulerDto.Statistics.RunningSince,
            numberOfJobsExec: schedulerDto.Statistics.NumberOfJobsExecuted,
            jsType: Type.GetType(schedulerDto.JobStore.Type, throwOnError: true)!,
            jsPersistent: schedulerDto.JobStore.Persistent,
            jsClustered: schedulerDto.JobStore.Clustered,
            tpType: Type.GetType(schedulerDto.ThreadPool.Type, throwOnError: true)!,
            tpSize: schedulerDto.ThreadPool.Size,
            version: schedulerDto.Statistics.Version
        );

        return metadata;
    }

    public async ValueTask<List<IJobExecutionContext>> GetCurrentlyExecutingJobs(CancellationToken cancellationToken = default)
    {
        var dtos = await httpClient.Get<CurrentlyExecutingJobDto[]>($"{JobEndpointUrl()}/currently-executing", jsonSerializerOptions, cancellationToken).ConfigureAwait(false);

        var result = new List<IJobExecutionContext>(dtos.Length);
        foreach (var dto in dtos)
        {
            var (context, errorReason) = dto.AsIJobExecutionContext(this);
            if (context is null)
            {
                throw new HttpClientException("Could not create IJobExecutionContext from CurrentlyExecutingJobDto: " + errorReason);
            }

            result.Add(context);
        }

        return result;
    }

    public async ValueTask<List<string>> GetJobGroupNames(CancellationToken cancellationToken = default)
    {
        var result = await httpClient.Get<NamesDto>($"{JobEndpointUrl()}/groups", jsonSerializerOptions, cancellationToken).ConfigureAwait(false);
        return result.Names;
    }

    public async ValueTask<List<string>> GetTriggerGroupNames(CancellationToken cancellationToken = default)
    {
        var result = await httpClient.Get<NamesDto>($"{TriggerEndpointUrl()}/groups", jsonSerializerOptions, cancellationToken).ConfigureAwait(false);
        return result.Names;
    }

    public async ValueTask<List<string>> GetPausedTriggerGroups(CancellationToken cancellationToken = default)
    {
        var result = await httpClient.Get<NamesDto>($"{TriggerEndpointUrl()}/groups/paused", jsonSerializerOptions, cancellationToken).ConfigureAwait(false);
        return result.Names;
    }

    public ValueTask Start(CancellationToken cancellationToken = default)
    {
        return httpClient.Post($"{SchedulerEndpointUrl()}/start", jsonSerializerOptions, cancellationToken);
    }

    public ValueTask StartDelayed(TimeSpan delay, CancellationToken cancellationToken = default)
    {
        var delayMilliseconds = (long) Math.Round(delay.TotalMilliseconds);
        return httpClient.Post($"{SchedulerEndpointUrl()}/start?delayMilliseconds={delayMilliseconds}", jsonSerializerOptions, cancellationToken);
    }

    public ValueTask Standby(CancellationToken cancellationToken = default)
    {
        return httpClient.Post($"{SchedulerEndpointUrl()}/standby", jsonSerializerOptions, cancellationToken);
    }

    public ValueTask Shutdown(CancellationToken cancellationToken = default)
    {
        return httpClient.Post($"{SchedulerEndpointUrl()}/shutdown", jsonSerializerOptions, cancellationToken);
    }

    public ValueTask Shutdown(bool waitForJobsToComplete, CancellationToken cancellationToken = default)
    {
        return httpClient.Post($"{SchedulerEndpointUrl()}/shutdown?waitForJobsToComplete={waitForJobsToComplete}", jsonSerializerOptions, cancellationToken);
    }

    public ValueTask<DateTimeOffset> ScheduleJob(IJobDetail jobDetail, ITrigger trigger, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(jobDetail);

        return DoScheduleJob(jobDetail, trigger, cancellationToken);
    }

    public ValueTask<DateTimeOffset> ScheduleJob(ITrigger trigger, CancellationToken cancellationToken = default)
    {
        return DoScheduleJob(null, trigger, cancellationToken);
    }

    private async ValueTask<DateTimeOffset> DoScheduleJob(IJobDetail? jobDetail, ITrigger trigger, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(trigger);

        var jobDetailsDto = jobDetail is not null ? JobDetailDto.Create(jobDetail) : null;
        var result = await httpClient.PostWithResponse<ScheduleJobRequest, ScheduleJobResponse>(
            $"{TriggerEndpointUrl()}/schedule",
            new ScheduleJobRequest(trigger, jobDetailsDto),
            jsonSerializerOptions,
            cancellationToken
        ).ConfigureAwait(false);

        return result.FirstFireTimeUtc;
    }

    public ValueTask ScheduleJobs(IReadOnlyDictionary<IJobDetail, IReadOnlyCollection<ITrigger>> triggersAndJobs, bool replace, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(triggersAndJobs);

        var requestItems = triggersAndJobs.Select(CreateRequestItem).ToArray();
        var request = new ScheduleJobsRequest(requestItems, replace);

        return httpClient.Post($"{TriggerEndpointUrl()}/schedule-multiple", request, jsonSerializerOptions, cancellationToken);

        static ScheduleJobsRequestItem CreateRequestItem(KeyValuePair<IJobDetail, IReadOnlyCollection<ITrigger>> triggersAndJob)
        {
            var (job, triggers) = (triggersAndJob.Key, triggersAndJob.Value);
            return new ScheduleJobsRequestItem(JobDetailDto.Create(job), triggers.ToArray());
        }
    }

    public ValueTask ScheduleJob(IJobDetail jobDetail, IReadOnlyCollection<ITrigger> triggersForJob, bool replace, CancellationToken cancellationToken = default)
    {
        var triggersAndJobs = new Dictionary<IJobDetail, IReadOnlyCollection<ITrigger>>
        {
            { jobDetail, triggersForJob }
        };

        return ScheduleJobs(triggersAndJobs, replace, cancellationToken);
    }

    public async ValueTask<bool> UnscheduleJob(TriggerKey triggerKey, CancellationToken cancellationToken = default)
    {
        var result = await httpClient.PostWithResponse<UnscheduleJobResponse>(
            $"{TriggerEndpointUrl(triggerKey)}/unschedule",
            jsonSerializerOptions,
            cancellationToken
        ).ConfigureAwait(false);

        return result.TriggerFound;
    }

    public async ValueTask<bool> UnscheduleJobs(IReadOnlyCollection<TriggerKey> triggerKeys, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(triggerKeys);

        var result = await httpClient.PostWithResponse<UnscheduleJobsRequest, UnscheduleJobsResponse>(
            $"{TriggerEndpointUrl()}/unschedule",
            new UnscheduleJobsRequest(triggerKeys.Select(KeyDto.Create).ToArray()),
            jsonSerializerOptions,
            cancellationToken
        ).ConfigureAwait(false);

        return result.AllTriggersFound;
    }

    public async ValueTask<DateTimeOffset?> RescheduleJob(TriggerKey triggerKey, ITrigger newTrigger, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(newTrigger);

        var result = await httpClient.PostWithResponse<RescheduleJobRequest, RescheduleJobResponse>(
            $"{TriggerEndpointUrl(triggerKey)}/reschedule",
            new RescheduleJobRequest(newTrigger),
            jsonSerializerOptions,
            cancellationToken
        ).ConfigureAwait(false);

        return result.FirstFireTimeUtc;
    }

    public ValueTask AddJob(IJobDetail jobDetail, bool replace, CancellationToken cancellationToken = default)
    {
        var request = new AddJobRequest(
            Job: JobDetailDto.Create(jobDetail),
            Replace: replace,
            StoreNonDurableWhileAwaitingScheduling: null
        );

        return httpClient.Post(JobEndpointUrl(), request, jsonSerializerOptions, cancellationToken);
    }

    public ValueTask AddJob(IJobDetail jobDetail, bool replace, bool storeNonDurableWhileAwaitingScheduling, CancellationToken cancellationToken = default)
    {
        var request = new AddJobRequest(
            Job: JobDetailDto.Create(jobDetail),
            Replace: replace,
            StoreNonDurableWhileAwaitingScheduling: storeNonDurableWhileAwaitingScheduling
        );

        return httpClient.Post(JobEndpointUrl(), request, jsonSerializerOptions, cancellationToken);
    }

    public async ValueTask<bool> DeleteJob(JobKey jobKey, CancellationToken cancellationToken = default)
    {
        var result = await httpClient.DeleteWithResponse<DeleteJobResponse>($"{JobEndpointUrl(jobKey)}", jsonSerializerOptions, cancellationToken).ConfigureAwait(false);
        return result.JobFound;
    }

    public async ValueTask<bool> DeleteJobs(IReadOnlyCollection<JobKey> jobKeys, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(jobKeys);

        var result = await httpClient.PostWithResponse<DeleteJobsRequest, DeleteJobsResponse>(
            $"{JobEndpointUrl()}/delete",
            new DeleteJobsRequest(jobKeys.Select(KeyDto.Create).ToArray()),
            jsonSerializerOptions,
            cancellationToken
        ).ConfigureAwait(false);

        return result.AllJobsFound;
    }

    public ValueTask TriggerJob(JobKey jobKey, CancellationToken cancellationToken = default)
    {
        return httpClient.Post($"{JobEndpointUrl(jobKey)}/trigger", jsonSerializerOptions, cancellationToken);
    }

    public ValueTask TriggerJob(JobKey jobKey, JobDataMap data, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(data);

        var request = new TriggerJobRequest(data);
        return httpClient.Post($"{JobEndpointUrl(jobKey)}/trigger", request, jsonSerializerOptions, cancellationToken);
    }

    public ValueTask PauseJob(JobKey jobKey, CancellationToken cancellationToken = default)
    {
        return httpClient.Post($"{JobEndpointUrl(jobKey)}/pause", jsonSerializerOptions, cancellationToken);
    }

    public ValueTask PauseJobs(GroupMatcher<JobKey> matcher, CancellationToken cancellationToken = default)
    {
        var urlParams = matcher.ToUrlParameters();
        return httpClient.Post($"{JobEndpointUrl()}/pause?{urlParams}", jsonSerializerOptions, cancellationToken);
    }

    public ValueTask PauseTrigger(TriggerKey triggerKey, CancellationToken cancellationToken = default)
    {
        return httpClient.Post($"{TriggerEndpointUrl(triggerKey)}/pause", jsonSerializerOptions, cancellationToken);
    }

    public ValueTask PauseTriggers(GroupMatcher<TriggerKey> matcher, CancellationToken cancellationToken = default)
    {
        var urlParams = matcher.ToUrlParameters();
        return httpClient.Post($"{TriggerEndpointUrl()}/pause?{urlParams}", jsonSerializerOptions, cancellationToken);
    }

    public ValueTask ResumeJob(JobKey jobKey, CancellationToken cancellationToken = default)
    {
        return httpClient.Post($"{JobEndpointUrl(jobKey)}/resume", jsonSerializerOptions, cancellationToken);
    }

    public ValueTask ResumeJobs(GroupMatcher<JobKey> matcher, CancellationToken cancellationToken = default)
    {
        var urlParams = matcher.ToUrlParameters();
        return httpClient.Post($"{JobEndpointUrl()}/resume?{urlParams}", jsonSerializerOptions, cancellationToken);
    }

    public ValueTask ResumeTrigger(TriggerKey triggerKey, CancellationToken cancellationToken = default)
    {
        return httpClient.Post($"{TriggerEndpointUrl(triggerKey)}/resume", jsonSerializerOptions, cancellationToken);
    }

    public ValueTask ResumeTriggers(GroupMatcher<TriggerKey> matcher, CancellationToken cancellationToken = default)
    {
        var urlParams = matcher.ToUrlParameters();
        return httpClient.Post($"{TriggerEndpointUrl()}/resume?{urlParams}", jsonSerializerOptions, cancellationToken);
    }

    public ValueTask PauseAll(CancellationToken cancellationToken = default)
    {
        return httpClient.Post($"{SchedulerEndpointUrl()}/pause-all", jsonSerializerOptions, cancellationToken);
    }

    public ValueTask ResumeAll(CancellationToken cancellationToken = default)
    {
        return httpClient.Post($"{SchedulerEndpointUrl()}/resume-all", jsonSerializerOptions, cancellationToken);
    }

    public async ValueTask<List<JobKey>> GetJobKeys(GroupMatcher<JobKey> matcher, CancellationToken cancellationToken = default)
    {
        var urlParams = matcher.ToUrlParameters();
        var result = await httpClient.Get<KeyDto[]>($"{JobEndpointUrl()}?{urlParams}", jsonSerializerOptions, cancellationToken).ConfigureAwait(false);
        return result.Select(x => x.AsJobKey()).ToList();
    }

    public async ValueTask<List<ITrigger>> GetTriggersOfJob(JobKey jobKey, CancellationToken cancellationToken = default)
    {
        var result = await httpClient.Get<List<ITrigger>>($"{JobEndpointUrl(jobKey)}/triggers", jsonSerializerOptions, cancellationToken).ConfigureAwait(false);
        return result;
    }

    public async ValueTask<List<TriggerKey>> GetTriggerKeys(GroupMatcher<TriggerKey> matcher, CancellationToken cancellationToken = default)
    {
        var urlParams = matcher.ToUrlParameters();
        var result = await httpClient.Get<KeyDto[]>($"{TriggerEndpointUrl()}?{urlParams}", jsonSerializerOptions, cancellationToken).ConfigureAwait(false);
        return result.Select(x => x.AsTriggerKey()).ToList();
    }

    public async ValueTask<IJobDetail?> GetJobDetail(JobKey jobKey, CancellationToken cancellationToken = default)
    {
        var result = await httpClient.GetWithNullForNotFound<JobDetailDto>($"{JobEndpointUrl(jobKey)}", jsonSerializerOptions, cancellationToken).ConfigureAwait(false);
        if (result is null)
        {
            return null;
        }

        var (jobDetail, errorReason) = result.AsIJobDetail();
        if (jobDetail is null)
        {
            throw new HttpClientException("Could not create IJobDetail from JobDetailDto: " + errorReason);
        }

        return jobDetail;
    }

    public async ValueTask<ITrigger?> GetTrigger(TriggerKey triggerKey, CancellationToken cancellationToken = default)
    {
        var result = await httpClient.GetWithNullForNotFound<ITrigger>(TriggerEndpointUrl(triggerKey), jsonSerializerOptions, cancellationToken).ConfigureAwait(false);
        return result;
    }

    public async ValueTask<TriggerState> GetTriggerState(TriggerKey triggerKey, CancellationToken cancellationToken = default)
    {
        var result = await httpClient.Get<TriggerStateDto>($"{TriggerEndpointUrl(triggerKey)}/state", jsonSerializerOptions, cancellationToken).ConfigureAwait(false);
        return result.State;
    }

    public ValueTask ResetTriggerFromErrorState(TriggerKey triggerKey, CancellationToken cancellationToken = default)
    {
        return httpClient.Post($"{TriggerEndpointUrl(triggerKey)}/reset-from-error-state", jsonSerializerOptions, cancellationToken);
    }

    public ValueTask AddCalendar(string calName, ICalendar calendar, bool replace, bool updateTriggers, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(calName))
        {
            throw new ArgumentException("Calendar name required", nameof(calName));
        }

        ArgumentNullException.ThrowIfNull(calendar);

        var requestContent = new AddCalendarRequest(calName, calendar, replace, updateTriggers);
        return httpClient.Post(CalendarEndpointUrl(), requestContent, jsonSerializerOptions, cancellationToken);
    }

    public async ValueTask<bool> DeleteCalendar(string calName, CancellationToken cancellationToken = default)
    {
        var result = await httpClient.DeleteWithResponse<DeleteCalendarResponse>(CalendarEndpointUrl(calName), jsonSerializerOptions, cancellationToken).ConfigureAwait(false);
        return result.CalendarFound;
    }

    public ValueTask<ICalendar?> GetCalendar(string calName, CancellationToken cancellationToken = default)
    {
        return httpClient.GetWithNullForNotFound<ICalendar>(CalendarEndpointUrl(calName), jsonSerializerOptions, cancellationToken);
    }

    public async ValueTask<List<string>> GetCalendarNames(CancellationToken cancellationToken = default)
    {
        var result = await httpClient.Get<NamesDto>(CalendarEndpointUrl(), jsonSerializerOptions, cancellationToken).ConfigureAwait(false);
        return result.Names;
    }

    public async ValueTask<bool> Interrupt(JobKey jobKey, CancellationToken cancellationToken = default)
    {
        var response = await httpClient.PostWithResponse<InterruptResponse>($"{JobEndpointUrl(jobKey)}/interrupt", jsonSerializerOptions, cancellationToken).ConfigureAwait(false);
        return response.Interrupted;
    }

    public async ValueTask<bool> Interrupt(string fireInstanceId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(fireInstanceId))
        {
            throw new ArgumentException("Fire instance id required", nameof(fireInstanceId));
        }

        var response = await httpClient.PostWithResponse<InterruptResponse>(
            $"{JobEndpointUrl()}/interrupt/{fireInstanceId}",
            jsonSerializerOptions,
            cancellationToken
        ).ConfigureAwait(false);

        return response.Interrupted;
    }

    public async ValueTask<bool> CheckExists(JobKey jobKey, CancellationToken cancellationToken = default)
    {
        var result = await httpClient.Get<ExistsResponse>($"{JobEndpointUrl(jobKey)}/exists", jsonSerializerOptions, cancellationToken).ConfigureAwait(false);
        return result.Exists;
    }

    public async ValueTask<bool> CheckExists(TriggerKey triggerKey, CancellationToken cancellationToken = default)
    {
        var result = await httpClient.Get<ExistsResponse>($"{TriggerEndpointUrl(triggerKey)}/exists", jsonSerializerOptions, cancellationToken).ConfigureAwait(false);
        return result.Exists;
    }

    public ValueTask Clear(CancellationToken cancellationToken = default)
    {
        return httpClient.Post($"{SchedulerEndpointUrl()}/clear", jsonSerializerOptions, cancellationToken);
    }

    private string SchedulerEndpointUrl() => $"schedulers/{SchedulerName}";

    private string CalendarEndpointUrl() => $"schedulers/{SchedulerName}/calendars";

    private string CalendarEndpointUrl(string calendarName)
    {
        if (string.IsNullOrWhiteSpace(calendarName))
        {
            throw new ArgumentException("Calendar name required", nameof(calendarName));
        }

        return $"schedulers/{SchedulerName}/calendars/{calendarName}";
    }

    private string JobEndpointUrl() => $"schedulers/{SchedulerName}/jobs";

    private string JobEndpointUrl(JobKey job)
    {
        if (job is null)
        {
            throw new ArgumentNullException(nameof(job), "JobKey required");
        }

        return $"schedulers/{SchedulerName}/jobs/{job.Group}/{job.Name}";
    }

    private string TriggerEndpointUrl() => $"schedulers/{SchedulerName}/triggers";

    private string TriggerEndpointUrl(TriggerKey trigger)
    {
        if (trigger is null)
        {
            throw new ArgumentNullException(nameof(trigger), "TriggerKey required");
        }

        return $"schedulers/{SchedulerName}/triggers/{trigger.Group}/{trigger.Name}";
    }

    private SchedulerDto GetSchedulerDetailsSync()
    {
#pragma warning disable CA2012
        var schedulerDto = GetSchedulerDetails(CancellationToken.None).ConfigureAwait(false).GetAwaiter().GetResult();
#pragma warning restore CA2012
        return schedulerDto;
    }

    private ValueTask<SchedulerDto> GetSchedulerDetails(CancellationToken cancellationToken)
    {
        return httpClient.Get<SchedulerDto>(SchedulerEndpointUrl(), jsonSerializerOptions, cancellationToken);
    }
}