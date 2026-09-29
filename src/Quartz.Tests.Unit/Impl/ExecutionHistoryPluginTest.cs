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

#nullable enable

using System.Collections.Concurrent;
using System.Text.Json;

using FakeItEasy;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

using Quartz.Extensibility;
using Quartz.Impl;

namespace Quartz.Tests.Unit.Impl;

/// <summary>
/// What the recorder writes for a finished firing: the result it decides, what the job reported, and the
/// vetoes and manual runs it now records.
/// </summary>
public sealed class ExecutionHistoryPluginTest
{
    private const string SchedulerName = "recorder";
    private static readonly DateTimeOffset now = new(2026, 9, 29, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan waitLimit = TimeSpan.FromSeconds(20);

    // ---------------------------------------------------------------------------------------------
    // The result, rule by rule
    // ---------------------------------------------------------------------------------------------

    [Test]
    public async Task ACancelledRunIsCancelledWhateverItReported()
    {
        JobExecutionContextImpl context = Context();
        context.Result = JobRunReport.Succeeded("got halfway").With("pages", 20);
        context.Settle(ExecutionOutcome.Cancelled, retryScheduled: false);

        ExecutionHistoryEntry row = await Record(context);

        row.Result.Should().Be(JobRunResult.Cancelled, "a run that was stopped did not do what it set out to, whatever it says");
        row.Succeeded.Should().BeFalse();
        row.Summary.Should().Be("got halfway", "what the job said is kept, whichever rule decided the result");
        row.MetricsJson.Should().Be("{\"pages\":20}");
    }

    [Test]
    public async Task ARunThatThrewFailedWhateverItReported()
    {
        JobExecutionContextImpl context = Context();
        context.Result = JobRunReport.Skipped("nothing to do");
        context.Settle(ExecutionOutcome.Failed, retryScheduled: false);

        ExecutionHistoryEntry row = await Record(context, new JobExecutionException("the upstream refused"));

        row.Result.Should().Be(JobRunResult.Failed, "a throw is the scheduler's own evidence, and outranks the job's report");
        row.Succeeded.Should().BeFalse();
        row.ExceptionMessage.Should().Be("the upstream refused");
        row.Summary.Should().Be("nothing to do");
    }

    [TestCase(JobRunResult.Skipped, true)]
    [TestCase(JobRunResult.Failed, false)]
    [TestCase(JobRunResult.Succeeded, true)]
    public async Task AReportAnswersForARunThatReturned(JobRunResult reported, bool succeeded)
    {
        JobExecutionContextImpl context = Context();
        context.Result = new JobRunReport { Result = reported, Summary = "said so" };

        ExecutionHistoryEntry row = await Record(context);

        row.Result.Should().Be(reported);
        row.Succeeded.Should().Be(succeeded, "Succeeded means the run counts as a success, which a skip does");
        row.ExceptionMessage.Should().BeNull("nothing was thrown");
    }

    [Test]
    public async Task AReportOfYourOwnIsReadThroughTheInterface()
    {
        JobExecutionContextImpl context = Context();
        context.Result = new OwnReport();

        ExecutionHistoryEntry row = await Record(context);

        row.Result.Should().Be(JobRunResult.Skipped);
        row.Summary.Should().Be("own");
        row.MetricsJson.Should().Be("{\"n\":1}");
    }

    [Test]
    public async Task ARunThatReportedNothingSucceeded()
    {
        ExecutionHistoryEntry row = await Record(Context());

        row.Result.Should().Be(JobRunResult.Succeeded);
        row.Succeeded.Should().BeTrue();
        row.Summary.Should().BeNull();
        row.MetricsJson.Should().BeNull();
    }

    [Test]
    public async Task AResultThatIsNotAReportIsIgnored()
    {
        JobExecutionContextImpl context = Context();
        context.Result = 3;

        ExecutionHistoryEntry row = await Record(context);

        row.Result.Should().Be(JobRunResult.Succeeded,
            "Result is the job's to use for anything — NativeJob keeps an exit code there — and only a report says what the run achieved");
        row.Summary.Should().BeNull();
    }

    [Test]
    public async Task ALongSummaryIsCutWithoutSplittingACharacter()
    {
        // A surrogate pair straddling the limit: cutting at the limit would keep half of it.
        string summary = new string('a', JobRunReport.MaxSummaryLength - 1) + "\U0001F600" + "tail";
        JobExecutionContextImpl context = Context();
        context.Result = JobRunReport.Succeeded(summary);

        ExecutionHistoryEntry row = await Record(context);

        row.Summary.Should().Be(new string('a', JobRunReport.MaxSummaryLength - 1),
            "the summary is cut to what the history keeps, and a character is never cut in half");
    }

    [Test]
    public async Task MetricsAreRecordedAsJson()
    {
        JobExecutionContextImpl context = Context();
        context.Result = JobRunReport.Skipped("no stale reservations").With("released", 0).With("scanned", 1200);

        ExecutionHistoryEntry row = await Record(context);

        using JsonDocument metrics = JsonDocument.Parse(row.MetricsJson!);
        metrics.RootElement.GetProperty("released").GetInt32().Should().Be(0);
        metrics.RootElement.GetProperty("scanned").GetInt32().Should().Be(1200);
    }

    [Test]
    public async Task AMetricThatThrowsWhenWrittenCostsTheMetricsNotTheRow()
    {
        FakeLoggerProvider logs = new();
        InMemoryExecutionHistoryStore store = Store();

        ServiceCollection services = new();
        services.AddSingleton<IExecutionHistoryStore>(store);
        services.AddLogging(logging => logging.AddProvider(logs));
        await using ServiceProvider provider = services.BuildServiceProvider();

        JobExecutionContextImpl context = Context();
        context.Result = JobRunReport.Skipped("nothing to release").With("tenant", new ExecutionMetricsWriterTest.ThrowingValue());

        await new ExecutionHistoryPlugin(provider, new FakeTimeProvider(now)).JobWasExecuted(context, jobException: null);

        ExecutionHistoryEntry row = (await store.QueryExecutions(new ExecutionHistoryQuery { SchedulerName = SchedulerName }))
            .Items.Should().ContainSingle("the run happened, and a metric the job could not describe does not change that").Subject;

        row.Result.Should().Be(JobRunResult.Skipped);
        row.Summary.Should().Be("nothing to release");
        row.MetricsJson.Should().BeNull();

        logs.Collector.GetSnapshot().Should().ContainSingle(record => record.Id.Id == 1060,
            "the container's logging hears why the metrics are missing");
    }

    // ---------------------------------------------------------------------------------------------
    // Vetoes
    // ---------------------------------------------------------------------------------------------

    [Test]
    public async Task AVetoIsRecordedBesideTheMisfires()
    {
        InMemoryExecutionHistoryStore store = Store();
        ExecutionHistoryPlugin plugin = new(Provider(store), new FakeTimeProvider(now));

        JobExecutionContextImpl context = Context(scheduledAt: now.AddMinutes(-1));
        context.Settle(ExecutionOutcome.Vetoed, retryScheduled: false);

        await plugin.JobExecutionVetoed(context);

        MisfireHistoryEntry row = (await store.QueryMisfires(new MisfireHistoryQuery { SchedulerName = SchedulerName }))
            .Items.Should().ContainSingle().Subject;

        row.Reason.Should().Be(MisfireReason.Vetoed);
        row.JobKey.Should().Be(context.JobDetail.Key);
        row.TriggerGroup.Should().Be(context.Trigger.Key.Group);
        row.TriggerName.Should().Be(context.Trigger.Key.Name);
        row.ScheduledFireTimeUtc.Should().Be(now.AddMinutes(-1), "the firing that did not happen is the one it was due for");
        row.MisfiredAtUtc.Should().Be(now, "stamped on the scheduler's clock, as a misfire is");
        row.SchedulerInstanceId.Should().Be("node-a");

        (await store.QueryExecutions(new ExecutionHistoryQuery { SchedulerName = SchedulerName })).Items.Should().BeEmpty(
            "nothing ran, and a vetoed row among the executions would read as a failure");
        (await store.CountMisfires(SchedulerName, now.AddHours(-1))).Should().Be(0, "a veto is not a misfire");
    }

    [Test]
    public async Task NothingIsRecordedWhenTheHistoryIsOff()
    {
        InMemoryExecutionHistoryStore store = Store();
        ServiceCollection services = new();
        services.AddSingleton<IExecutionHistoryStore>(store);
        services.AddSingleton(Options.Create(new ExecutionHistoryOptions { MaxEntriesPerScheduler = 0 }));
        await using ServiceProvider provider = services.BuildServiceProvider();

        await new ExecutionHistoryPlugin(provider, new FakeTimeProvider(now)).JobExecutionVetoed(Context());

        (await store.QueryMisfires(new MisfireHistoryQuery { SchedulerName = SchedulerName })).Items.Should().BeEmpty();
    }

    [Test]
    public async Task AContainerBeingDisposedRecordsNoVeto()
    {
        ServiceProvider provider = new ServiceCollection().BuildServiceProvider();
        await provider.DisposeAsync();

        Func<Task> veto = async () => await new ExecutionHistoryPlugin(provider, new FakeTimeProvider(now)).JobExecutionVetoed(Context());

        await veto.Should().NotThrowAsync("a host shutting down is a reason to record nothing, not to fail the firing");
    }

    // ---------------------------------------------------------------------------------------------
    // On a running scheduler
    // ---------------------------------------------------------------------------------------------

    [Test]
    public async Task ACancelledJobIsRecordedAsCancelled()
    {
        await using ServiceProvider container = BuildContainer();
        IScheduler scheduler = await Start(container);

        await RunNow<SelfInterruptingJob>(scheduler, "interrupted");

        ExecutionHistoryEntry row = (await Rows(container, scheduler.SchedulerName, 1)).Single();

        row.Succeeded.Should().BeFalse(
            "the job was stopped, not finished; recording it as a success hid every cancellation from FailedFinally");
        row.Result.Should().Be(JobRunResult.Cancelled);
        row.ExceptionMessage.Should().BeNull("a cancelled job threw nothing the scheduler reports");

        await scheduler.Shutdown(waitForJobsToComplete: true);
    }

    /// <summary>
    /// A job that throws is recorded with its own message, and its status says the same.
    /// </summary>
    /// <remarks>
    /// The run shell reports the exception as <c>JobExecutionException</c> → <c>JobExecutionProcessException</c>
    /// → what the job threw, and both wrappers say "Job threw an unhandled exception". 4.3 recorded that,
    /// so every job that threw read the same on the dashboard and over HTTP.
    /// </remarks>
    [Test]
    public async Task AJobThatThrowsIsRecordedWithItsOwnMessage()
    {
        await using ServiceProvider container = BuildContainer();
        IScheduler scheduler = await Start(container);

        await RunNow<ThrowingJob>(scheduler, "throws");

        ExecutionHistoryEntry row = (await Rows(container, scheduler.SchedulerName, 1)).Single();
        await scheduler.Shutdown(waitForJobsToComplete: true);

        row.Result.Should().Be(JobRunResult.Failed);
        row.ExceptionMessage.Should().Be(ThrowingJob.Message,
            "the row names what the job threw, not the run shell's wrapper around it");

        JobRunStatus? status = await container.GetRequiredService<IExecutionHistoryStore>()
            .GetJobRunStatus(scheduler.SchedulerName, new JobKey("throws", "plugin"));
        status.Should().NotBeNull();
        status!.LastFailureMessage.Should().Be(ThrowingJob.Message, "the status is folded from the row");
    }

    [Test]
    public async Task AJobExecutionExceptionTheJobThrewIsRecordedWithItsOwnMessage()
    {
        await using ServiceProvider container = BuildContainer();
        IScheduler scheduler = await Start(container);

        await RunNow<RefusingJob>(scheduler, "refuses");

        ExecutionHistoryEntry row = (await Rows(container, scheduler.SchedulerName, 1)).Single();
        await scheduler.Shutdown(waitForJobsToComplete: true);

        row.ExceptionMessage.Should().Be(RefusingJob.Message,
            "a JobExecutionException the job threw itself is what it chose to say, and is not looked through to its cause");

        JobRunStatus? status = await container.GetRequiredService<IExecutionHistoryStore>()
            .GetJobRunStatus(scheduler.SchedulerName, new JobKey("refuses", "plugin"));
        status!.LastFailureMessage.Should().Be(RefusingJob.Message);
    }

    [Test]
    public async Task ARunAskedForWithTriggerJobIsManualAndAScheduledOneIsNot()
    {
        await using ServiceProvider container = BuildContainer();
        IScheduler scheduler = await Start(container);

        await RunNow<RecordingJob>(scheduler, "manual");

        IJobDetail scheduled = JobBuilder.Create<RecordingJob>().WithIdentity("scheduled", "plugin").Build();
        await scheduler.ScheduleJob(scheduled, TriggerBuilder.Create().ForJob(scheduled).StartNow().Build());

        List<ExecutionHistoryEntry> rows = await Rows(container, scheduler.SchedulerName, 2);

        ExecutionHistoryEntry manual = rows.Single(row => row.JobName == "manual");
        manual.Manual.Should().BeTrue("TriggerJob marks the trigger it fires, and the recorder reads the mark");
        manual.FireInstanceId.Should().NotBeNullOrEmpty()
            .And.Be(RecordingJob.FireInstanceIds["manual"], "the row links to the firing's span and log scope by it");

        ExecutionHistoryEntry fromSchedule = rows.Single(row => row.JobName == "scheduled");
        fromSchedule.Manual.Should().BeFalse("a trigger the application scheduled carries no mark");
        fromSchedule.FireInstanceId.Should().Be(RecordingJob.FireInstanceIds["scheduled"]);

        await scheduler.Shutdown(waitForJobsToComplete: true);
    }

    [Test]
    public async Task TheManualMarkIsVisibleToTheJob()
    {
        await using ServiceProvider container = BuildContainer();
        IScheduler scheduler = await Start(container);

        await RunNow<RecordingJob>(scheduler, "visible");
        await Rows(container, scheduler.SchedulerName, 1);

        RecordingJob.ManualMarks["visible"].Should().Be("true",
            "the mark is an ordinary entry on the trigger's map, so it reads through MergedJobDataMap");

        await scheduler.Shutdown(waitForJobsToComplete: true);
    }

    [Test]
    public async Task AVetoedFiringOnARunningSchedulerIsInTheMisfireFeed()
    {
        await using ServiceProvider container = BuildContainer();
        IScheduler scheduler = await Start(container);
        scheduler.ListenerManager.AddTriggerListener(new VetoingListener());

        await RunNow<RecordingJob>(scheduler, "vetoed");

        IExecutionHistoryStore store = container.GetRequiredService<IExecutionHistoryStore>();
        MisfireHistoryEntry row = await Eventually(async () =>
            (await store.QueryMisfires(new MisfireHistoryQuery { SchedulerName = scheduler.SchedulerName })).Items.FirstOrDefault());

        row.Reason.Should().Be(MisfireReason.Vetoed);
        row.JobKey.Should().Be(new JobKey("vetoed", "plugin"));

        await scheduler.Shutdown(waitForJobsToComplete: true);

        (await store.QueryExecutions(new ExecutionHistoryQuery { SchedulerName = scheduler.SchedulerName })).Items.Should().BeEmpty();
    }

    [Test]
    public void AManualMarkIsReadAsATrueFlag()
    {
        ExecutionHistoryPlugin.IsManual(TriggerWith(null)).Should().BeFalse();
        ExecutionHistoryPlugin.IsManual(TriggerWith("true")).Should().BeTrue();
        ExecutionHistoryPlugin.IsManual(TriggerWith("True")).Should().BeTrue();
        ExecutionHistoryPlugin.IsManual(TriggerWith(true)).Should().BeTrue("a serializer that typed the value keeps it a mark");
        ExecutionHistoryPlugin.IsManual(TriggerWith("false")).Should().BeFalse();
        ExecutionHistoryPlugin.IsManual(TriggerWith(1)).Should().BeFalse("only a flag is a mark");

        static ITrigger TriggerWith(object? value)
        {
            ITrigger trigger = TriggerBuilder.Create().WithIdentity("t").ForJob("j").Build();
            if (value is not null)
            {
                trigger.JobDataMap[SchedulerConstants.ManualTrigger] = value;
            }

            return trigger;
        }
    }

    // ---------------------------------------------------------------------------------------------

    private static InMemoryExecutionHistoryStore Store() => new(Options.Create(new ExecutionHistoryOptions()), new FakeTimeProvider(now));

    private static ServiceProvider Provider(IExecutionHistoryStore store)
    {
        ServiceCollection services = new();
        services.AddSingleton(store);
        return services.BuildServiceProvider();
    }

    private static JobExecutionContextImpl Context(DateTimeOffset? scheduledAt = null)
    {
        IScheduler scheduler = A.Fake<IScheduler>();
        A.CallTo(() => scheduler.SchedulerName).Returns(SchedulerName);
        A.CallTo(() => scheduler.SchedulerInstanceId).Returns("node-a");

        return JobExecutionContextBuilder.For(new RecordingJob())
            .WithJob(JobBuilder.Create<RecordingJob>().WithIdentity("reconcile", "billing").Build())
            .WithScheduler(scheduler)
            .FiredAt(now, scheduledAt)
            .Build();
    }

    private static async Task<ExecutionHistoryEntry> Record(IJobExecutionContext context, JobExecutionException? jobException = null)
    {
        InMemoryExecutionHistoryStore store = Store();
        await using ServiceProvider provider = Provider(store);

        await new ExecutionHistoryPlugin(provider, new FakeTimeProvider(now)).JobWasExecuted(context, jobException);

        return (await store.QueryExecutions(new ExecutionHistoryQuery { SchedulerName = SchedulerName }))
            .Items.Should().ContainSingle().Subject;
    }

    private static ServiceProvider BuildContainer()
    {
        ServiceCollection services = new();
        services.AddLogging();
        services.AddQuartzExecutionHistory();
        services.AddQuartz(q => q.ConfigureScheduler(options =>
        {
            options.InstanceName = "plugin-" + Guid.NewGuid().ToString("N");
            options.InstanceId = "one";
        }));

        return services.BuildServiceProvider();
    }

    private static async Task<IScheduler> Start(ServiceProvider container)
    {
        IScheduler scheduler = await container.GetRequiredService<ISchedulerFactory>().GetScheduler();
        await scheduler.Start();
        return scheduler;
    }

    private static async Task RunNow<TJob>(IScheduler scheduler, string name) where TJob : IJob
    {
        JobKey key = new(name, "plugin");
        await scheduler.AddJob(JobBuilder.Create<TJob>().WithIdentity(key).StoreDurably().Build());
        await scheduler.TriggerJob(key);
    }

    private static Task<List<ExecutionHistoryEntry>> Rows(ServiceProvider container, string schedulerName, int count)
    {
        IExecutionHistoryStore store = container.GetRequiredService<IExecutionHistoryStore>();

        return Eventually(async () =>
        {
            PagedResult<ExecutionHistoryEntry> page = await store.QueryExecutions(new ExecutionHistoryQuery { SchedulerName = schedulerName });
            return page.Items.Count >= count ? page.Items.ToList() : null;
        });
    }

    private static async Task<T> Eventually<T>(Func<Task<T?>> read) where T : class
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow + waitLimit;

        while (true)
        {
            if (await read() is { } found)
            {
                return found;
            }

            DateTimeOffset.UtcNow.Should().BeBefore(deadline, "the recorder writes as the firing completes");
            await Task.Delay(20);
        }
    }

