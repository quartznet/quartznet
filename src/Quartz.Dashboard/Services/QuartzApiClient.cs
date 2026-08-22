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

using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

using Quartz.HttpApiContract;

namespace Quartz.Dashboard.Services;

internal sealed class QuartzApiClient : IQuartzApiClient
{
    private static readonly JsonSerializerOptions historySerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly IHttpClientFactory httpClientFactory;
    private readonly IHttpContextAccessor httpContextAccessor;
    private readonly IOptions<QuartzDashboardOptions> options;
    private readonly JsonSerializerOptions quartzSerializerOptions;
    private Uri? cachedBaseAddress;
    private string? cachedCookieHeader;

    /// <remarks>
    /// <paramref name="serializerOptions"/> carries the Quartz converters, which is what turns the
    /// wire's discriminated trigger and calendar payloads back into <see cref="ITrigger"/> and
    /// <see cref="ICalendar"/>. A kind no serializer is registered for cannot be read, and says so
    /// rather than rendering as an anonymous bag of properties.
    /// </remarks>
    public QuartzApiClient(
        IHttpClientFactory httpClientFactory,
        IHttpContextAccessor httpContextAccessor,
        IOptions<QuartzDashboardOptions> options,
        DashboardSerializerOptions serializerOptions)
    {
        ArgumentNullException.ThrowIfNull(serializerOptions);

        this.httpClientFactory = httpClientFactory;
        this.httpContextAccessor = httpContextAccessor;
        this.options = options;
        quartzSerializerOptions = serializerOptions.Deserializer;
    }

    public async ValueTask<List<SchedulerHeaderDto>> GetSchedulers(CancellationToken cancellationToken = default)
    {
        JsonElement json = await GetJson($"{ApiPath}/schedulers", cancellationToken).ConfigureAwait(false);
        if (json.ValueKind is not JsonValueKind.Array)
        {
            return [];
        }

        List<SchedulerHeaderDto> result = [];
        foreach (JsonElement scheduler in json.EnumerateArray())
        {
            string schedulerName = GetStringProperty(scheduler, "name");
            string schedulerInstanceId = GetStringProperty(scheduler, "schedulerInstanceId");
            SchedulerStatus status = GetSchedulerStatusProperty(scheduler, "status");
            result.Add(new SchedulerHeaderDto(schedulerName, schedulerInstanceId, status));
        }

        return result;
    }

    public async ValueTask<SchedulerDetailDto> GetScheduler(string schedulerName, CancellationToken cancellationToken = default)
    {
        JsonElement json = await GetJson($"{GetSchedulerPath(schedulerName)}", cancellationToken).ConfigureAwait(false);
        string resolvedName = GetStringProperty(json, "name");
        string schedulerInstanceId = GetStringProperty(json, "schedulerInstanceId");
        SchedulerStatus status = GetSchedulerStatusProperty(json, "status");
        return new SchedulerDetailDto(schedulerInstanceId, resolvedName, status);
    }

    public ValueTask StartScheduler(string schedulerName, CancellationToken cancellationToken = default)
    {
        return Post($"{GetSchedulerPath(schedulerName)}/start", body: null, cancellationToken);
    }

    public ValueTask StandbyScheduler(string schedulerName, CancellationToken cancellationToken = default)
    {
        return Post($"{GetSchedulerPath(schedulerName)}/standby", body: null, cancellationToken);
    }

    public ValueTask ShutdownScheduler(string schedulerName, CancellationToken cancellationToken = default)
    {
        return Post($"{GetSchedulerPath(schedulerName)}/shutdown", body: null, cancellationToken);
    }

    public ValueTask PauseAll(string schedulerName, CancellationToken cancellationToken = default)
    {
        return Post($"{GetSchedulerPath(schedulerName)}/pause-all", body: null, cancellationToken);
    }

    public ValueTask ResumeAll(string schedulerName, CancellationToken cancellationToken = default)
    {
        return Post($"{GetSchedulerPath(schedulerName)}/resume-all", body: null, cancellationToken);
    }

