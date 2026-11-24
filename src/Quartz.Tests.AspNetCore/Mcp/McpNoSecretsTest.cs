using System.Text.Json;

using Microsoft.Extensions.DependencyInjection;

using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

using Quartz.Extensibility;
using Quartz.Tests.AspNetCore.Support;

namespace Quartz.Tests.AspNetCore.Mcp;

/// <summary>
/// M7: no tool answers a data value — not job data, not trigger data, not a run's recorded input or captured log —
/// and the keys of a map are what an agent is shown of it.
/// </summary>
public sealed class McpNoSecretsTest
{
    private const string Sentinel = "SENTINEL-c5b1f0e2";

    private McpTestHost? host;

    [TearDown]
    public async Task TearDown()
    {
        if (host is not null)
        {
            await host.DisposeAsync();
            host = null;
        }
    }

    [Test]
    public async Task NoViewToolAnswersADataValueAndTheKeysAreListed()
    {
        host = await McpTestHost.Start(services: services => services.AddQuartzExecutionHistory());
        (JobKey job, TriggerKey trigger) = await host.ScheduleJob(
            jobData: new JobDataMap { ["connectionString"] = Sentinel },
            triggerData: new JobDataMap { ["apiKey"] = Sentinel });

        // A run whose recorded input, captured log and metrics all hold the sentinel too.
        IExecutionHistoryStore history = host.Services.GetRequiredService<IExecutionHistoryStore>();
        await history.AddExecution(new ExecutionHistoryEntry(
            host.Scheduler.SchedulerName, host.Scheduler.SchedulerInstanceId, job.Group, job.Name, trigger.Group, trigger.Name,
            DateTimeOffset.UtcNow.AddMinutes(-5), TimeSpan.FromSeconds(2), Succeeded: false, ExceptionMessage: "boom")
        {
            EntryId = "run-1",
            Input = "{\"secret\":\"" + Sentinel + "\"}",
            Log = "connecting with " + Sentinel,
            MetricsJson = "{\"token\":\"" + Sentinel + "\"}",
            Result = JobRunResult.Failed,
        });

        McpClient viewer = await host.Connect(RoleAuthenticationExtensions.Viewer);
        string scheduler = host.Scheduler.SchedulerName;

        List<CallToolResult> answers =
        [
            await McpTestHost.Call(viewer, "list_schedulers"),
            await McpTestHost.Call(viewer, "get_scheduler", new { scheduler }),
            await McpTestHost.Call(viewer, "list_cluster_nodes", new { scheduler }),
            await McpTestHost.Call(viewer, "list_jobs", new { scheduler }),
            await McpTestHost.Call(viewer, "get_job", new { scheduler, group = job.Group, name = job.Name }),
            await McpTestHost.Call(viewer, "list_triggers", new { scheduler }),
            await McpTestHost.Call(viewer, "get_trigger", new { scheduler, group = trigger.Group, name = trigger.Name }),
            await McpTestHost.Call(viewer, "list_executing", new { scheduler }),
            await McpTestHost.Call(viewer, "query_history", new { scheduler }),
            await McpTestHost.Call(viewer, "get_job_run_status", new { scheduler, group = job.Group, name = job.Name }),
        ];

        foreach (CallToolResult answer in answers)
        {
            McpTestHost.Answer(answer);
            string everything = McpTestHost.Text(answer) + answer.StructuredContent;
            everything.Should().NotContain(Sentinel, "no tool answers a data value, a recorded input, a captured log or a metric");
        }

        JsonElement jobAnswer = McpTestHost.Answer(answers[4]);
        jobAnswer.GetProperty("dataKeys").EnumerateArray().Select(static x => x.GetString()).Should().Equal("connectionString");

        JsonElement triggerAnswer = McpTestHost.Answer(answers[6]);
        triggerAnswer.GetProperty("dataKeys").EnumerateArray().Select(static x => x.GetString()).Should().Equal("apiKey");

        JsonElement run = McpTestHost.Answer(answers[8]).GetProperty("executions")[0];
        run.GetProperty("errorMessage").GetString().Should().Be("boom");
        run.GetProperty("result").GetString().Should().Be("Failed");
    }

    [Test]
    public async Task FreeTextIsCutToALengthAndSaysSo()
    {
        host = await McpTestHost.Start();
        IJobDetail job = JobBuilder.Create<DummyJob>().WithIdentity("long", "text").WithDescription(new string('d', 1500)).StoreDurably().Build();
        await host.Scheduler.AddJob(job);

        JsonElement row = McpTestHost.Answer(await McpTestHost.Call(await host.Connect(RoleAuthenticationExtensions.Viewer), "list_jobs", new { scheduler = host.Scheduler.SchedulerName }))
            .GetProperty("jobs")[0];

        row.GetProperty("description").GetString().Should().HaveLength(1000);
        row.GetProperty("descriptionTruncated").GetBoolean().Should().BeTrue();
    }
}
