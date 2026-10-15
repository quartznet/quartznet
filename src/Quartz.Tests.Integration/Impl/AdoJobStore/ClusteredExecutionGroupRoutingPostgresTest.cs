using System.Collections.Concurrent;
using System.Collections.Specialized;
using System.Globalization;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;

namespace Quartz.Tests.Integration.Impl.AdoJobStore;

/// <summary>
/// Execution-group routing across two real nodes sharing one PostgreSQL database (#4069): each group
/// fires on the node that serves it, a group whose node is gone waits — unmodified, and reported — and a
/// node that serves it arriving later applies each trigger's misfire policy once.
/// </summary>
/// <remarks>
/// <para>
/// <c>JobStoreContractTest</c> and the SQLite fixture hold one store to the contract. None of that says
/// what two nodes do to each other's groups: that the <c>reports</c> node's misfire sweep leaves the
/// <c>gpu</c> rows exactly as they are while nobody serves them, which is what makes the health check's
/// inference sound, and what a 4.5 node in the same cluster would have undone.
/// </para>
/// <para>
/// PostgreSQL, whose misfire count runs the <c>DoubleCheckLockMisfireHandler</c> path the SQLite fixture
/// cannot reach, since SQLite forces every operation under the lock.
/// </para>
/// </remarks>
[Category("db-postgres")]
[NonParallelizable]
public sealed class ClusteredExecutionGroupRoutingPostgresTest : ClusteredJobStoreTestBase
{
    private const string Reports = "reports";
    private const string Gpu = "gpu";
    private const string Group = "routing";
    private const int TriggersPerGroup = 20;
    private const int LateGpuTriggers = 5;

    /// <summary>
    /// Short, so that three of them — the health check's default tolerance — pass inside the test.
    /// </summary>
    private const int MisfireThresholdMs = 3000;

    public ClusteredExecutionGroupRoutingPostgresTest() : base(TestConstants.PostgresProvider)
    {
    }

    protected override string SchedulerName => "ClusterRoutingTest";

    [SetUp]
    public void ResetRoutedJob() => RoutedJob.Reset();

