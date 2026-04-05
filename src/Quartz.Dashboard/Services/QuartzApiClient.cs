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

public sealed class QuartzApiClient : IQuartzApiClient
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

    public async ValueTask<List<SchedulerHeaderDto>> GetSchedulers()
    {
        JsonElement json = await GetJsonAsync($"{ApiPath}/schedulers").ConfigureAwait(false);
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

    public async ValueTask<SchedulerDetailDto> GetScheduler(string schedulerName)
    {
        JsonElement json = await GetJsonAsync($"{GetSchedulerPath(schedulerName)}").ConfigureAwait(false);
        string resolvedName = GetStringProperty(json, "name");
        string schedulerInstanceId = GetStringProperty(json, "schedulerInstanceId");
        string status = TranslateSchedulerStatus(GetIntProperty(json, "status"));
        return new SchedulerDetailDto(schedulerInstanceId, resolvedName, status);
    }

    public ValueTask StartScheduler(string schedulerName)
    {
        return PostAsync($"{GetSchedulerPath(schedulerName)}/start");
    }

    public ValueTask StandbyScheduler(string schedulerName)
    {
        return PostAsync($"{GetSchedulerPath(schedulerName)}/standby");
    }

    public ValueTask ShutdownScheduler(string schedulerName)
    {
        return PostAsync($"{GetSchedulerPath(schedulerName)}/shutdown");
    }

    public ValueTask PauseAll(string schedulerName)
    {
        return PostAsync($"{GetSchedulerPath(schedulerName)}/pause-all");
    }

    public ValueTask ResumeAll(string schedulerName)
    {
        return PostAsync($"{GetSchedulerPath(schedulerName)}/resume-all");
    }

    public async ValueTask<List<JobKeyDto>> GetJobKeys(string schedulerName, string? groupFilter = null)
    {
        string path = $"{GetSchedulerPath(schedulerName)}/jobs";
        if (!string.IsNullOrWhiteSpace(groupFilter))
        {
            path += $"?groupContains={Uri.EscapeDataString(groupFilter)}";
        }

        JsonElement json = await GetJsonAsync(path).ConfigureAwait(false);
        if (json.ValueKind is not JsonValueKind.Array)
        {
            return [];
        }

        List<JobKeyDto> result = [];
        foreach (JsonElement key in json.EnumerateArray())
        {
            string name = GetStringProperty(key, "name");
            string group = GetStringProperty(key, "group");
            result.Add(new JobKeyDto(group, name));
        }

        return result;
    }

    public async ValueTask<JobDetailDto> GetJob(string schedulerName, string group, string name)
    {
        JsonElement json = await GetJsonAsync($"{GetSchedulerPath(schedulerName)}/jobs/{Uri.EscapeDataString(group)}/{Uri.EscapeDataString(name)}").ConfigureAwait(false);

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

    public async ValueTask<List<TriggerHeaderDto>> GetJobTriggers(string schedulerName, string group, string name)
    {
        JsonElement json = await GetJsonAsync($"{GetSchedulerPath(schedulerName)}/jobs/{Uri.EscapeDataString(group)}/{Uri.EscapeDataString(name)}/triggers").ConfigureAwait(false);
        if (json.ValueKind is not JsonValueKind.Array)
        {
            return [];
        }

        List<TriggerHeaderDto> result = [];
        foreach (JsonElement trigger in json.EnumerateArray())
        {
            JsonElement key = GetOptionalProperty(trigger, "key");
            string triggerName = GetStringProperty(key, "name");
            string triggerGroup = GetStringProperty(key, "group");
            string? executionGroup = GetNullableStringProperty(trigger, "executionGroup");
            result.Add(new TriggerHeaderDto(triggerGroup, triggerName, executionGroup));
        }

        return result;
    }

    public async ValueTask<List<CurrentlyExecutingJobDto>> GetCurrentlyExecutingJobs(string schedulerName)
    {
        JsonElement json = await GetJsonAsync($"{GetSchedulerPath(schedulerName)}/jobs/currently-executing").ConfigureAwait(false);
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

    public ValueTask PauseJob(string schedulerName, string group, string name)
    {
        return PostAsync($"{GetSchedulerPath(schedulerName)}/jobs/{Uri.EscapeDataString(group)}/{Uri.EscapeDataString(name)}/pause");
    }

    public ValueTask ResumeJob(string schedulerName, string group, string name)
    {
        return PostAsync($"{GetSchedulerPath(schedulerName)}/jobs/{Uri.EscapeDataString(group)}/{Uri.EscapeDataString(name)}/resume");
    }

    public ValueTask TriggerJob(string schedulerName, string group, string name)
    {
        return PostAsync($"{GetSchedulerPath(schedulerName)}/jobs/{Uri.EscapeDataString(group)}/{Uri.EscapeDataString(name)}/trigger");
    }

    public ValueTask TriggerJobWithData(string schedulerName, string group, string name, JsonElement jobDataMap)
    {
        object payload = new
        {
            JobData = jobDataMap
        };
        return PostAsync($"{GetSchedulerPath(schedulerName)}/jobs/{Uri.EscapeDataString(group)}/{Uri.EscapeDataString(name)}/trigger", payload);
    }

    public async ValueTask<bool> IsJobGroupPaused(string schedulerName, string group)
    {
        JsonElement json = await GetJsonAsync($"{GetSchedulerPath(schedulerName)}/jobs/groups/{Uri.EscapeDataString(group)}/paused").ConfigureAwait(false);
        return GetBooleanProperty(json, "paused");
    }

    public ValueTask InterruptJob(string schedulerName, string group, string name)
    {
        return PostAsync($"{GetSchedulerPath(schedulerName)}/jobs/{Uri.EscapeDataString(group)}/{Uri.EscapeDataString(name)}/interrupt");
    }

    public ValueTask DeleteJob(string schedulerName, string group, string name)
    {
        return DeleteAsync($"{GetSchedulerPath(schedulerName)}/jobs/{Uri.EscapeDataString(group)}/{Uri.EscapeDataString(name)}");
    }

    public ValueTask AddJob(string schedulerName, AddJobRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return PostAsync($"{GetSchedulerPath(schedulerName)}/jobs", request);
    }

    public async ValueTask<List<TriggerHeaderDto>> GetTriggerKeys(string schedulerName, string? groupFilter = null)
    {
        string path = $"{GetSchedulerPath(schedulerName)}/triggers";
        if (!string.IsNullOrWhiteSpace(groupFilter))
        {
            path += $"?groupContains={Uri.EscapeDataString(groupFilter)}";
        }

        JsonElement json = await GetJsonAsync(path).ConfigureAwait(false);
        if (json.ValueKind is not JsonValueKind.Array)
        {
            return [];
        }

        List<TriggerHeaderDto> result = [];
        foreach (JsonElement key in json.EnumerateArray())
        {
            string name = GetStringProperty(key, "name");
            string group = GetStringProperty(key, "group");
            string? executionGroup = GetNullableStringProperty(key, "executionGroup");
            result.Add(new TriggerHeaderDto(group, name, executionGroup));
        }

        return result;
    }

    public async ValueTask<TriggerDetailDto> GetTrigger(string schedulerName, string group, string name)
    {
        JsonElement json = await GetJsonAsync($"{GetSchedulerPath(schedulerName)}/triggers/{Uri.EscapeDataString(group)}/{Uri.EscapeDataString(name)}").ConfigureAwait(false);
        return new TriggerDetailDto(json);
    }

    public async ValueTask<string> GetTriggerState(string schedulerName, string group, string name)
    {
        JsonElement json = await GetJsonAsync($"{GetSchedulerPath(schedulerName)}/triggers/{Uri.EscapeDataString(group)}/{Uri.EscapeDataString(name)}/state").ConfigureAwait(false);
        int state = GetIntProperty(json, "state");
        if (Enum.IsDefined(typeof(TriggerState), state))
        {
            return ((TriggerState) state).ToString();
        }

        return state.ToString(CultureInfo.InvariantCulture);
    }

    public ValueTask PauseTrigger(string schedulerName, string group, string name)
    {
        return PostAsync($"{GetSchedulerPath(schedulerName)}/triggers/{Uri.EscapeDataString(group)}/{Uri.EscapeDataString(name)}/pause");
    }

    public ValueTask ResumeTrigger(string schedulerName, string group, string name)
    {
        return PostAsync($"{GetSchedulerPath(schedulerName)}/triggers/{Uri.EscapeDataString(group)}/{Uri.EscapeDataString(name)}/resume");
    }

    public ValueTask ResetTriggerFromErrorState(string schedulerName, string group, string name)
    {
        return PostAsync($"{GetSchedulerPath(schedulerName)}/triggers/{Uri.EscapeDataString(group)}/{Uri.EscapeDataString(name)}/reset-from-error-state");
    }

    public ValueTask ScheduleJob(string schedulerName, ScheduleJobRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return PostAsync($"{GetSchedulerPath(schedulerName)}/triggers/schedule", request);
    }

    public ValueTask UnscheduleJob(string schedulerName, string group, string name)
    {
        return PostAsync($"{GetSchedulerPath(schedulerName)}/triggers/{Uri.EscapeDataString(group)}/{Uri.EscapeDataString(name)}/unschedule");
    }

    public ValueTask RescheduleJob(string schedulerName, string group, string name, RescheduleRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return PostAsync($"{GetSchedulerPath(schedulerName)}/triggers/{Uri.EscapeDataString(group)}/{Uri.EscapeDataString(name)}/reschedule", request);
    }

    public async ValueTask<List<string>> GetCalendarNames(string schedulerName)
    {
        JsonElement json = await GetJsonAsync($"{GetSchedulerPath(schedulerName)}/calendars").ConfigureAwait(false);
        JsonElement names = GetOptionalProperty(json, "names");
        if (names.ValueKind is not JsonValueKind.Array)
        {
            return [];
        }

        List<string> result = [];
        foreach (JsonElement name in names.EnumerateArray())
        {
            result.Add(name.GetString() ?? string.Empty);
        }

        return result;
    }

    public async ValueTask<CalendarDetailDto> GetCalendar(string schedulerName, string calendarName)
    {
        JsonElement json = await GetJsonAsync($"{GetSchedulerPath(schedulerName)}/calendars/{Uri.EscapeDataString(calendarName)}").ConfigureAwait(false);
        return new CalendarDetailDto(json);
    }

    public ValueTask AddCalendar(string schedulerName, AddCalendarRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return PostAsync($"{GetSchedulerPath(schedulerName)}/calendars", request);
    }

    public ValueTask DeleteCalendar(string schedulerName, string calendarName)
    {
        return DeleteAsync($"{GetSchedulerPath(schedulerName)}/calendars/{Uri.EscapeDataString(calendarName)}");
    }

    public async ValueTask<JobHistoryPageDto?> GetHistory(JobHistoryQueryDto query)
    {
        ArgumentNullException.ThrowIfNull(query);

        System.Net.Http.HttpClient client = CreateClient();
        string path = $"{GetSchedulerPath(query.SchedulerName)}/history?page={query.Page}&pageSize={query.PageSize}";
        if (!string.IsNullOrWhiteSpace(query.JobFilter))
        {
            path += $"&jobFilter={Uri.EscapeDataString(query.JobFilter)}";
        }

        if (!string.IsNullOrWhiteSpace(query.TriggerFilter))
        {
            path += $"&triggerFilter={Uri.EscapeDataString(query.TriggerFilter)}";
        }

        using HttpResponseMessage response = await client.GetAsync(path).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        JsonElement json = await ParseJsonAsync(response).ConfigureAwait(false);
        return new JobHistoryPageDto(json);
    }

    private string ApiPath => options.Value.TrimmedApiPath;

    private string GetSchedulerPath(string schedulerName)
    {
        return $"{ApiPath}/schedulers/{Uri.EscapeDataString(schedulerName)}";
    }

    private System.Net.Http.HttpClient CreateClient()
    {
        System.Net.Http.HttpClient client = httpClientFactory.CreateClient("QuartzDashboard");

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

    private async ValueTask<JsonElement> GetJsonAsync(string path)
    {
        System.Net.Http.HttpClient client = CreateClient();
        using HttpResponseMessage response = await client.GetAsync(path).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return await ParseJsonAsync(response).ConfigureAwait(false);
    }

    private async ValueTask PostAsync(string path, object? body = null)
    {
        EnsureWritable();

        System.Net.Http.HttpClient client = CreateClient();
        HttpResponseMessage response;
        if (body is null)
        {
            response = await client.PostAsync(path, content: null).ConfigureAwait(false);
        }
        else
        {
            response = await client.PostAsJsonAsync(path, body).ConfigureAwait(false);
        }

        using (response)
        {
            response.EnsureSuccessStatusCode();
        }
    }

    private async ValueTask DeleteAsync(string path)
    {
        EnsureWritable();

        System.Net.Http.HttpClient client = CreateClient();
        using HttpResponseMessage response = await client.DeleteAsync(path).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
    }

    private void EnsureWritable()
    {
        if (options.Value.ReadOnly)
        {
            throw new InvalidOperationException("Quartz dashboard is configured as read-only.");
        }
    }

    private static async ValueTask<JsonElement> ParseJsonAsync(HttpResponseMessage response)
    {
        string jsonContent = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
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

    public async ValueTask<ExecutionLimitsDto?> GetExecutionLimits(string schedulerName)
    {
        using System.Net.Http.HttpClient client = CreateClient();
        string url = $"{GetSchedulerPath(schedulerName)}/execution-limits";
        using HttpResponseMessage response = await client.GetAsync(url).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        JsonElement json = await ParseJsonAsync(response).ConfigureAwait(false);
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