    public async ValueTask<PagedResult<JobKeyDto>> GetJobs(string schedulerName, DashboardJobQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        string path = BuildPagedPath($"{GetSchedulerPath(schedulerName)}/jobs", query.GroupContains, query);
        JsonElement json = await GetJson(path, cancellationToken).ConfigureAwait(false);
        JsonElement items = GetOptionalProperty(json, "items");

        List<JobKeyDto> result = [];
        if (items.ValueKind is JsonValueKind.Array)
        {
            foreach (JsonElement job in items.EnumerateArray())
            {
                result.Add(new JobKeyDto(GetStringProperty(job, "group"), GetStringProperty(job, "name")));
            }
        }

        int totalCount = GetNullableIntProperty(json, "totalCount") ?? result.Count;
        return new PagedResult<JobKeyDto>(result, GetBooleanProperty(json, "hasMore"), totalCount);
    }

    public async ValueTask<List<JobGroupDto>> GetJobGroups(string schedulerName, CancellationToken cancellationToken = default)
    {
        List<JobGroupDto> result = [];
        int skip = 0;
        while (true)
        {
            JsonElement json = await GetJson($"{GetSchedulerPath(schedulerName)}/jobs/groups?skip={skip.ToString(CultureInfo.InvariantCulture)}", cancellationToken).ConfigureAwait(false);
            JsonElement items = GetOptionalProperty(json, "items");
            if (items.ValueKind is not JsonValueKind.Array)
            {
                return result;
            }

            int itemCount = 0;
            foreach (JsonElement group in items.EnumerateArray())
            {
                result.Add(new JobGroupDto(GetStringProperty(group, "name"), GetBooleanProperty(group, "paused")));
                itemCount++;
            }

            if (!GetBooleanProperty(json, "hasMore") || itemCount == 0)
            {
                return result;
            }

            skip += itemCount;
        }
    }

    public async ValueTask<JobDetailDto> GetJob(string schedulerName, JobKeyDto key, CancellationToken cancellationToken = default)
    {
        JsonElement json = await GetJson(JobPath(schedulerName, key), cancellationToken).ConfigureAwait(false);

        return new JobDetailDto(
            Name: GetStringProperty(json, "name"),
            Group: GetStringProperty(json, "group"),
            JobType: GetStringProperty(json, "jobType"),
            Description: GetNullableStringProperty(json, "description"),
            Durable: GetBooleanProperty(json, "durable"),
            RequestsRecovery: GetBooleanProperty(json, "requestsRecovery"),
            ConcurrentExecutionDisallowed: GetBooleanProperty(json, "concurrentExecutionDisallowed"),
            PersistJobDataAfterExecution: GetBooleanProperty(json, "persistJobDataAfterExecution"),
            JobDataMap: ReadJobDataMap(GetOptionalProperty(json, "jobDataMap")));
    }

    /// <remarks>
    /// The triggers themselves are needed for the schedule summary, and their states come from a single
    /// trigger listing filtered by job rather than one state request per trigger. The kind and the
    /// summary are read off the trigger by <see cref="TriggerDisplay" />, the same way the in-process
    /// client reads them — this used to echo the wire's discriminator instead, so the two clients
    /// called the same trigger <c>CronTrigger</c> and <c>Cron</c>.
    /// </remarks>
    public async ValueTask<List<TriggerHeaderDto>> GetJobTriggers(string schedulerName, JobKeyDto key, CancellationToken cancellationToken = default)
    {
        JsonElement json = await GetJson($"{JobPath(schedulerName, key)}/triggers", cancellationToken).ConfigureAwait(false);
        if (json.ValueKind is not JsonValueKind.Array)
        {
            return [];
        }

        Dictionary<(string Group, string Name), TriggerState?> states = await GetJobTriggerStates(schedulerName, key, cancellationToken).ConfigureAwait(false);

        List<TriggerHeaderDto> result = [];
        foreach (JsonElement element in json.EnumerateArray())
        {
            ITrigger? trigger = element.Deserialize<ITrigger>(quartzSerializerOptions);
            if (trigger is null)
            {
                continue;
            }

            result.Add(new TriggerHeaderDto(
                Group: trigger.Key.Group,
                Name: trigger.Key.Name,
                TriggerType: TriggerDisplay.TypeName(trigger),
                ScheduleSummary: TriggerDisplay.ScheduleSummary(trigger),
                State: states.TryGetValue((trigger.Key.Group, trigger.Key.Name), out TriggerState? state) ? state : null,
                ExecutionGroup: trigger.ExecutionGroup));
        }

        return result;
    }

