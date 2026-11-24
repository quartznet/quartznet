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

using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

using Microsoft.Extensions.DependencyInjection;

using static Quartz.Trimming.Canary.Wire.CanarySteps;

namespace Quartz.Trimming.Canary.Wire;

/// <summary>
/// <c>Quartz.Mcp</c> served natively (#4245): <c>tools/list</c> and two tool calls, as raw JSON-RPC over Streamable
/// HTTP, against the host's scheduler under the same operation policy the HTTP API is held to.
/// </summary>
/// <remarks>
/// Raw requests rather than the SDK's client, so the canary proves the server half alone: the SDK's request
/// pipeline, Quartz's tools as <c>McpServerTool</c> subclasses, their schemas written by <c>JsonSchemaExporter</c>,
/// the arguments read and the answers written through <c>McpJsonContext</c>'s generated metadata, the Data
/// Protection and rate-limiting services they ask for, and the operation policy — with reflection off.
/// </remarks>
internal static class McpCheck
{
    public const string Path = "/quartz-mcp";

    private const string JobGroup = "mcp-canary";

    private const string JobName = "job";

    private const string TriggerName = "trigger";

    public static Task<bool> Run(IServiceProvider host, Uri siteRoot, CancellationToken cancellationToken)
    {
        (string Name, Func<Task<string>> Step)[] steps =
        [
            ("schedule", () => Schedule(host, cancellationToken)),
            ("tools-list", () => ToolsList(siteRoot, cancellationToken)),
            ("list-jobs", () => ListJobs(siteRoot, cancellationToken)),
            ("pause-trigger", () => PauseTrigger(host, siteRoot, cancellationToken)),
            ("reschedule-confirmed", () => RescheduleConfirmed(host, siteRoot, cancellationToken)),
        ];

        return CanarySteps.Run("mcp", steps);
    }

    private static async Task<string> Schedule(IServiceProvider host, CancellationToken cancellationToken)
    {
        IScheduler scheduler = await host.GetRequiredService<ISchedulerFactory>().GetScheduler(cancellationToken).ConfigureAwait(false);

        IJobDetail job = JobBuilder.Create<WireCanaryJob>().WithIdentity(JobName, JobGroup).UsingJobData("secret", "never-answered").StoreDurably().Build();
        ITrigger trigger = TriggerBuilder.Create()
            .WithIdentity(TriggerName, JobGroup)
            .ForJob(job)
            .StartAt(DateTimeOffset.UtcNow.AddHours(1))
            .WithSimpleSchedule(schedule => schedule.WithInterval(TimeSpan.FromHours(1)).RepeatForever())
            .Build();

        await scheduler.ScheduleJob(job, trigger, cancellationToken: cancellationToken).ConfigureAwait(false);
        return $"a job and a trigger in group {JobGroup} for the tools to read and pause";
    }

    private static async Task<string> ToolsList(Uri siteRoot, CancellationToken cancellationToken)
    {
        JsonElement result = await Call(siteRoot, """{"jsonrpc":"2.0","id":1,"method":"tools/list","params":{}}""", cancellationToken).ConfigureAwait(false);

        JsonElement tools = result.GetProperty("tools");
        Expect(tools.GetArrayLength() == 19, $"tools/list answered {tools.GetArrayLength()} tools to an administrator, not 19: {result}");

        foreach (JsonElement tool in tools.EnumerateArray())
        {
            JsonElement schema = tool.GetProperty("inputSchema");
            Expect(schema.TryGetProperty("additionalProperties", out JsonElement additional) && additional.ValueKind == JsonValueKind.False,
                $"{tool.GetProperty("name").GetString()}'s schema does not refuse unknown properties: {schema}");
        }

        return "an administrator is offered the 19 tools, each schema written by JsonSchemaExporter with additionalProperties false";
    }

    private static async Task<string> ListJobs(Uri siteRoot, CancellationToken cancellationToken)
    {
        string request = ToolCall(2, "list_jobs", $"{{\"scheduler\":\"{Program.SchedulerName}\",\"groupContains\":\"{JobGroup}\"}}");
        JsonElement result = await Call(siteRoot, request, cancellationToken).ConfigureAwait(false);
        JsonElement answer = Answer(result, "list_jobs");

        JsonElement jobs = answer.GetProperty("jobs");
        Expect(jobs.GetArrayLength() == 1 && jobs[0].GetProperty("name").GetString() == JobName, $"list_jobs did not list the canary's job: {answer}");
        Expect(answer.TryGetProperty("asOfUtc", out _) && answer.GetProperty("scheduler").GetProperty("key").GetString() == Program.SchedulerName,
            $"list_jobs carries no asOfUtc or scheduler key: {answer}");
        Expect(!result.ToString().Contains("never-answered", StringComparison.Ordinal), "list_jobs answered a job data value.");

        return "list_jobs reads the job through McpJsonContext, with asOfUtc, the scheduler's key and no data value";
    }

