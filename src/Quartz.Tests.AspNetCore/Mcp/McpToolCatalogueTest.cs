using System.Text.Json;

using Quartz.HttpApiContract;
using Quartz.Mcp.Impl;

namespace Quartz.Tests.AspNetCore.Mcp;

/// <summary>
/// M1: the nineteen tools are carriers of the HTTP API's catalogue, at its routes' levels, with annotations that say
/// what they do, schemas that refuse what they do not name, and no argument that carries data.
/// </summary>
public sealed class McpToolCatalogueTest
{
    private static readonly (string Tool, string[] Routes, SchedulerAccess Level)[] Expected =
    [
        ("list_schedulers", [nameof(SchedulerRoutes.GetAllSchedulers)], SchedulerAccess.View),
        ("get_scheduler", [nameof(SchedulerRoutes.GetSchedulerDetails)], SchedulerAccess.View),
        ("list_cluster_nodes", [nameof(SchedulerRoutes.GetClusterNodes)], SchedulerAccess.View),
        ("list_jobs", [nameof(SchedulerRoutes.QueryJobs), nameof(SchedulerRoutes.FetchJobRunStatuses)], SchedulerAccess.View),
        ("get_job", [nameof(SchedulerRoutes.GetJobDetails), nameof(SchedulerRoutes.GetJobTriggers), nameof(SchedulerRoutes.GetJobRunStatus)], SchedulerAccess.View),
        ("list_triggers", [nameof(SchedulerRoutes.QueryTriggers)], SchedulerAccess.View),
        ("get_trigger", [nameof(SchedulerRoutes.GetTrigger), nameof(SchedulerRoutes.GetTriggerState), nameof(SchedulerRoutes.GetTriggerFireTimes)], SchedulerAccess.View),
        ("list_executing", [nameof(SchedulerRoutes.QueryFireInstances)], SchedulerAccess.View),
        ("query_history", [nameof(SchedulerRoutes.QueryExecutionHistory)], SchedulerAccess.View),
        ("get_job_run_status", [nameof(SchedulerRoutes.GetJobRunStatus)], SchedulerAccess.View),
        ("pause_trigger", [nameof(SchedulerRoutes.PauseTrigger)], SchedulerAccess.Operate),
        ("pause_job", [nameof(SchedulerRoutes.PauseJob)], SchedulerAccess.Operate),
        ("resume_trigger", [nameof(SchedulerRoutes.ResumeTrigger)], SchedulerAccess.Operate),
        ("resume_job", [nameof(SchedulerRoutes.ResumeJob)], SchedulerAccess.Operate),
        ("trigger_job", [nameof(SchedulerRoutes.TriggerJob)], SchedulerAccess.Operate),
        ("interrupt", [nameof(SchedulerRoutes.InterruptJobInstance)], SchedulerAccess.Operate),
        ("reset_trigger_from_error", [nameof(SchedulerRoutes.ResetTriggerFromErrorState)], SchedulerAccess.Operate),
        ("reschedule", [nameof(SchedulerRoutes.RescheduleJob)], SchedulerAccess.Administer),
        ("unschedule", [nameof(SchedulerRoutes.UnscheduleJob)], SchedulerAccess.Administer),
    ];

    private static readonly QuartzTool[] Tools = QuartzTools.Create();

    [Test]
    public void TheToolsAreTheNineteenOfTheDesignOnTheirRoutesAtTheirLevels()
    {
        Tools.Select(static tool => (tool.Name, Routes: string.Join(",", tool.Routes.Select(static route => route.Name)), tool.Level))
            .Should().Equal(Expected.Select(static x => (x.Tool, string.Join(",", x.Routes), x.Level)));

        foreach (QuartzTool tool in Tools)
        {
            tool.Level.Should().Be(tool.Routes.Max(static route => SchedulerRoutes.RequiredAccess(route, carriesJobData: false)),
                $"{tool.Name}'s level is its routes', read from the route table");
            tool.Routes.Should().OnlyContain(route => ReferenceEquals(SchedulerRoutes.Find(route.Name), route), "every route is the catalogue's own");
        }
    }