    private async ValueTask<Dictionary<(string Group, string Name), TriggerState?>> GetJobTriggerStates(
        string schedulerName,
        JobKeyDto jobKey,
        CancellationToken cancellationToken = default)
    {
        Dictionary<(string Group, string Name), TriggerState?> states = new();
        int skip = 0;
        while (true)
        {
            string path = $"{GetSchedulerPath(schedulerName)}/triggers?jobName={Uri.EscapeDataString(jobKey.Name)}&jobGroup={Uri.EscapeDataString(jobKey.Group)}&skip={skip.ToString(CultureInfo.InvariantCulture)}";
            JsonElement json = await GetJson(path, cancellationToken).ConfigureAwait(false);
            JsonElement items = GetOptionalProperty(json, "items");
            if (items.ValueKind is not JsonValueKind.Array)
            {
                return states;
            }

            int itemCount = 0;
            foreach (JsonElement header in items.EnumerateArray())
            {
                states[(GetStringProperty(header, "group"), GetStringProperty(header, "name"))] = GetTriggerStateProperty(header, "state");
                itemCount++;
            }

            if (!GetBooleanProperty(json, "hasMore") || itemCount == 0)
            {
                return states;
            }

            skip += itemCount;
        }
    }

    public async ValueTask<PagedResult<FireInstanceDto>> GetFireInstances(
        string schedulerName,
        DashboardFireInstanceQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        string path = BuildPagedPath($"{GetSchedulerPath(schedulerName)}/jobs/fire-instances", query.GroupContains, query);

        // The endpoint's own default is "executing", so "every state" has to be said out loud.
        path += $"&state={Uri.EscapeDataString(query.State?.ToString() ?? HttpApiConstants.AnyFireInstanceState)}";

        JsonElement json = await GetJson(path, cancellationToken).ConfigureAwait(false);
        JsonElement items = GetOptionalProperty(json, "items");

        List<FireInstanceDto> result = [];
        if (items.ValueKind is JsonValueKind.Array)
        {
            foreach (JsonElement item in items.EnumerateArray())
            {
                string? jobName = GetNullableStringProperty(item, "jobName");
                string? jobGroup = GetNullableStringProperty(item, "jobGroup");

                result.Add(new FireInstanceDto(
                    FireInstanceId: GetStringProperty(item, "fireInstanceId"),
                    TriggerKey: new TriggerKeyDto(GetStringProperty(item, "triggerGroup"), GetStringProperty(item, "triggerName")),
                    JobKey: jobName is not null && jobGroup is not null ? new JobKeyDto(jobGroup, jobName) : null,
                    SchedulerInstanceId: GetStringProperty(item, "schedulerInstanceId"),
                    State: GetFireInstanceStateProperty(item, "state") ?? FireInstanceState.Executing,
                    FireTimeUtc: GetDateTimeOffsetProperty(item, "fireTimeUtc"),
                    ScheduledFireTimeUtc: GetNullableDateTimeOffsetProperty(item, "scheduledFireTimeUtc"),
                    ExecutionGroup: GetNullableStringProperty(item, "executionGroup")));
            }
        }

        int totalCount = GetNullableIntProperty(json, "totalCount") ?? result.Count;
        return new PagedResult<FireInstanceDto>(result, GetBooleanProperty(json, "hasMore"), totalCount);
    }

    public ValueTask<bool> PauseJob(string schedulerName, JobKeyDto key, CancellationToken cancellationToken = default)
    {
        return PostReadingAppliedFlag($"{JobPath(schedulerName, key)}/pause", cancellationToken);
    }

    public ValueTask<bool> ResumeJob(string schedulerName, JobKeyDto key, CancellationToken cancellationToken = default)
    {
        return PostReadingAppliedFlag($"{JobPath(schedulerName, key)}/resume", cancellationToken);
    }

    public ValueTask TriggerJob(string schedulerName, JobKeyDto key, CancellationToken cancellationToken = default)
    {
        return Post($"{JobPath(schedulerName, key)}/trigger", body: null, cancellationToken);
    }

    public ValueTask TriggerJobWithData(string schedulerName, JobKeyDto key, JobDataMap jobDataMap, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(jobDataMap);

        TriggerJobRequest payload = new(jobDataMap);
        return Post($"{JobPath(schedulerName, key)}/trigger", payload, cancellationToken);
    }

    public ValueTask InterruptJob(string schedulerName, JobKeyDto key, CancellationToken cancellationToken = default)
    {
        return Post($"{JobPath(schedulerName, key)}/interrupt", body: null, cancellationToken);
    }