    [Test]
    public async Task EachGroupFiresOnItsNodeAndAGroupNobodyServesWaitsForANodeThatDoes()
    {
        RecordingLoggerProvider reportsLogs = new();
        SchedulerHost reportsNode = await CreateSchedulerHost(
            "reports-node",
            configure: properties => Route(properties, Reports),
            configureBuilder: q =>
            {
                q.Services.AddLogging(logging => logging.AddProvider(reportsLogs));
                q.AddQuartzHealthChecks();
            });
        IScheduler gpuNode = await CreateScheduler("gpu-node", configure: properties => Route(properties, Gpu));
        IScheduler gpuNodeAgain = null;

        try
        {
            IJobDetail job = JobBuilder.Create<RoutedJob>()
                .WithIdentity("routedJob", Group)
                .StoreDurably()
                .Build();
            await reportsNode.Scheduler.AddJob(job, new AddJobOptions { Replace = true });

            await reportsNode.Scheduler.Start();
            await gpuNode.Start();

            // Phase 1: both groups due at once, through one node's API; each lands on the node that
            // serves it.
            for (int i = 0; i < TriggersPerGroup; i++)
            {
                await Schedule(reportsNode.Scheduler, job, Reports + "-" + i.ToString(CultureInfo.InvariantCulture), Reports);
                await Schedule(reportsNode.Scheduler, job, Gpu + "-" + i.ToString(CultureInfo.InvariantCulture), Gpu);
            }

            await WaitForCondition(
                () => Task.FromResult(RoutedJob.Executions.Count >= 2 * TriggersPerGroup),
                timeoutMs: 60_000,
                async () => $"all {2 * TriggersPerGroup} triggers to fire; {RoutedJob.Executions.Count} did. State:\n{await DumpDatabaseState()}");

            List<Execution> executions = [.. RoutedJob.Executions];
            executions.Should().HaveCount(2 * TriggersPerGroup, "every trigger fires exactly once");
            executions.Where(x => x.Group == Reports).Should().HaveCount(TriggersPerGroup)
                .And.OnlyContain(x => x.InstanceId == "reports-node",
                    "a reports trigger is acquired only by the node that serves reports");
            executions.Where(x => x.Group == Gpu).Should().HaveCount(TriggersPerGroup)
                .And.OnlyContain(x => x.InstanceId == "gpu-node",
                    "a gpu trigger is acquired only by the node that serves gpu");

            // Phase 2: the gpu node goes away. New gpu triggers wait for a node that serves them: the
            // reports node neither acquires nor re-arms them, and says so.
            await gpuNode.Shutdown(waitForJobsToComplete: false);
            RoutedJob.Reset();

            Dictionary<string, long> dueAt = new(StringComparer.Ordinal);
            for (int i = 0; i < LateGpuTriggers; i++)
            {
                string name = "gpu-late-" + i.ToString(CultureInfo.InvariantCulture);
                await Schedule(reportsNode.Scheduler, job, name, Gpu);
                dueAt[name] = await NextFireTimeOf(name);
            }

            // Past the health check's three thresholds, with a sweep or two to spare: the reports node's
            // misfire handler runs every second here.
            await Task.Delay(TimeSpan.FromMilliseconds(4 * MisfireThresholdMs));

            foreach ((string name, long due) in dueAt)
            {
                (await StateOf(name)).Should().Be("WAITING", "{0} is nobody's to take while no node serves gpu", name);
                (await NextFireTimeOf(name)).Should().Be(due,
                    "the reports node's sweep handles only the groups it serves, so {0} keeps the fire time it was stored with", name);
            }

            RoutedJob.Executions.Should().BeEmpty("the reports node never runs a gpu trigger");

            HealthReport report = await reportsNode.Services.GetRequiredService<HealthCheckService>().CheckHealthAsync();
            HealthReportEntry entry = report.Entries["quartz-scheduler"];
            entry.Status.Should().Be(HealthStatus.Degraded,
                "five gpu triggers are more than three thresholds overdue and this node does not serve gpu: no live node does");
            entry.Description.Should().Contain($"'{Gpu}' ({LateGpuTriggers})").And.Contain("does not serve");

            reportsLogs.Records.Count(record => record.EventId == 3609).Should().Be(1,
                "the handler logs 3609 on the transition and then at most hourly, not on every one-second pass. Logged: "
                + string.Join(" | ", reportsLogs.Records.Where(record => record.EventId == 3609).Select(record => record.Message)));

            // Phase 3: a node that serves gpu arrives. Its sweep applies each trigger's FireNow once, and
            // each fires once, there.
            gpuNodeAgain = await CreateScheduler("gpu-node-2", configure: properties => Route(properties, Gpu));
            await gpuNodeAgain.Start();

            await WaitForCondition(
                () => Task.FromResult(RoutedJob.Executions.Count >= LateGpuTriggers),
                timeoutMs: 60_000,
                async () => $"the {LateGpuTriggers} waiting gpu triggers to fire on the new node; {RoutedJob.Executions.Count} did. State:\n{await DumpDatabaseState()}");

            // Room for a second firing to show up, were the policy applied twice.
            await Task.Delay(TimeSpan.FromMilliseconds(2 * MisfireThresholdMs));

            List<Execution> late = [.. RoutedJob.Executions];
            late.Select(x => x.TriggerName).Should().BeEquivalentTo(dueAt.Keys,
                "a trigger that waited for a serving node fires exactly once when one arrives: FireNow runs the missed occurrence, and nothing re-armed it meanwhile");
            late.Should().OnlyContain(x => x.InstanceId == "gpu-node-2");

            await WaitForCondition(
                () => Task.FromResult(reportsLogs.Records.Any(record => record.EventId == 3610)),
                timeoutMs: 30_000,
                "the reports node to log 3610 once the gpu triggers are gone from its unserved count");
        }
        finally
        {
            await reportsNode.Scheduler.Shutdown(waitForJobsToComplete: false);
            if (gpuNode.Status != SchedulerStatus.Shutdown)
            {
                await gpuNode.Shutdown(waitForJobsToComplete: false);
            }

            if (gpuNodeAgain is not null)
            {
                await gpuNodeAgain.Shutdown(waitForJobsToComplete: false);
            }

            await reportsNode.Services.DisposeAsync();
        }
    }