    [Test]
    public void TheAnnotationsSayWhatEachToolDoes()
    {
        foreach (QuartzTool tool in Tools)
        {
            bool view = tool.Level == SchedulerAccess.View;
            tool.ProtocolTool.Annotations!.ReadOnlyHint.Should().Be(view, $"{tool.Name} reads only when it is a View tool");
            tool.ProtocolTool.Annotations.OpenWorldHint.Should().BeFalse();

            if (tool.Level == SchedulerAccess.Administer)
            {
                tool.ProtocolTool.Annotations.DestructiveHint.Should().BeTrue($"{tool.Name} removes or replaces a trigger");
                tool.Confirms.Should().BeTrue($"{tool.Name} changes nothing until confirmed");
            }
            else
            {
                tool.ProtocolTool.Annotations.DestructiveHint.Should().NotBe(true, $"{tool.Name} is not destructive");
                tool.Confirms.Should().BeFalse();
            }

            tool.ProtocolTool.Description.Should().EndWith(QuartzTool.UntrustedText, "every description says the text it reads is data");
        }

        Annotations("pause_trigger").IdempotentHint.Should().BeTrue();
        Annotations("resume_job").IdempotentHint.Should().BeTrue();
        Annotations("reset_trigger_from_error").IdempotentHint.Should().BeTrue();
        Annotations("trigger_job").IdempotentHint.Should().BeFalse("every call is another run");
    }

    [Test]
    public void EverySchemaRefusesWhatItDoesNotNameAndNoneTakesData()
    {
        string[] dataNames = ["jobData", "data", "jobDataMap", "input", "jobType", "trigger body", "context"];

        foreach (QuartzTool tool in Tools)
        {
            JsonElement schema = tool.ProtocolTool.InputSchema;
            schema.GetProperty("type").GetString().Should().Be("object", tool.Name);
            schema.GetProperty("additionalProperties").ValueKind.Should().Be(JsonValueKind.False, $"{tool.Name}'s schema refuses unknown properties");

            List<string> properties = schema.TryGetProperty("properties", out JsonElement named)
                ? [.. named.EnumerateObject().Select(static property => property.Name)]
                : [];
            properties.Should().NotIntersectWith(dataNames, $"{tool.Name} takes no data value");

            if (tool.NamesScheduler)
            {
                properties.Should().Contain("scheduler");
                schema.GetProperty("required").EnumerateArray().Select(static x => x.GetString()).Should().Contain("scheduler");
            }

            foreach (JsonProperty property in named.ValueKind == JsonValueKind.Object ? named.EnumerateObject() : Enumerable.Empty<JsonProperty>())
            {
                if (property.Value.TryGetProperty("type", out JsonElement type) && (type.ToString().Contains("object", StringComparison.Ordinal)))
                {
                    property.Value.GetProperty("additionalProperties").ValueKind.Should().Be(JsonValueKind.False, $"{tool.Name}.{property.Name} refuses unknown properties too");
                }
            }
        }

        Properties("trigger_job").Should().BeEquivalentTo(["scheduler", "group", "name"], "trigger_job takes no job data at all");
        Required("pause_trigger").Should().Contain("reason", "a pause says why");
        Required("pause_job").Should().Contain("reason");
        Tool("pause_trigger").ProtocolTool.InputSchema.GetProperty("properties").GetProperty("reason").GetProperty("minLength").GetInt32().Should().Be(3);
        Tool("list_jobs").ProtocolTool.InputSchema.GetProperty("properties").GetProperty("take").GetProperty("maximum").GetInt32().Should().Be(200);
    }

    private static QuartzTool Tool(string name) => Tools.Single(tool => tool.Name == name);

    private static ModelContextProtocol.Protocol.ToolAnnotations Annotations(string name) => Tool(name).ProtocolTool.Annotations!;

    private static List<string> Properties(string name) =>
        [.. Tool(name).ProtocolTool.InputSchema.GetProperty("properties").EnumerateObject().Select(static property => property.Name)];

    private static List<string?> Required(string name) =>
        [.. Tool(name).ProtocolTool.InputSchema.GetProperty("required").EnumerateArray().Select(static x => x.GetString())];
}