    public ValueTask InterruptFireInstance(string schedulerName, string fireInstanceId, CancellationToken cancellationToken = default)
    {
        return Post($"{GetSchedulerPath(schedulerName)}/jobs/interrupt/{Uri.EscapeDataString(fireInstanceId)}", body: null, cancellationToken);
    }

    public ValueTask DeleteJob(string schedulerName, JobKeyDto key, CancellationToken cancellationToken = default)
    {
        return Delete(JobPath(schedulerName, key), cancellationToken);
    }

    public ValueTask AddJob(string schedulerName, AddJobRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Post($"{GetSchedulerPath(schedulerName)}/jobs", request, cancellationToken);
    }

    public async ValueTask<PagedResult<TriggerHeaderDto>> GetTriggers(
        string schedulerName,
        DashboardTriggerQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        string path = BuildPagedPath($"{GetSchedulerPath(schedulerName)}/triggers", query.GroupContains, query);
        if (query.State.HasValue)
        {
            path += $"&state={Uri.EscapeDataString(query.State.Value.ToString())}";
        }

        JsonElement json = await GetJson(path, cancellationToken).ConfigureAwait(false);
        JsonElement items = GetOptionalProperty(json, "items");

        List<TriggerHeaderDto> result = [];
        if (items.ValueKind is JsonValueKind.Array)
        {
            foreach (JsonElement trigger in items.EnumerateArray())
            {
                // A listing does not load the triggers, so there is no schedule to summarise and no
                // kind to name.
                result.Add(new TriggerHeaderDto(
                    Group: GetStringProperty(trigger, "group"),
                    Name: GetStringProperty(trigger, "name"),
                    TriggerType: null,
                    ScheduleSummary: null,
                    State: GetTriggerStateProperty(trigger, "state"),
                    ExecutionGroup: GetNullableStringProperty(trigger, "executionGroup")));
            }
        }

        int totalCount = GetNullableIntProperty(json, "totalCount") ?? result.Count;
        return new PagedResult<TriggerHeaderDto>(result, GetBooleanProperty(json, "hasMore"), totalCount);
    }

    public async ValueTask<ITrigger> GetTrigger(string schedulerName, TriggerKeyDto key, CancellationToken cancellationToken = default)
    {
        JsonElement json = await GetJson(TriggerPath(schedulerName, key), cancellationToken).ConfigureAwait(false);
        return json.Deserialize<ITrigger>(quartzSerializerOptions)
               ?? throw new InvalidOperationException($"Trigger '{key.Group}.{key.Name}' could not be read from the API response.");
    }

    public async ValueTask<TriggerState> GetTriggerState(string schedulerName, TriggerKeyDto key, CancellationToken cancellationToken = default)
    {
        JsonElement json = await GetJson($"{TriggerPath(schedulerName, key)}/state", cancellationToken).ConfigureAwait(false);
        return GetTriggerStateProperty(json, "state") ?? TriggerState.None;
    }

    public ValueTask<bool> PauseTrigger(string schedulerName, TriggerKeyDto key, CancellationToken cancellationToken = default)
    {
        return PostReadingAppliedFlag($"{TriggerPath(schedulerName, key)}/pause", cancellationToken);
    }

    public ValueTask<bool> ResumeTrigger(string schedulerName, TriggerKeyDto key, CancellationToken cancellationToken = default)
    {
        return PostReadingAppliedFlag($"{TriggerPath(schedulerName, key)}/resume", cancellationToken);
    }

    public ValueTask<bool> ResetTriggerFromErrorState(string schedulerName, TriggerKeyDto key, CancellationToken cancellationToken = default)
    {
        return PostReadingAppliedFlag($"{TriggerPath(schedulerName, key)}/reset-from-error-state", cancellationToken);
    }

    public ValueTask ScheduleJob(string schedulerName, ScheduleJobRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Post($"{GetSchedulerPath(schedulerName)}/triggers/schedule", request, cancellationToken);
    }

    public ValueTask UnscheduleJob(string schedulerName, TriggerKeyDto key, CancellationToken cancellationToken = default)
    {
        return Post($"{TriggerPath(schedulerName, key)}/unschedule", body: null, cancellationToken);
    }

    public ValueTask RescheduleJob(string schedulerName, TriggerKeyDto key, RescheduleRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Post($"{TriggerPath(schedulerName, key)}/reschedule", request, cancellationToken);
    }