    private sealed class OwnReport : IJobRunReport
    {
        public JobRunResult Result => JobRunResult.Skipped;

        public string? Summary => "own";

        public IReadOnlyDictionary<string, object?>? Metrics { get; } = new Dictionary<string, object?> { ["n"] = 1 };
    }

    public sealed class RecordingJob : IJob
    {
        public static ConcurrentDictionary<string, string> FireInstanceIds { get; } = new();

        public static ConcurrentDictionary<string, string?> ManualMarks { get; } = new();

        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
        {
            FireInstanceIds[context.JobDetail.Key.Name] = context.FireInstanceId;
            ManualMarks[context.JobDetail.Key.Name] = context.MergedJobDataMap.GetString(SchedulerConstants.ManualTrigger);
            return default;
        }
    }

    /// <summary>Throws an exception of its own, which the run shell wraps.</summary>
    public sealed class ThrowingJob : IJob
    {
        public const string Message = "the upstream system is down";

        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException(Message);
        }
    }

    /// <summary>Throws a <see cref="JobExecutionException" /> of its own, with its own words over a cause.</summary>
    public sealed class RefusingJob : IJob
    {
        public const string Message = "quota exceeded";

        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
        {
            throw new JobExecutionException(Message, new InvalidOperationException("HTTP 429"));
        }
    }

    /// <summary>
    /// Interrupts its own firing and then waits on the token: the shape of every job that is stopped
    /// rather than finished.
    /// </summary>
    public sealed class SelfInterruptingJob : IJob
    {
        public async ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
        {
            await context.Scheduler.InterruptFireInstance(context.FireInstanceId, CancellationToken.None).ConfigureAwait(false);
            await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
        }
    }

    private sealed class VetoingListener : ITriggerListener
    {
        public string Name => "vetoing";

        public ValueTask<bool> VetoJobExecution(ITrigger trigger, IJobExecutionContext context, CancellationToken cancellationToken = default)
        {
            return new ValueTask<bool>(true);
        }
    }
}
