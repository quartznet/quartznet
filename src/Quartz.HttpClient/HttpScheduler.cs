#region License

/*
 * All content copyright Marko Lahma, unless otherwise indicated. All rights reserved.
 *
 * Licensed under the Apache License, Version 2.0 (the "License"); you may not
 * use this file except in compliance with the License. You may obtain a copy
 * of the License at
 *
 *   http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS, WITHOUT
 * WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the
 * License for the specific language governing permissions and limitations
 * under the License.
 *
 */

#endregion

using System.Text.Json;

using Quartz.HttpApiContract;
using Quartz.Serialization.SystemTextJson;
using Quartz.Extensibility;
using Quartz.Util;

namespace Quartz;

public sealed class HttpScheduler : IScheduler
{
    private readonly HttpClient httpClient;
    private readonly JsonSerializerOptions jsonSerializerOptions;

    /// <param name="schedulerName">Name of the scheduler, must be same as the remote scheduler.</param>
    /// <param name="httpClient">The client to call the remote scheduler with.</param>
    /// <param name="jsonSerializerOptions">
    /// Optional serializer options. A copy is taken and Quartz's own converters are added to the copy,
    /// so the instance passed in is left untouched.
    /// </param>
    /// <param name="serializerRegistry">
    /// The trigger and calendar serializers to understand. Custom types are only readable over HTTP when
    /// their serializers are given here — the remote scheduler's own registrations are not visible in this
    /// process. Defaults to the built-in types.
    /// </param>
    public HttpScheduler(
        string schedulerName,
        HttpClient httpClient,
        JsonSerializerOptions? jsonSerializerOptions = null,
        SystemTextJsonSerializerRegistry? serializerRegistry = null)
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

        // The caller's options are borrowed, not owned: adding our converters to their instance would
        // throw once those options had been used for anything (they are read-only from then on), and
        // would add the converters a second time when two clients share one instance.
        this.jsonSerializerOptions = jsonSerializerOptions is null
            ? new JsonSerializerOptions(JsonSerializerDefaults.Web)
            : new JsonSerializerOptions(jsonSerializerOptions);

        this.jsonSerializerOptions.ConfigureWireFormat(serializerRegistry ?? new SystemTextJsonSerializerRegistry());
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

    public IListenerManager ListenerManager
    {
        get
        {
            Throw.SchedulerException("Operation not supported for remote schedulers.");
            return null;
        }
    }

    public async ValueTask<SchedulerMetadata> GetMetadata(CancellationToken cancellationToken = default)
    {
        var schedulerDto = await GetSchedulerDetails(cancellationToken).ConfigureAwait(false);
        return new SchedulerMetadata
        {
            SchedulerName = schedulerDto.Name,
            SchedulerInstanceId = schedulerDto.SchedulerInstanceId,
            SchedulerTypeName = GetType().AssemblyQualifiedNameWithoutVersion(),
            IsProxy = true,
            Started = schedulerDto.Status == SchedulerStatus.Running,
            InStandbyMode = schedulerDto.Status == SchedulerStatus.Standby,
            Shutdown = schedulerDto.Status == SchedulerStatus.Shutdown,
            RunningSince = schedulerDto.Statistics.RunningSince,
            JobsExecuted = schedulerDto.Statistics.JobsExecuted,
            // the remote node's own count, which is what the member means everywhere
            LocalExecutingJobs = schedulerDto.Statistics.LocalExecutingJobs,
            // names pass through as strings: the remote types need not exist in this process
            JobStoreTypeName = schedulerDto.JobStore.Type,
            JobStorePersistent = schedulerDto.JobStore.Persistent,
            JobStoreClustered = schedulerDto.JobStore.Clustered,
            ThreadPoolTypeName = schedulerDto.ThreadPool.Type,
            ThreadPoolSize = schedulerDto.ThreadPool.Size,
            Version = schedulerDto.Statistics.Version,
        };
    }