    public async ValueTask<List<string>> GetCalendarNames(string schedulerName, CancellationToken cancellationToken = default)
    {
        List<string> result = [];
        int skip = 0;
        while (true)
        {
            JsonElement json = await GetJson($"{GetSchedulerPath(schedulerName)}/calendars?skip={skip.ToString(CultureInfo.InvariantCulture)}", cancellationToken).ConfigureAwait(false);
            JsonElement items = GetOptionalProperty(json, "items");
            if (items.ValueKind is not JsonValueKind.Array)
            {
                return result;
            }

            int itemCount = 0;
            foreach (JsonElement name in items.EnumerateArray())
            {
                result.Add(name.GetString() ?? string.Empty);
                itemCount++;
            }

            if (!GetBooleanProperty(json, "hasMore") || itemCount == 0)
            {
                return result;
            }

            skip += itemCount;
        }
    }

    public async ValueTask<ICalendar> GetCalendar(string schedulerName, string calendarName, CancellationToken cancellationToken = default)
    {
        JsonElement json = await GetJson($"{GetSchedulerPath(schedulerName)}/calendars/{Uri.EscapeDataString(calendarName)}", cancellationToken).ConfigureAwait(false);
        return json.Deserialize<ICalendar>(quartzSerializerOptions)
               ?? throw new InvalidOperationException($"Calendar '{calendarName}' could not be read from the API response.");
    }

    public ValueTask AddCalendar(string schedulerName, AddCalendarRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Post($"{GetSchedulerPath(schedulerName)}/calendars", request, cancellationToken);
    }

    public ValueTask DeleteCalendar(string schedulerName, string calendarName, CancellationToken cancellationToken = default)
    {
        return Delete($"{GetSchedulerPath(schedulerName)}/calendars/{Uri.EscapeDataString(calendarName)}", cancellationToken);
    }