    private static async Task<string> PauseTrigger(IServiceProvider host, Uri siteRoot, CancellationToken cancellationToken)
    {
        string request = ToolCall(3, "pause_trigger", $"{{\"scheduler\":\"{Program.SchedulerName}\",\"group\":\"{JobGroup}\",\"name\":\"{TriggerName}\",\"reason\":\"canary pause\"}}");
        JsonElement result = await Call(siteRoot, request, cancellationToken).ConfigureAwait(false);
        JsonElement answer = Answer(result, "pause_trigger");
        Expect(answer.GetProperty("applied").GetBoolean(), $"pause_trigger was not applied: {answer}");

        IScheduler scheduler = await host.GetRequiredService<ISchedulerFactory>().GetScheduler(cancellationToken).ConfigureAwait(false);
        TriggerKey key = new(TriggerName, JobGroup);
        Expect(await scheduler.GetTriggerState(key, cancellationToken).ConfigureAwait(false) == TriggerState.Paused, "the trigger is not paused after pause_trigger.");

        PauseInfo? pause = await scheduler.GetTriggerPause(key, cancellationToken).ConfigureAwait(false);
        Expect(pause is { Reason: "canary pause", RequestedBy: "canary via MCP" }, $"the pause recorded {pause?.Reason} by {pause?.RequestedBy}.");

        return "pause_trigger pauses the trigger under the operation policy, and the pause says why and who, via MCP";
    }

    /// <summary>
    /// A destructive tool's two calls: the preview, whose confirmation token Data Protection protects with its
    /// time-limited protector, and the confirmed call, which unprotects the token, reads the payload back through
    /// <c>McpJsonContext</c>, checks it against the call and the trigger's state, and applies.
    /// </summary>
    private static async Task<string> RescheduleConfirmed(IServiceProvider host, Uri siteRoot, CancellationToken cancellationToken)
    {
        const string Cron = "0 0 3 * * ?";
        string arguments = $"\"scheduler\":\"{Program.SchedulerName}\",\"group\":\"{JobGroup}\",\"name\":\"{TriggerName}\",\"cronExpression\":\"{Cron}\",\"timeZone\":\"UTC\"";

        JsonElement preview = Answer(await Call(siteRoot, ToolCall(4, "reschedule", "{" + arguments + "}"), cancellationToken).ConfigureAwait(false), "reschedule preview");
        Expect(preview.GetProperty("applied").GetBoolean() == false, $"the preview applied something: {preview}");
        string token = preview.GetProperty("confirmationToken").GetString()
                       ?? throw new CanaryFailedException($"the preview carries no confirmation token: {preview}");

        IScheduler scheduler = await host.GetRequiredService<ISchedulerFactory>().GetScheduler(cancellationToken).ConfigureAwait(false);
        TriggerKey key = new(TriggerName, JobGroup);
        Expect(await scheduler.GetTrigger(key, cancellationToken).ConfigureAwait(false) is ISimpleTrigger, "the preview changed the trigger.");

        string confirm = ToolCall(5, "reschedule", "{" + arguments + ",\"confirmationToken\":\"" + token + "\"}");
        JsonElement applied = Answer(await Call(siteRoot, confirm, cancellationToken).ConfigureAwait(false), "reschedule confirmed");
        Expect(applied.GetProperty("applied").GetBoolean(), $"the confirmed reschedule was not applied: {applied}");

        ITrigger? rescheduled = await scheduler.GetTrigger(key, cancellationToken).ConfigureAwait(false);
        Expect(rescheduled is ICronTrigger { CronExpressionString: Cron }, $"the trigger is {rescheduled?.GetType().Name} after the confirmed reschedule.");

        JsonElement replay = await Call(siteRoot, confirm, cancellationToken).ConfigureAwait(false);
        Expect(replay.TryGetProperty("isError", out JsonElement isError) && isError.ValueKind == JsonValueKind.True, $"a replayed confirmation was not refused: {replay}");

        return "reschedule previews with a Data Protection token, applies when confirmed with it, and refuses the token a second time";
    }

    /// <summary>
    /// A <c>tools/call</c> request naming <paramref name="tool" />, with <paramref name="arguments" /> as written.
    /// </summary>
    private static string ToolCall(int id, string tool, string arguments)
    {
        return "{\"jsonrpc\":\"2.0\",\"id\":" + id.ToString(System.Globalization.CultureInfo.InvariantCulture)
               + ",\"method\":\"tools/call\",\"params\":{\"name\":\"" + tool + "\",\"arguments\":" + arguments + "}}";
    }

    /// <summary>
    /// The tool's answer: its structured content, once the call is known not to be an error.
    /// </summary>
    private static JsonElement Answer(JsonElement result, string tool)
    {
        Expect(!(result.TryGetProperty("isError", out JsonElement isError) && isError.ValueKind == JsonValueKind.True), $"{tool} answered an error: {result}");
        Expect(result.TryGetProperty("structuredContent", out JsonElement answer), $"{tool} answered no structured content: {result}");
        return answer;
    }

    /// <summary>
    /// Posts one JSON-RPC request as a Streamable HTTP client does, and reads the result out of the answer, whether
    /// the server wrote it as JSON or as one server-sent event.
    /// </summary>
    private static async Task<JsonElement> Call(Uri siteRoot, string body, CancellationToken cancellationToken)
    {
        using HttpClient client = new() { BaseAddress = siteRoot };
        using HttpRequestMessage request = new(HttpMethod.Post, Path.TrimStart('/'))
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));

        using HttpResponseMessage response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
        string text = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        Expect(response.StatusCode == HttpStatusCode.OK, $"the MCP endpoint answered {(int) response.StatusCode}: {text}");

        string json = text;
        if (response.Content.Headers.ContentType?.MediaType == "text/event-stream")
        {
            json = string.Join(
                "",
                text.Split('\n').Where(static line => line.StartsWith("data:", StringComparison.Ordinal)).Select(static line => line["data:".Length..].Trim()));
        }

        using JsonDocument document = JsonDocument.Parse(json);
        Expect(document.RootElement.TryGetProperty("result", out JsonElement result), $"the MCP endpoint answered no result: {json}");
        return result.Clone();
    }
}