    /// <summary>
    /// One node of the cluster, serving one group, with a misfire handler quick enough for the test to
    /// watch and the default two-second idle wait of the base fixture.
    /// </summary>
    private static void Route(NameValueCollection properties, string servedGroup)
    {
        properties["quartz.scheduler.servedExecutionGroups"] = servedGroup;
        properties["quartz.jobStore.misfireThreshold"] = MisfireThresholdMs.ToString(CultureInfo.InvariantCulture);
        properties["quartz.jobStore.misfireHandlerFrequency"] = "1000";
        properties["quartz.threadPool.maxConcurrency"] = "4";
    }

    /// <summary>
    /// A one-shot trigger due now, whose misfire policy fires the missed occurrence once.
    /// </summary>
    private static Task Schedule(IScheduler scheduler, IJobDetail job, string name, string executionGroup)
    {
        return scheduler.ScheduleJob(TriggerBuilder.Create()
            .WithIdentity(name, Group)
            .ForJob(job)
            .WithExecutionGroup(executionGroup)
            .StartNow()
            .WithSimpleSchedule(x => x.WithMisfireInstruction(SimpleTriggerMisfireInstruction.FireNow))
            .Build()).AsTask();
    }

    private async Task<string> StateOf(string triggerName)
    {
        return (string) await ExecuteScalar(
            "SELECT TRIGGER_STATE FROM QRTZ_TRIGGERS WHERE SCHED_NAME = @schedulerName AND TRIGGER_NAME = @triggerName",
            ("schedulerName", SchedulerName),
            ("triggerName", triggerName));
    }

    private async Task<long> NextFireTimeOf(string triggerName)
    {
        return Convert.ToInt64(await ExecuteScalar(
            "SELECT NEXT_FIRE_TIME FROM QRTZ_TRIGGERS WHERE SCHED_NAME = @schedulerName AND TRIGGER_NAME = @triggerName",
            ("schedulerName", SchedulerName),
            ("triggerName", triggerName)), CultureInfo.InvariantCulture);
    }

    public sealed record Execution(string TriggerName, string InstanceId, string Group);

    /// <summary>
    /// Records which node ran which trigger of which group; what every assertion here is about.
    /// </summary>
    public sealed class RoutedJob : IJob
    {
        private static volatile ConcurrentQueue<Execution> executions = new();

        public static ConcurrentQueue<Execution> Executions => executions;

        public static void Reset() => Interlocked.Exchange(ref executions, new ConcurrentQueue<Execution>());

        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
        {
            Executions.Enqueue(new Execution(context.Trigger.Key.Name, context.Scheduler.SchedulerInstanceId, context.Trigger.ExecutionGroup));
            return default;
        }
    }

    /// <summary>
    /// Keeps every record the node logs, for the two events the misfire handler owes this test.
    /// </summary>
    private sealed class RecordingLoggerProvider : ILoggerProvider
    {
        private readonly ConcurrentQueue<(int EventId, string Message)> records = new();

        public IReadOnlyCollection<(int EventId, string Message)> Records => records;

        public ILogger CreateLogger(string categoryName) => new RecordingLogger(this);

        public void Dispose()
        {
        }

        private sealed class RecordingLogger(RecordingLoggerProvider provider) : ILogger
        {
            public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
            {
                provider.records.Enqueue((eventId.Id, formatter(state, exception)));
            }
        }

        private sealed class NullScope : IDisposable
        {
            public static NullScope Instance { get; } = new();

            public void Dispose()
            {
            }
        }
    }
}