    /// <remarks>
    /// Execution history is the dashboard's own record, kept by whichever process runs the schedulers.
    /// A Quartz HTTP API does not serve it, so this asks and reports "no history" when the answer is a
    /// 404 — which, against a plain HTTP API, it always is.
    /// </remarks>
    public async ValueTask<PagedResult<DashboardHistoryEntry>?> GetHistory(DashboardHistoryQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        HttpClient client = CreateClient();
        string path = $"{GetSchedulerPath(query.SchedulerName)}/history?skip={query.Skip.ToString(CultureInfo.InvariantCulture)}&take={query.Take.ToString(CultureInfo.InvariantCulture)}";
        if (!string.IsNullOrWhiteSpace(query.JobFilter))
        {
            path += $"&jobFilter={Uri.EscapeDataString(query.JobFilter)}";
        }

        if (!string.IsNullOrWhiteSpace(query.TriggerFilter))
        {
            path += $"&triggerFilter={Uri.EscapeDataString(query.TriggerFilter)}";
        }

        using HttpResponseMessage response = await client.GetAsync(path, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        PagedResult<DashboardHistoryEntry>? page = await response.Content
            .ReadFromJsonAsync<PagedResult<DashboardHistoryEntry>>(historySerializerOptions, cancellationToken)
            .ConfigureAwait(false);
        return page ?? new PagedResult<DashboardHistoryEntry>([], HasMore: false, TotalCount: 0);
    }

    private string ApiPath => options.Value.TrimmedApiPath;

    private string GetSchedulerPath(string schedulerName)
    {
        return $"{ApiPath}/schedulers/{Uri.EscapeDataString(schedulerName)}";
    }

    private string JobPath(string schedulerName, JobKeyDto key)
    {
        ArgumentNullException.ThrowIfNull(key);
        return $"{GetSchedulerPath(schedulerName)}/jobs/{Uri.EscapeDataString(key.Group)}/{Uri.EscapeDataString(key.Name)}";
    }

    private string TriggerPath(string schedulerName, TriggerKeyDto key)
    {
        ArgumentNullException.ThrowIfNull(key);
        return $"{GetSchedulerPath(schedulerName)}/triggers/{Uri.EscapeDataString(key.Group)}/{Uri.EscapeDataString(key.Name)}";
    }

    /// <remarks>
    /// <c>take</c> is always sent, because omitting it hands the page size to the server's own default
    /// rather than to the caller — the same reason <c>QueryStringBuilder</c> always sends it.
    /// </remarks>
    private static string BuildPagedPath(string path, string? groupFilter, PagedQuery query)
    {
        string result = $"{path}?skip={query.Skip.ToString(CultureInfo.InvariantCulture)}&take={query.Take.ToString(CultureInfo.InvariantCulture)}&includeTotalCount=true";
        if (!string.IsNullOrWhiteSpace(groupFilter))
        {
            result += $"&groupContains={Uri.EscapeDataString(groupFilter)}";
        }

        return result;
    }

    private HttpClient CreateClient()
    {
        HttpClient client = httpClientFactory.CreateClient("QuartzDashboard");

        // Use the explicitly configured BaseUrl when available to avoid SSRF via Host header injection.
        Uri? configuredBaseUrl = options.Value.BaseUrl;
        if (configuredBaseUrl is not null)
        {
            // HttpClient only treats a base address as a prefix when it ends in '/', so a URL given
            // without one would otherwise have its last segment replaced by every relative request.
            string absolute = configuredBaseUrl.AbsoluteUri;
            client.BaseAddress = absolute.EndsWith('/') ? configuredBaseUrl : new Uri(absolute + "/");
            return client;
        }

        HttpContext? httpContext = httpContextAccessor.HttpContext;
        if (httpContext is not null)
        {
            string pathBase = httpContext.Request.PathBase.HasValue ? httpContext.Request.PathBase.Value! : "/";
            if (!pathBase.EndsWith('/'))
            {
                pathBase += "/";
            }

            UriBuilder uriBuilder = new(httpContext.Request.Scheme, httpContext.Request.Host.Host, httpContext.Request.Host.Port ?? -1)
            {
                Path = pathBase
            };
            Uri baseAddress = uriBuilder.Uri;
            cachedBaseAddress = baseAddress;
            client.BaseAddress = baseAddress;

            string cookieHeader = httpContext.Request.Headers.Cookie.ToString();
            if (!string.IsNullOrWhiteSpace(cookieHeader))
            {
                cachedCookieHeader = cookieHeader;
            }
        }
        else if (cachedBaseAddress is not null)
        {
            client.BaseAddress = cachedBaseAddress;
        }
        else if (client.BaseAddress is null)
        {
            client.BaseAddress = new Uri("http://localhost/");
        }

        if (!string.IsNullOrWhiteSpace(cachedCookieHeader))
        {
            client.DefaultRequestHeaders.Remove("Cookie");
            _ = client.DefaultRequestHeaders.TryAddWithoutValidation("Cookie", cachedCookieHeader);
        }

        return client;
    }

    private async ValueTask<JsonElement> GetJson(string path, CancellationToken cancellationToken = default)
    {
        HttpClient client = CreateClient();
        using HttpResponseMessage response = await client.GetAsync(path, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return await ParseJson(response, cancellationToken).ConfigureAwait(false);
    }

    /// <remarks>
    /// The body is written with the Quartz converters, because a request carrying a trigger, a
    /// calendar or a job data map has to travel in the discriminated shape the API reads — reflection
    /// over the concrete type writes something the server cannot parse back.
    /// </remarks>
    private async ValueTask Post(string path, object? body = null, CancellationToken cancellationToken = default)
    {
        EnsureWritable();

        HttpClient client = CreateClient();
        HttpResponseMessage response;
        if (body is null)
        {
            response = await client.PostAsync(path, content: null, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            response = await client.PostAsJsonAsync(path, body, quartzSerializerOptions, cancellationToken).ConfigureAwait(false);
        }

        using (response)
        {
            response.EnsureSuccessStatusCode();
        }
    }

    /// <summary>
    /// Posts a single-key mutation and reads the <c>applied</c> flag the missing-key rule puts
    /// in the response body.
    /// </summary>
    private async ValueTask<bool> PostReadingAppliedFlag(string path, CancellationToken cancellationToken = default)
    {
        EnsureWritable();

        HttpClient client = CreateClient();
        using HttpResponseMessage response = await client.PostAsync(path, content: null, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        JsonElement json = await ParseJson(response, cancellationToken).ConfigureAwait(false);
        return GetBooleanProperty(json, "applied");
    }

    private async ValueTask Delete(string path, CancellationToken cancellationToken = default)
    {
        EnsureWritable();

        HttpClient client = CreateClient();
        using HttpResponseMessage response = await client.DeleteAsync(path, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
    }

    private void EnsureWritable()
    {
        if (options.Value.ReadOnly)
        {
            throw new InvalidOperationException("Quartz dashboard is configured as read-only.");
        }
    }

    private static async ValueTask<JsonElement> ParseJson(HttpResponseMessage response, CancellationToken cancellationToken = default)
    {
        string jsonContent = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        using JsonDocument document = JsonDocument.Parse(jsonContent);
        return document.RootElement.Clone();
    }

    /// <summary>
    /// Reads the scheduler status the API reports. It is a name on the wire; the numeric form is still
    /// read so that a dashboard can talk to a server that predates the change.
    /// </summary>
    private static SchedulerStatus GetSchedulerStatusProperty(JsonElement element, string propertyName)
    {
        if (element.ValueKind is not JsonValueKind.Object || !element.TryGetProperty(propertyName, out JsonElement value))
        {
            return SchedulerStatus.Unknown;
        }

        if (value.ValueKind is JsonValueKind.String)
        {
            return Enum.TryParse(value.GetString(), ignoreCase: true, out SchedulerStatus parsed)
                ? parsed
                : SchedulerStatus.Unknown;
        }

        if (value.ValueKind is JsonValueKind.Number && value.TryGetInt32(out int status) && Enum.IsDefined((SchedulerStatus) status))
        {
            return (SchedulerStatus) status;
        }

        return SchedulerStatus.Unknown;
    }

    private static string GetStringProperty(JsonElement element, string propertyName)
    {
        if (element.ValueKind is not JsonValueKind.Object)
        {
            return string.Empty;
        }

        if (!element.TryGetProperty(propertyName, out JsonElement value))
        {
            return string.Empty;
        }

        return value.GetString() ?? string.Empty;
    }

    private static string? GetNullableStringProperty(JsonElement element, string propertyName)
    {
        if (element.ValueKind is not JsonValueKind.Object)
        {
            return null;
        }

        if (!element.TryGetProperty(propertyName, out JsonElement value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.Null => null,
            JsonValueKind.String => value.GetString(),
            _ => value.ToString()
        };
    }

    private static int GetIntProperty(JsonElement element, string propertyName)
    {
        if (element.ValueKind is not JsonValueKind.Object)
        {
            return 0;
        }

        if (!element.TryGetProperty(propertyName, out JsonElement value))
        {
            return 0;
        }

        if (value.ValueKind is JsonValueKind.Number && value.TryGetInt32(out int intValue))
        {
            return intValue;
        }

        if (value.ValueKind is JsonValueKind.String &&
            int.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedValue))
        {
            return parsedValue;
        }

        return 0;
    }

    private static int? GetNullableIntProperty(JsonElement element, string propertyName)
    {
        if (element.ValueKind is not JsonValueKind.Object)
        {
            return null;
        }

        if (!element.TryGetProperty(propertyName, out JsonElement value))
        {
            return null;
        }

        if (value.ValueKind is JsonValueKind.Number && value.TryGetInt32(out int intValue))
        {
            return intValue;
        }

        if (value.ValueKind is JsonValueKind.String &&
            int.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedValue))
        {
            return parsedValue;
        }

        return null;
    }

    /// <summary>
    /// Reads a trigger state the API reports. It is a name on the wire; the numeric form is still read so
    /// that a dashboard can talk to a server that predates the change.
    /// </summary>
    private static TriggerState? GetTriggerStateProperty(JsonElement element, string propertyName)
    {
        if (element.ValueKind is not JsonValueKind.Object)
        {
            return null;
        }

        if (!element.TryGetProperty(propertyName, out JsonElement value))
        {
            return null;
        }

        if (value.ValueKind is JsonValueKind.String)
        {
            return Enum.TryParse(value.GetString(), ignoreCase: true, out TriggerState parsed) ? parsed : null;
        }

        if (value.ValueKind is JsonValueKind.Number && value.TryGetInt32(out int intValue) && Enum.IsDefined((TriggerState) intValue))
        {
            return (TriggerState) intValue;
        }

        return null;
    }

    private static bool GetBooleanProperty(JsonElement element, string propertyName)
    {
        if (element.ValueKind is not JsonValueKind.Object)
        {
            return false;
        }

        if (!element.TryGetProperty(propertyName, out JsonElement value))
        {
            return false;
        }

        if (value.ValueKind is JsonValueKind.True)
        {
            return true;
        }

        if (value.ValueKind is JsonValueKind.False)
        {
            return false;
        }

        if (value.ValueKind is JsonValueKind.String &&
            bool.TryParse(value.GetString(), out bool parsedValue))
        {
            return parsedValue;
        }

        return false;
    }

    private static DateTimeOffset GetDateTimeOffsetProperty(JsonElement element, string propertyName)
    {
        if (element.ValueKind is not JsonValueKind.Object)
        {
            return default;
        }

        if (!element.TryGetProperty(propertyName, out JsonElement value))
        {
            return default;
        }

        if (value.ValueKind is JsonValueKind.String &&
            DateTimeOffset.TryParse(value.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTimeOffset parsed))
        {
            return parsed;
        }

        if (value.ValueKind is JsonValueKind.Number && value.TryGetInt64(out long unixMilliseconds))
        {
            return DateTimeOffset.FromUnixTimeMilliseconds(unixMilliseconds);
        }

        return default;
    }

    private static DateTimeOffset? GetNullableDateTimeOffsetProperty(JsonElement element, string propertyName)
    {
        if (element.ValueKind is not JsonValueKind.Object
            || !element.TryGetProperty(propertyName, out JsonElement value)
            || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }

        return GetDateTimeOffsetProperty(element, propertyName);
    }

    private static FireInstanceState? GetFireInstanceStateProperty(JsonElement element, string propertyName)
    {
        if (element.ValueKind is not JsonValueKind.Object || !element.TryGetProperty(propertyName, out JsonElement value))
        {
            return null;
        }

        if (value.ValueKind is JsonValueKind.String)
        {
            return Enum.TryParse(value.GetString(), ignoreCase: true, out FireInstanceState parsed) ? parsed : null;
        }

        if (value.ValueKind is JsonValueKind.Number && value.TryGetInt32(out int intValue) && Enum.IsDefined((FireInstanceState) intValue))
        {
            return (FireInstanceState) intValue;
        }

        return null;
    }

    private static JsonElement GetOptionalProperty(JsonElement element, string propertyName)
    {
        if (element.ValueKind is not JsonValueKind.Object)
        {
            return default;
        }

        if (!element.TryGetProperty(propertyName, out JsonElement value))
        {
            return default;
        }

        return value.Clone();
    }

    /// <summary>
    /// Reads the <c>jobDataMap</c> member of a job detail body. An absent or null one is an empty map:
    /// a job with no data is not a job whose data could not be read.
    /// </summary>
    private JobDataMap ReadJobDataMap(JsonElement element)
    {
        if (element.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
        {
            return new JobDataMap();
        }

        return element.Deserialize<JobDataMap>(quartzSerializerOptions) ?? new JobDataMap();
    }

    private static string? DescribeSchedule(JsonElement trigger)
    {
        string? cron = GetNullableStringProperty(trigger, "cronExpressionString");
        if (!string.IsNullOrWhiteSpace(cron))
        {
            return cron;
        }

        string? repeatInterval = GetNullableStringProperty(trigger, "repeatIntervalTimeSpan");
        if (!string.IsNullOrWhiteSpace(repeatInterval))
        {
            string summary = "Every " + repeatInterval;
            int repeatCount = GetIntProperty(trigger, "repeatCount");
            return summary + (repeatCount < 0 ? ", repeat forever" : ", " + repeatCount + " time(s)");
        }

        return null;
    }

    public async ValueTask<ExecutionLimitsDto?> GetExecutionLimits(string schedulerName, CancellationToken cancellationToken = default)
    {
        using HttpClient client = CreateClient();
        string url = $"{GetSchedulerPath(schedulerName)}/execution-limits";
        using HttpResponseMessage response = await client.GetAsync(url, cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        JsonElement json = await ParseJson(response, cancellationToken).ConfigureAwait(false);
        if (!json.TryGetProperty("limits", out JsonElement limitsElement) || limitsElement.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        Dictionary<string, int?> dict = new();
        foreach (JsonProperty prop in limitsElement.EnumerateObject())
        {
            string key = prop.Name is "" or "_" ? "(default)" : prop.Name;
            dict[key] = prop.Value.ValueKind == JsonValueKind.Null ? null : prop.Value.GetInt32();
        }

        return dict.Count > 0 ? new ExecutionLimitsDto(dict) : null;
    }
}
