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

namespace Quartz.Dashboard.Services;

internal sealed class QuartzApiClient : IQuartzApiClient
{
    private readonly IHttpClientFactory httpClientFactory;
    private readonly IHttpContextAccessor httpContextAccessor;
    private readonly IOptions<QuartzDashboardOptions> options;
    private Uri? cachedBaseAddress;
    private string? cachedCookieHeader;

    public QuartzApiClient(
        IHttpClientFactory httpClientFactory,
        IHttpContextAccessor httpContextAccessor,
        IOptions<QuartzDashboardOptions> options)
    {
        this.httpClientFactory = httpClientFactory;
        this.httpContextAccessor = httpContextAccessor;
        this.options = options;
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
            string status = TranslateSchedulerStatus(GetIntProperty(scheduler, "status"));
            result.Add(new SchedulerHeaderDto(schedulerName, schedulerInstanceId, status));
        }

        return result;
    }

    public async ValueTask<SchedulerDetailDto> GetScheduler(string schedulerName, CancellationToken cancellationToken = default)
    {
        JsonElement json = await GetJson($"{GetSchedulerPath(schedulerName)}", cancellationToken).ConfigureAwait(false);
        string resolvedName = GetStringProperty(json, "name");
        string schedulerInstanceId = GetStringProperty(json, "schedulerInstanceId");
        string status = TranslateSchedulerStatus(GetIntProperty(json, "status"));
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

    public async ValueTask<JobPageDto> GetJobs(string schedulerName, string? groupFilter, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        string path = BuildPagedPath($"{GetSchedulerPath(schedulerName)}/jobs", groupFilter, page, pageSize);
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
        return new JobPageDto(page, pageSize, totalCount, GetBooleanProperty(json, "hasMore"), result);
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

    public async ValueTask<JobDetailDto> GetJob(string schedulerName, string group, string name, CancellationToken cancellationToken = default)
    {
        JsonElement json = await GetJson($"{GetSchedulerPath(schedulerName)}/jobs/{Uri.EscapeDataString(group)}/{Uri.EscapeDataString(name)}", cancellationToken).ConfigureAwait(false);

        return new JobDetailDto(
            Name: GetStringProperty(json, "name"),
            Group: GetStringProperty(json, "group"),
            JobType: GetStringProperty(json, "jobType"),
            Description: GetNullableStringProperty(json, "description"),
            Durable: GetBooleanProperty(json, "durable"),
            RequestsRecovery: GetBooleanProperty(json, "requestsRecovery"),
            ConcurrentExecutionDisallowed: GetBooleanProperty(json, "concurrentExecutionDisallowed"),
            PersistJobDataAfterExecution: GetBooleanProperty(json, "persistJobDataAfterExecution"),
            JobDataMap: GetOptionalProperty(json, "jobDataMap"));
    }

    /// <remarks>
    /// The triggers themselves are needed for the schedule summary, and their states come from a single
    /// trigger listing filtered by job rather than one state request per trigger.
    /// </remarks>
    public async ValueTask<List<TriggerHeaderDto>> GetJobTriggers(string schedulerName, string group, string name, CancellationToken cancellationToken = default)
    {
        JsonElement json = await GetJson($"{GetSchedulerPath(schedulerName)}/jobs/{Uri.EscapeDataString(group)}/{Uri.EscapeDataString(name)}/triggers", cancellationToken).ConfigureAwait(false);
        if (json.ValueKind is not JsonValueKind.Array)
        {
            return [];
        }

        Dictionary<(string Group, string Name), string?> states = await GetJobTriggerStates(schedulerName, group, name, cancellationToken).ConfigureAwait(false);

        List<TriggerHeaderDto> result = [];
        foreach (JsonElement trigger in json.EnumerateArray())
        {
            JsonElement key = GetOptionalProperty(trigger, "key");
            string triggerName = GetStringProperty(key, "name");
            string triggerGroup = GetStringProperty(key, "group");
            string? executionGroup = GetNullableStringProperty(trigger, "executionGroup");
            result.Add(new TriggerHeaderDto(triggerGroup, triggerName, executionGroup)
            {
                TriggerType = GetNullableStringProperty(trigger, "triggerType"),
                ScheduleSummary = DescribeSchedule(trigger),
                State = states.TryGetValue((triggerGroup, triggerName), out string? state) ? state : null
            });
        }

        return result;
    }

    private async ValueTask<Dictionary<(string Group, string Name), string?>> GetJobTriggerStates(
        string schedulerName,
        string jobGroup,
        string jobName,
        CancellationToken cancellationToken = default)
    {
        Dictionary<(string Group, string Name), string?> states = new();
        int skip = 0;
        while (true)
        {
            string path = $"{GetSchedulerPath(schedulerName)}/triggers?jobName={Uri.EscapeDataString(jobName)}&jobGroup={Uri.EscapeDataString(jobGroup)}&skip={skip.ToString(CultureInfo.InvariantCulture)}";
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

    public async ValueTask<List<CurrentlyExecutingJobDto>> GetCurrentlyExecutingJobs(string schedulerName, CancellationToken cancellationToken = default)
    {
        JsonElement json = await GetJson($"{GetSchedulerPath(schedulerName)}/jobs/currently-executing", cancellationToken).ConfigureAwait(false);
        if (json.ValueKind is not JsonValueKind.Array)
        {
            return [];
        }

        List<CurrentlyExecutingJobDto> result = [];
        foreach (JsonElement item in json.EnumerateArray())
        {
            JsonElement jobDetail = GetOptionalProperty(item, "jobDetail");
            string jobName = GetStringProperty(jobDetail, "name");
            string jobGroup = GetStringProperty(jobDetail, "group");

            JsonElement trigger = GetOptionalProperty(item, "trigger");
            JsonElement triggerKey = GetOptionalProperty(trigger, "key");
            string triggerName = GetStringProperty(triggerKey, "name");
            string triggerGroup = GetStringProperty(triggerKey, "group");
            string? executionGroup = GetNullableStringProperty(trigger, "executionGroup");

            DateTimeOffset fireTimeUtc = GetDateTimeOffsetProperty(item, "fireTime");
            string? fireInstanceId = GetNullableStringProperty(item, "fireInstanceId");

            result.Add(new CurrentlyExecutingJobDto(
                JobKey: new JobKeyDto(jobGroup, jobName),
                TriggerKey: new TriggerKeyDto(triggerGroup, triggerName),
                FireTimeUtc: fireTimeUtc,
                FireInstanceId: fireInstanceId,
                ExecutionGroup: executionGroup));
        }

        return result;
    }

    public async ValueTask<List<ExecutingFireInstanceDto>> GetExecutingFireInstances(string schedulerName, CancellationToken cancellationToken = default)
    {
        JsonElement json = await GetJson($"{GetSchedulerPath(schedulerName)}/jobs/executing-fire-instances", cancellationToken).ConfigureAwait(false);
        if (json.ValueKind is not JsonValueKind.Array)
        {
            return [];
        }

        List<ExecutingFireInstanceDto> result = [];
        foreach (JsonElement item in json.EnumerateArray())
        {
            string fireInstanceId = GetStringProperty(item, "fireInstanceId");
            string triggerName = GetStringProperty(item, "triggerName");
            string triggerGroup = GetStringProperty(item, "triggerGroup");
            string jobName = GetStringProperty(item, "jobName");
            string jobGroup = GetStringProperty(item, "jobGroup");
            string schedulerInstanceId = GetStringProperty(item, "schedulerInstanceId");
            DateTimeOffset fireTimeUtc = GetDateTimeOffsetProperty(item, "fireTimeUtc");
            DateTimeOffset? scheduledFireTimeUtc = GetNullableDateTimeOffsetProperty(item, "scheduledFireTimeUtc");

            result.Add(new ExecutingFireInstanceDto(
                FireInstanceId: fireInstanceId,
                TriggerKey: new TriggerKeyDto(triggerGroup, triggerName),
                JobKey: new JobKeyDto(jobGroup, jobName),
                SchedulerInstanceId: schedulerInstanceId,
                FireTimeUtc: fireTimeUtc,
                ScheduledFireTimeUtc: scheduledFireTimeUtc));
        }

        return result;
    }

    public ValueTask<bool> PauseJob(string schedulerName, string group, string name, CancellationToken cancellationToken = default)
    {
        return PostReadingAppliedFlag($"{GetSchedulerPath(schedulerName)}/jobs/{Uri.EscapeDataString(group)}/{Uri.EscapeDataString(name)}/pause", cancellationToken);
    }

    public ValueTask<bool> ResumeJob(string schedulerName, string group, string name, CancellationToken cancellationToken = default)
    {
        return PostReadingAppliedFlag($"{GetSchedulerPath(schedulerName)}/jobs/{Uri.EscapeDataString(group)}/{Uri.EscapeDataString(name)}/resume", cancellationToken);
    }

    public ValueTask TriggerJob(string schedulerName, string group, string name, CancellationToken cancellationToken = default)
    {
        return Post($"{GetSchedulerPath(schedulerName)}/jobs/{Uri.EscapeDataString(group)}/{Uri.EscapeDataString(name)}/trigger", body: null, cancellationToken);
    }

    public ValueTask TriggerJobWithData(string schedulerName, string group, string name, JsonElement jobDataMap, CancellationToken cancellationToken = default)
    {
        object payload = new
        {
            JobData = jobDataMap
        };
        return Post($"{GetSchedulerPath(schedulerName)}/jobs/{Uri.EscapeDataString(group)}/{Uri.EscapeDataString(name)}/trigger", payload, cancellationToken);
    }

    public ValueTask InterruptJob(string schedulerName, string group, string name, CancellationToken cancellationToken = default)
    {
        return Post($"{GetSchedulerPath(schedulerName)}/jobs/{Uri.EscapeDataString(group)}/{Uri.EscapeDataString(name)}/interrupt", body: null, cancellationToken);
    }

    public ValueTask DeleteJob(string schedulerName, string group, string name, CancellationToken cancellationToken = default)
    {
        return Delete($"{GetSchedulerPath(schedulerName)}/jobs/{Uri.EscapeDataString(group)}/{Uri.EscapeDataString(name)}", cancellationToken);
    }

    public ValueTask AddJob(string schedulerName, AddJobRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Post($"{GetSchedulerPath(schedulerName)}/jobs", request, cancellationToken);
    }

    public async ValueTask<TriggerPageDto> GetTriggers(
        string schedulerName,
        string? groupFilter,
        TriggerState? state,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        string path = BuildPagedPath($"{GetSchedulerPath(schedulerName)}/triggers", groupFilter, page, pageSize);
        if (state.HasValue)
        {
            path += $"&state={Uri.EscapeDataString(state.Value.ToString())}";
        }

        JsonElement json = await GetJson(path, cancellationToken).ConfigureAwait(false);
        JsonElement items = GetOptionalProperty(json, "items");

        List<TriggerHeaderDto> result = [];
        if (items.ValueKind is JsonValueKind.Array)
        {
            foreach (JsonElement trigger in items.EnumerateArray())
            {
                string triggerGroup = GetStringProperty(trigger, "group");
                string triggerName = GetStringProperty(trigger, "name");
                string? executionGroup = GetNullableStringProperty(trigger, "executionGroup");
                result.Add(new TriggerHeaderDto(triggerGroup, triggerName, executionGroup)
                {
                    State = GetTriggerStateProperty(trigger, "state")
                });
            }
        }

        int totalCount = GetNullableIntProperty(json, "totalCount") ?? result.Count;
        return new TriggerPageDto(page, pageSize, totalCount, GetBooleanProperty(json, "hasMore"), result);
    }

    public async ValueTask<TriggerDetailDto> GetTrigger(string schedulerName, string group, string name, CancellationToken cancellationToken = default)
    {
        JsonElement json = await GetJson($"{GetSchedulerPath(schedulerName)}/triggers/{Uri.EscapeDataString(group)}/{Uri.EscapeDataString(name)}", cancellationToken).ConfigureAwait(false);
        return new TriggerDetailDto(json);
    }

    public async ValueTask<string> GetTriggerState(string schedulerName, string group, string name, CancellationToken cancellationToken = default)
    {
        JsonElement json = await GetJson($"{GetSchedulerPath(schedulerName)}/triggers/{Uri.EscapeDataString(group)}/{Uri.EscapeDataString(name)}/state", cancellationToken).ConfigureAwait(false);
        return FormatTriggerState(GetIntProperty(json, "state"));
    }

    public ValueTask<bool> PauseTrigger(string schedulerName, string group, string name, CancellationToken cancellationToken = default)
    {
        return PostReadingAppliedFlag($"{GetSchedulerPath(schedulerName)}/triggers/{Uri.EscapeDataString(group)}/{Uri.EscapeDataString(name)}/pause", cancellationToken);
    }

    public ValueTask<bool> ResumeTrigger(string schedulerName, string group, string name, CancellationToken cancellationToken = default)
    {
        return PostReadingAppliedFlag($"{GetSchedulerPath(schedulerName)}/triggers/{Uri.EscapeDataString(group)}/{Uri.EscapeDataString(name)}/resume", cancellationToken);
    }

    public ValueTask<bool> ResetTriggerFromErrorState(string schedulerName, string group, string name, CancellationToken cancellationToken = default)
    {
        return PostReadingAppliedFlag($"{GetSchedulerPath(schedulerName)}/triggers/{Uri.EscapeDataString(group)}/{Uri.EscapeDataString(name)}/reset-from-error-state", cancellationToken);
    }

    public ValueTask ScheduleJob(string schedulerName, ScheduleJobRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Post($"{GetSchedulerPath(schedulerName)}/triggers/schedule", request, cancellationToken);
    }

    public ValueTask UnscheduleJob(string schedulerName, string group, string name, CancellationToken cancellationToken = default)
    {
        return Post($"{GetSchedulerPath(schedulerName)}/triggers/{Uri.EscapeDataString(group)}/{Uri.EscapeDataString(name)}/unschedule", body: null, cancellationToken);
    }

    public ValueTask RescheduleJob(string schedulerName, string group, string name, RescheduleRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Post($"{GetSchedulerPath(schedulerName)}/triggers/{Uri.EscapeDataString(group)}/{Uri.EscapeDataString(name)}/reschedule", request, cancellationToken);
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

    public async ValueTask<CalendarDetailDto> GetCalendar(string schedulerName, string calendarName, CancellationToken cancellationToken = default)
    {
        JsonElement json = await GetJson($"{GetSchedulerPath(schedulerName)}/calendars/{Uri.EscapeDataString(calendarName)}", cancellationToken).ConfigureAwait(false);
        return new CalendarDetailDto(json);
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

    public async ValueTask<JobHistoryPageDto?> GetHistory(JobHistoryQueryDto query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        HttpClient client = CreateClient();
        string path = $"{GetSchedulerPath(query.SchedulerName)}/history?page={query.Page}&pageSize={query.PageSize}";
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
        JsonElement json = await ParseJson(response, cancellationToken).ConfigureAwait(false);
        return new JobHistoryPageDto(json);
    }

    private string ApiPath => options.Value.TrimmedApiPath;

    private string GetSchedulerPath(string schedulerName)
    {
        return $"{ApiPath}/schedulers/{Uri.EscapeDataString(schedulerName)}";
    }

    private static string BuildPagedPath(string path, string? groupFilter, int page, int pageSize)
    {
        string result = $"{path}?skip={GetSkip(page, pageSize).ToString(CultureInfo.InvariantCulture)}&take={pageSize.ToString(CultureInfo.InvariantCulture)}&includeTotalCount=true";
        if (!string.IsNullOrWhiteSpace(groupFilter))
        {
            result += $"&groupContains={Uri.EscapeDataString(groupFilter)}";
        }

        return result;
    }

    private static int GetSkip(int page, int pageSize)
    {
        if (page <= 1 || pageSize <= 0)
        {
            return 0;
        }

        long skip = (long) (page - 1) * pageSize;
        return skip > int.MaxValue ? int.MaxValue : (int) skip;
    }

    private HttpClient CreateClient()
    {
        HttpClient client = httpClientFactory.CreateClient("QuartzDashboard");

        // Use the explicitly configured BaseUrl when available to avoid SSRF via Host header injection.
        string? configuredBaseUrl = options.Value.BaseUrl;
        if (!string.IsNullOrWhiteSpace(configuredBaseUrl))
        {
            client.BaseAddress = new Uri(configuredBaseUrl.TrimEnd('/') + "/");
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
            response = await client.PostAsJsonAsync(path, body, cancellationToken).ConfigureAwait(false);
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

    private static string TranslateSchedulerStatus(int status)
    {
        return status switch
        {
            1 => "Running",
            2 => "Standby",
            3 => "Shutdown",
            _ => "Unknown"
        };
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

    private static string? GetTriggerStateProperty(JsonElement element, string propertyName)
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
            return value.GetString();
        }

        if (value.ValueKind is JsonValueKind.Number && value.TryGetInt32(out int intValue))
        {
            return FormatTriggerState(intValue);
        }

        return null;
    }

    private static string FormatTriggerState(int state)
    {
        if (Enum.IsDefined(typeof(TriggerState), state))
        {
            return ((TriggerState) state).ToString();
        }

        return state.ToString(CultureInfo.InvariantCulture);
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
        if (element.ValueKind is not JsonValueKind.Object)
        {
            return null;
        }

        if (!element.TryGetProperty(propertyName, out JsonElement value))
        {
            return null;
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