    public async ValueTask<PagedResult<FireInstance>> QueryFireInstances(FireInstanceQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        QueryStringBuilder parameters = new();
        parameters.AddPaging(query);
        parameters.AddGroupMatcher(query.TriggerGroup);
        parameters.AddNameMatcher(query.TriggerName);

        if (query.Job is not null)
        {
            parameters.Add("jobName", query.Job.Name);
            parameters.Add("jobGroup", query.Job.Group);
        }

        if (query.SchedulerInstanceId is not null)
        {
            parameters.Add("schedulerInstanceId", query.SchedulerInstanceId);
        }

        // Always sent, because the query's own default is Executing rather than "everything": omitting
        // the parameter would have to mean "every state", and then the default could not travel.
        parameters.Add("state", query.State?.ToString() ?? HttpApiConstants.AnyFireInstanceState);

        PagedResultDto<FireInstanceDto> result = await httpClient
            .Get<PagedResultDto<FireInstanceDto>>($"{JobEndpointUrl()}/fire-instances{parameters}", jsonSerializerOptions, cancellationToken)
            .ConfigureAwait(false);

        return new PagedResult<FireInstance>(result.Items.Select(x => x.AsFireInstance()).ToList(), result.HasMore, result.TotalCount);
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

    public ValueTask Shutdown(bool waitForJobsToComplete = false, CancellationToken cancellationToken = default)
    {
        return httpClient.Post($"{SchedulerEndpointUrl()}/shutdown?waitForJobsToComplete={waitForJobsToComplete}", jsonSerializerOptions, cancellationToken);
    }

    /// <summary>
    /// Releases what this client owns, which is nothing — and in particular does <b>not</b> shut the
    /// remote scheduler down.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Disposing an <see cref="IScheduler" /> releases what that instance owns. A local scheduler owns
    /// the execution it drives, so disposing it stops it. This one owns only a connection to a scheduler
    /// running somewhere else, which other clients are using and which outlives this process: a client
    /// going away is not an instruction to stop scheduling for everybody. Call
    /// <see cref="Shutdown(bool, CancellationToken)" /> to stop the remote scheduler, deliberately.
    /// </para>
    /// <para>
    /// The <see cref="System.Net.Http.HttpClient" /> is not disposed either — it belongs to whoever made
    /// it, an <see cref="System.Net.Http.IHttpClientFactory" /> or the caller, and disposing something
    /// handed in is how a client shared with the rest of an application stops working.
    /// </para>
    /// </remarks>
    public ValueTask DisposeAsync()
    {
        return default;
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

    public ValueTask ScheduleJobs(IReadOnlyDictionary<IJobDetail, IReadOnlyCollection<ITrigger>> triggersAndJobs, ScheduleJobOptions options = default, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(triggersAndJobs);

        var requestItems = triggersAndJobs.Select(CreateRequestItem).ToArray();
        var request = new ScheduleJobsRequest(requestItems, options.Replace);

        return httpClient.Post($"{TriggerEndpointUrl()}/schedule-multiple", request, jsonSerializerOptions, cancellationToken);

        static ScheduleJobsRequestItem CreateRequestItem(KeyValuePair<IJobDetail, IReadOnlyCollection<ITrigger>> triggersAndJob)
        {
            var (job, triggers) = (triggersAndJob.Key, triggersAndJob.Value);
            return new ScheduleJobsRequestItem(JobDetailDto.Create(job), triggers.ToArray());
        }
    }

    public ValueTask ScheduleJob(IJobDetail jobDetail, IReadOnlyCollection<ITrigger> triggersForJob, ScheduleJobOptions options = default, CancellationToken cancellationToken = default)
    {
        var triggersAndJobs = new Dictionary<IJobDetail, IReadOnlyCollection<ITrigger>>
        {
            { jobDetail, triggersForJob }
        };

        return ScheduleJobs(triggersAndJobs, options, cancellationToken);
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

    public ValueTask<bool> UpdateTriggerDetails(TriggerKey triggerKey, TriggerDetailsUpdate update, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("UpdateTriggerDetails is not yet supported via the HTTP API.");
    }

    public async ValueTask SetExecutionLimits(ExecutionLimits? limits, CancellationToken cancellationToken = default)
    {
        if (limits is null)
        {
            using HttpResponseMessage response = await httpClient.DeleteAsync($"{SchedulerEndpointUrl()}/execution-limits", cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
        }
        else
        {
            Dictionary<string, int?> dict = new();
            foreach (ExecutionGroupLimit limit in limits.Groups)
            {
                dict[limit.Scope.ToConfigurationKey()] = limit.MaxConcurrent;
            }
            await httpClient.Post(
                $"{SchedulerEndpointUrl()}/execution-limits",
                new SetExecutionLimitsRequest(dict, limits.UsesTriggerGroupWhenUnset),
                jsonSerializerOptions,
                cancellationToken).ConfigureAwait(false);
        }
    }

    public async ValueTask<ExecutionLimits?> GetExecutionLimits(CancellationToken cancellationToken = default)
    {
        ExecutionLimitsResponse response = await httpClient.Get<ExecutionLimitsResponse>($"{SchedulerEndpointUrl()}/execution-limits", jsonSerializerOptions, cancellationToken).ConfigureAwait(false);
        if (response.Limits is null || response.Limits.Count == 0)
        {
            return null;
        }

        ExecutionLimitsBuilder builder = ExecutionLimitsBuilder.Create();
        foreach (KeyValuePair<string, int?> kvp in response.Limits)
        {
            if (kvp.Key == ExecutionLimits.OtherGroups)
            {
                if (kvp.Value.HasValue) builder.ForOtherGroups(kvp.Value.Value);
            }
            else if (ExecutionLimits.IsDefaultGroupAlias(kvp.Key))
            {
                if (kvp.Value.HasValue) builder.ForDefaultGroup(kvp.Value.Value);
                // null value = unlimited, nothing to set
            }
            else
            {
                if (kvp.Value.HasValue) builder.ForGroup(kvp.Key, kvp.Value.Value);
                else builder.Unlimited(kvp.Key);
            }
        }

        if (response.UseTriggerGroupWhenUnset)
        {
            builder.UseTriggerGroupWhenUnset();
        }

        return builder.Build();
    }

    public ValueTask AddJob(IJobDetail jobDetail, AddJobOptions options = default, CancellationToken cancellationToken = default)
    {
        var request = new AddJobRequest(
            Job: JobDetailDto.Create(jobDetail),
            Replace: options.Replace,
            StoreNonDurableWhileAwaitingScheduling: options.StoreNonDurableWhileAwaitingScheduling
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

    public ValueTask TriggerJob(JobKey jobKey, JobDataMap? data = null, CancellationToken cancellationToken = default)
    {
        if (data is null)
        {
            return httpClient.Post($"{JobEndpointUrl(jobKey)}/trigger", jsonSerializerOptions, cancellationToken);
        }

        var request = new TriggerJobRequest(data);
        return httpClient.Post($"{JobEndpointUrl(jobKey)}/trigger", request, jsonSerializerOptions, cancellationToken);
    }

    public async ValueTask<bool> PauseJob(JobKey jobKey, CancellationToken cancellationToken = default)
    {
        var result = await httpClient.PostWithResponse<OperationAppliedResponse>($"{JobEndpointUrl(jobKey)}/pause", jsonSerializerOptions, cancellationToken).ConfigureAwait(false);
        return result.Applied;
    }

    public async ValueTask<List<string>> PauseJobs(GroupMatcher<JobKey> matcher, CancellationToken cancellationToken = default)
    {
        var urlParams = matcher.ToUrlParameters();
        var result = await httpClient.PostWithResponse<AffectedGroupsResponse>($"{JobEndpointUrl()}/pause?{urlParams}", jsonSerializerOptions, cancellationToken).ConfigureAwait(false);
        return [.. result.Groups];
    }

    public async ValueTask<List<JobKey>> PauseJobs(IReadOnlyCollection<JobKey> jobKeys, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(jobKeys);

        var request = new JobKeySetRequest([.. jobKeys.Select(KeyDto.Create)]);
        var result = await httpClient.PostWithResponse<JobKeySetRequest, AppliedJobKeysResponse>($"{JobEndpointUrl()}/keys/pause", request, jsonSerializerOptions, cancellationToken).ConfigureAwait(false);
        return [.. result.Jobs.Select(x => x.AsJobKey())];
    }

    public async ValueTask<bool> PauseTrigger(TriggerKey triggerKey, CancellationToken cancellationToken = default)
    {
        var result = await httpClient.PostWithResponse<OperationAppliedResponse>($"{TriggerEndpointUrl(triggerKey)}/pause", jsonSerializerOptions, cancellationToken).ConfigureAwait(false);
        return result.Applied;
    }

    public async ValueTask<List<string>> PauseTriggers(GroupMatcher<TriggerKey> matcher, CancellationToken cancellationToken = default)
    {
        var urlParams = matcher.ToUrlParameters();
        var result = await httpClient.PostWithResponse<AffectedGroupsResponse>($"{TriggerEndpointUrl()}/pause?{urlParams}", jsonSerializerOptions, cancellationToken).ConfigureAwait(false);
        return [.. result.Groups];
    }

    public async ValueTask<List<TriggerKey>> PauseTriggers(IReadOnlyCollection<TriggerKey> triggerKeys, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(triggerKeys);

        var request = new TriggerKeySetRequest([.. triggerKeys.Select(KeyDto.Create)]);
        var result = await httpClient.PostWithResponse<TriggerKeySetRequest, AppliedTriggerKeysResponse>($"{TriggerEndpointUrl()}/keys/pause", request, jsonSerializerOptions, cancellationToken).ConfigureAwait(false);
        return [.. result.Triggers.Select(x => x.AsTriggerKey())];
    }

    public async ValueTask<bool> ResumeJob(JobKey jobKey, CancellationToken cancellationToken = default)
    {
        var result = await httpClient.PostWithResponse<OperationAppliedResponse>($"{JobEndpointUrl(jobKey)}/resume", jsonSerializerOptions, cancellationToken).ConfigureAwait(false);
        return result.Applied;
    }

    public async ValueTask<List<string>> ResumeJobs(GroupMatcher<JobKey> matcher, CancellationToken cancellationToken = default)
    {
        var urlParams = matcher.ToUrlParameters();
        var result = await httpClient.PostWithResponse<AffectedGroupsResponse>($"{JobEndpointUrl()}/resume?{urlParams}", jsonSerializerOptions, cancellationToken).ConfigureAwait(false);
        return [.. result.Groups];
    }

    public async ValueTask<List<JobKey>> ResumeJobs(IReadOnlyCollection<JobKey> jobKeys, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(jobKeys);

        var request = new JobKeySetRequest([.. jobKeys.Select(KeyDto.Create)]);
        var result = await httpClient.PostWithResponse<JobKeySetRequest, AppliedJobKeysResponse>($"{JobEndpointUrl()}/keys/resume", request, jsonSerializerOptions, cancellationToken).ConfigureAwait(false);
        return [.. result.Jobs.Select(x => x.AsJobKey())];
    }

    public async ValueTask<bool> ResumeTrigger(TriggerKey triggerKey, CancellationToken cancellationToken = default)
    {
        var result = await httpClient.PostWithResponse<OperationAppliedResponse>($"{TriggerEndpointUrl(triggerKey)}/resume", jsonSerializerOptions, cancellationToken).ConfigureAwait(false);
        return result.Applied;
    }

    public async ValueTask<List<string>> ResumeTriggers(GroupMatcher<TriggerKey> matcher, CancellationToken cancellationToken = default)
    {
        var urlParams = matcher.ToUrlParameters();
        var result = await httpClient.PostWithResponse<AffectedGroupsResponse>($"{TriggerEndpointUrl()}/resume?{urlParams}", jsonSerializerOptions, cancellationToken).ConfigureAwait(false);
        return [.. result.Groups];
    }

    public async ValueTask<List<TriggerKey>> ResumeTriggers(IReadOnlyCollection<TriggerKey> triggerKeys, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(triggerKeys);

        var request = new TriggerKeySetRequest([.. triggerKeys.Select(KeyDto.Create)]);
        var result = await httpClient.PostWithResponse<TriggerKeySetRequest, AppliedTriggerKeysResponse>($"{TriggerEndpointUrl()}/keys/resume", request, jsonSerializerOptions, cancellationToken).ConfigureAwait(false);
        return [.. result.Triggers.Select(x => x.AsTriggerKey())];
    }

    public ValueTask PauseAll(CancellationToken cancellationToken = default)
    {
        return httpClient.Post($"{SchedulerEndpointUrl()}/pause-all", jsonSerializerOptions, cancellationToken);
    }

    public ValueTask ResumeAll(CancellationToken cancellationToken = default)
    {
        return httpClient.Post($"{SchedulerEndpointUrl()}/resume-all", jsonSerializerOptions, cancellationToken);
    }

    public async ValueTask<PagedResult<JobHeader>> QueryJobs(JobQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        QueryStringBuilder parameters = new();
        parameters.AddPaging(query);
        parameters.AddGroupMatcher(query.Group);
        parameters.AddNameMatcher(query.Name);

        PagedResultDto<JobHeaderDto> result = await httpClient
            .Get<PagedResultDto<JobHeaderDto>>($"{JobEndpointUrl()}{parameters}", jsonSerializerOptions, cancellationToken)
            .ConfigureAwait(false);

        return new PagedResult<JobHeader>(result.Items.Select(x => x.AsJobHeader()).ToList(), result.HasMore, result.TotalCount);
    }

    public async ValueTask<PagedResult<TriggerHeader>> QueryTriggers(TriggerQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        QueryStringBuilder parameters = new();
        parameters.AddPaging(query);
        parameters.AddGroupMatcher(query.Group);
        parameters.AddNameMatcher(query.Name);

        if (query.Job is not null)
        {
            parameters.Add("jobName", query.Job.Name);
            parameters.Add("jobGroup", query.Job.Group);
        }

        if (query.CalendarName is not null)
        {
            parameters.Add("calendarName", query.CalendarName);
        }

        if (query.State is not null)
        {
            parameters.Add("state", query.State.Value.ToString());
        }

        PagedResultDto<TriggerHeaderDto> result = await httpClient
            .Get<PagedResultDto<TriggerHeaderDto>>($"{TriggerEndpointUrl()}{parameters}", jsonSerializerOptions, cancellationToken)
            .ConfigureAwait(false);

        return new PagedResult<TriggerHeader>(result.Items.Select(x => x.AsTriggerHeader()).ToList(), result.HasMore, result.TotalCount);
    }

    public async ValueTask<PagedResult<JobGroup>> QueryJobGroups(JobGroupQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        QueryStringBuilder parameters = new();
        parameters.AddPaging(query);
        if (query.Name is not null)
        {
            parameters.Add("name", query.Name);
        }

        if (query.Paused is not null)
        {
            parameters.Add("paused", query.Paused.Value);
        }

        PagedResultDto<JobGroupDto> result = await httpClient
            .Get<PagedResultDto<JobGroupDto>>($"{JobEndpointUrl()}/groups{parameters}", jsonSerializerOptions, cancellationToken)
            .ConfigureAwait(false);

        return new PagedResult<JobGroup>(result.Items.Select(x => x.AsJobGroup()).ToList(), result.HasMore, result.TotalCount);
    }

    public async ValueTask<PagedResult<TriggerGroup>> QueryTriggerGroups(TriggerGroupQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        QueryStringBuilder parameters = new();
        parameters.AddPaging(query);
        if (query.Name is not null)
        {
            parameters.Add("name", query.Name);
        }

        if (query.Paused is not null)
        {
            parameters.Add("paused", query.Paused.Value);
        }

        PagedResultDto<TriggerGroupDto> result = await httpClient
            .Get<PagedResultDto<TriggerGroupDto>>($"{TriggerEndpointUrl()}/groups{parameters}", jsonSerializerOptions, cancellationToken)
            .ConfigureAwait(false);

        return new PagedResult<TriggerGroup>(result.Items.Select(x => x.AsTriggerGroup()).ToList(), result.HasMore, result.TotalCount);
    }

    public async ValueTask<PagedResult<string>> QueryCalendarNames(CalendarQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        QueryStringBuilder parameters = new();
        parameters.AddPaging(query);
        parameters.AddNameMatcher(query.Name);

        PagedResultDto<string> result = await httpClient
            .Get<PagedResultDto<string>>($"{CalendarEndpointUrl()}{parameters}", jsonSerializerOptions, cancellationToken)
            .ConfigureAwait(false);

        return new PagedResult<string>([..result.Items], result.HasMore, result.TotalCount);
    }

    public async ValueTask<List<IJobDetail>> GetJobDetails(IReadOnlyCollection<JobKey> jobKeys, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(jobKeys);

        if (jobKeys.Count == 0)
        {
            return [];
        }

        JobDetailDto[] dtos = await httpClient.PostWithResponse<KeyDto[], JobDetailDto[]>(
            $"{JobEndpointUrl()}/fetch",
            jobKeys.Select(KeyDto.Create).ToArray(),
            jsonSerializerOptions,
            cancellationToken
        ).ConfigureAwait(false);

        List<IJobDetail> result = new(dtos.Length);
        foreach (JobDetailDto dto in dtos)
        {
            var (jobDetail, errorReason) = dto.AsIJobDetail();
            if (jobDetail is null)
            {
                throw new HttpClientException("Could not create IJobDetail from JobDetailDto: " + errorReason);
            }

            result.Add(jobDetail);
        }

        return result;
    }

    public async ValueTask<List<ITrigger>> GetTriggers(IReadOnlyCollection<TriggerKey> triggerKeys, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(triggerKeys);

        if (triggerKeys.Count == 0)
        {
            return [];
        }

        return await httpClient.PostWithResponse<KeyDto[], List<ITrigger>>(
            $"{TriggerEndpointUrl()}/fetch",
            triggerKeys.Select(KeyDto.Create).ToArray(),
            jsonSerializerOptions,
            cancellationToken
        ).ConfigureAwait(false);
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

    public async ValueTask<bool> ResetTriggerFromErrorState(TriggerKey triggerKey, CancellationToken cancellationToken = default)
    {
        var result = await httpClient.PostWithResponse<OperationAppliedResponse>($"{TriggerEndpointUrl(triggerKey)}/reset-from-error-state", jsonSerializerOptions, cancellationToken).ConfigureAwait(false);
        return result.Applied;
    }

    public async ValueTask<List<TriggerKey>> ResetTriggersFromErrorState(IReadOnlyCollection<TriggerKey> triggerKeys, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(triggerKeys);

        var request = new TriggerKeySetRequest([.. triggerKeys.Select(KeyDto.Create)]);
        var result = await httpClient.PostWithResponse<TriggerKeySetRequest, AppliedTriggerKeysResponse>($"{TriggerEndpointUrl()}/keys/reset-from-error-state", request, jsonSerializerOptions, cancellationToken).ConfigureAwait(false);
        return [.. result.Triggers.Select(x => x.AsTriggerKey())];
    }

    public ValueTask AddCalendar(string calendarName, ICalendar calendar, AddCalendarOptions options = default, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(calendarName))
        {
            throw new ArgumentException("Calendar name required", nameof(calendarName));
        }

        ArgumentNullException.ThrowIfNull(calendar);

        var requestContent = new AddCalendarRequest(calendarName, calendar, options.Replace, options.UpdateTriggers);
        return httpClient.Post(CalendarEndpointUrl(), requestContent, jsonSerializerOptions, cancellationToken);
    }

    public async ValueTask<bool> DeleteCalendar(string calendarName, CancellationToken cancellationToken = default)
    {
        var result = await httpClient.DeleteWithResponse<DeleteCalendarResponse>(CalendarEndpointUrl(calendarName), jsonSerializerOptions, cancellationToken).ConfigureAwait(false);
        return result.CalendarFound;
    }

    public ValueTask<ICalendar?> GetCalendar(string calendarName, CancellationToken cancellationToken = default)
    {
        return httpClient.GetWithNullForNotFound<ICalendar>(CalendarEndpointUrl(calendarName), jsonSerializerOptions, cancellationToken);
    }

    public async ValueTask<bool> Interrupt(JobKey jobKey, CancellationToken cancellationToken = default)
    {
        var response = await httpClient.PostWithResponse<InterruptResponse>($"{JobEndpointUrl(jobKey)}/interrupt", jsonSerializerOptions, cancellationToken).ConfigureAwait(false);
        return response.Interrupted;
    }

    public async ValueTask<bool> InterruptFireInstance(string fireInstanceId, CancellationToken cancellationToken = default)
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

    public async ValueTask<bool> Exists(JobKey jobKey, CancellationToken cancellationToken = default)
    {
        var result = await httpClient.Get<ExistsResponse>($"{JobEndpointUrl(jobKey)}/exists", jsonSerializerOptions, cancellationToken).ConfigureAwait(false);
        return result.Exists;
    }

    public async ValueTask<bool> Exists(TriggerKey triggerKey, CancellationToken cancellationToken = default)
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