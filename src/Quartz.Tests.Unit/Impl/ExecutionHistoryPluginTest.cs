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

using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

using Quartz.Core;
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

    /// <summary>
    /// The recorder's manual flag and the firing's <see cref="FiringOrigin" /> are one reading of the mark
    /// (#4246): a run asked for with <c>TriggerJob</c> is both, and a scheduled one is neither.
    /// </summary>
    [Test]
    public async Task AManualRunIsRecordedAsManualAndItsOccurrenceSaysSoToo()
    {
        await using ServiceProvider container = BuildContainer();
        IScheduler scheduler = await Start(container);

        await RunNow<RecordingJob>(scheduler, "asked-for");

        IJobDetail scheduled = JobBuilder.Create<RecordingJob>().WithIdentity("on-schedule", "plugin").Build();
        await scheduler.ScheduleJob(scheduled, TriggerBuilder.Create().ForJob(scheduled).StartNow().Build());

        List<ExecutionHistoryEntry> rows = await Rows(container, scheduler.SchedulerName, 2);
        await scheduler.Shutdown(waitForJobsToComplete: true);

        rows.Single(row => row.JobName == "asked-for").Manual.Should().BeTrue("TriggerJob marks the trigger, and the recorder reads the mark");
        RecordingJob.Origins["asked-for"].Should().Be(FiringOrigin.Manual, "the job reads the same mark the recorder does");

        rows.Single(row => row.JobName == "on-schedule").Manual.Should().BeFalse("a trigger the application scheduled carries no mark");
        RecordingJob.Origins["on-schedule"].Should().Be(FiringOrigin.Schedule);
    }

    /// <summary>
    /// A scheduled run records when it was due, how late it started and why it ran: the occurrence it
    /// filled, the delay the run shell measured from it, and the schedule as its origin.
    /// </summary>
    [Test]
    public async Task AScheduledRunRecordsItsDueTimeItsDelayAndItsOrigin()
    {
        await using ServiceProvider container = BuildContainer();
        IScheduler scheduler = await Start(container);

        DateTimeOffset scheduledAt = DateTimeOffset.UtcNow;
        IJobDetail job = JobBuilder.Create<RecordingJob>().WithIdentity("on-time", "plugin").Build();
        await scheduler.ScheduleJob(job, TriggerBuilder.Create().ForJob(job).StartAt(scheduledAt).Build());

        ExecutionHistoryEntry row = (await Rows(container, scheduler.SchedulerName, 1)).Single();
        await scheduler.Shutdown(waitForJobsToComplete: true);

        row.DueTimeUtc.Should().BeCloseTo(scheduledAt, TimeSpan.FromSeconds(1),
            "the run was due when its trigger said, not when the thread got to it");
        row.StartDelay.Should().NotBeNull("the shell measures the delay of every first firing");
        row.StartDelay!.Value.Should().BeGreaterThanOrEqualTo(TimeSpan.Zero)
            .And.BeLessThan(TimeSpan.FromSeconds(30), "a run that started half a minute late is not on time");
        row.Origin.Should().Be(FiringOrigin.Schedule, "a trigger the application scheduled fires on its schedule");
        row.Manual.Should().BeFalse();
    }

    /// <summary>
    /// A run asked for with <c>TriggerJob</c> records its origin as manual, and its due time is when it
    /// was asked for.
    /// </summary>
    [Test]
    public async Task AManualRunRecordsItsOriginAndItsDueTime()
    {
        await using ServiceProvider container = BuildContainer();
        IScheduler scheduler = await Start(container);

        DateTimeOffset askedAt = DateTimeOffset.UtcNow;
        await RunNow<RecordingJob>(scheduler, "asked-for");

        ExecutionHistoryEntry row = (await Rows(container, scheduler.SchedulerName, 1)).Single();
        await scheduler.Shutdown(waitForJobsToComplete: true);

        row.Origin.Should().Be(FiringOrigin.Manual, "TriggerJob marks the trigger, and the recorder reads the mark");
        row.DueTimeUtc.Should().BeCloseTo(askedAt, TimeSpan.FromSeconds(5),
            "a manual run is due when it was asked for, not at some earlier scheduled time");
        row.StartDelay.Should().NotBeNull("the shell measures the delay of every first firing");
        row.StartDelay!.Value.Should().BeGreaterThanOrEqualTo(TimeSpan.Zero);
    }

    /// <summary>
    /// A job that throws records the type of what it threw, so failures can be grouped by cause.
    /// </summary>
    [Test]
    public async Task AJobThatThrowsRecordsTheTypeItThrew()
    {
        await using ServiceProvider container = BuildContainer();
        IScheduler scheduler = await Start(container);

        await RunNow<ThrowingJob>(scheduler, "throws");

        ExecutionHistoryEntry row = (await Rows(container, scheduler.SchedulerName, 1)).Single();
        await scheduler.Shutdown(waitForJobsToComplete: true);

        row.ExceptionType.Should().Be(typeof(InvalidOperationException).FullName,
            "the run shell wraps what the job threw twice, and the wrapper's type names nothing");
    }

    /// <summary>
    /// A <see cref="JobExecutionException" /> the job threw itself is recorded under its own type, not its
    /// cause's: the job chose it to say what failed.
    /// </summary>
    [Test]
    public async Task AJobExecutionExceptionTheJobThrewIsRecordedUnderItsOwnType()
    {
        await using ServiceProvider container = BuildContainer();
        IScheduler scheduler = await Start(container);

        await RunNow<RefusingJob>(scheduler, "refuses");

        ExecutionHistoryEntry row = (await Rows(container, scheduler.SchedulerName, 1)).Single();
        await scheduler.Shutdown(waitForJobsToComplete: true);

        row.ExceptionType.Should().Be(typeof(JobExecutionException).FullName,
            "the job chose JobExecutionException to say it, and its cause is a detail of that, not the failure");
    }

    /// <summary>
    /// A context records its occurrence as its due time and its origin, and no delay when nothing measured one.
    /// </summary>
    [Test]
    public async Task AContextRecordsItsOccurrenceAsItsDueTimeAndOrigin()
    {
        JobExecutionContextImpl context = Context(scheduledAt: now.AddSeconds(-5));
        context.Settle(ExecutionOutcome.Succeeded, retryScheduled: false);

        ExecutionHistoryEntry row = await Record(context);

        row.DueTimeUtc.Should().Be(now.AddSeconds(-5), "the run was due when its trigger said");
        row.Origin.Should().Be(FiringOrigin.Schedule);
        row.StartDelay.Should().BeNull("nothing measured a delay for a context built by hand");
    }

    [Test]
    public void AManualMarkIsReadAsATrueFlag()
    {
        ManualFiring.IsMarked(TriggerWith(null)).Should().BeFalse();
        ManualFiring.IsMarked(TriggerWith("true")).Should().BeTrue();
        ManualFiring.IsMarked(TriggerWith("True")).Should().BeTrue();
        ManualFiring.IsMarked(TriggerWith(true)).Should().BeTrue("a serializer that typed the value keeps it a mark");
        ManualFiring.IsMarked(TriggerWith("false")).Should().BeFalse();
        ManualFiring.IsMarked(TriggerWith(1)).Should().BeFalse("only a flag is a mark");

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
    // The run's input
    // ---------------------------------------------------------------------------------------------

    [Test]
    public async Task TheInputIsNotRecordedUnlessAskedFor()
    {
        ExecutionHistoryEntry row = await Record(ContextWithInput(onTrigger: "{\"invoiceId\":42}"), new ExecutionHistoryOptions());

        row.Input.Should().BeNull("an input can hold secrets, so the history keeps none until it is told to");
        row.InputTooLarge.Should().BeFalse("nothing was refused, because nothing was asked for");
    }

    [Test]
    public async Task TheInputIsRecordedWhenAskedFor()
    {
        ExecutionHistoryEntry row = await Record(
            ContextWithInput(onTrigger: "{\"invoiceId\":42}"),
            new ExecutionHistoryOptions { RecordInput = true });

        row.Input.Should().Be("{\"invoiceId\":42}", "the input is the string the scheduler stored, recorded as it is");
        row.InputTooLarge.Should().BeFalse();
    }

    [Test]
    public async Task TheTriggersInputIsRecordedOverTheJobs()
    {
        ExecutionHistoryEntry row = await Record(
            ContextWithInput(onTrigger: "\"from the trigger\"", onJob: "\"from the job\""),
            new ExecutionHistoryOptions { RecordInput = true });

        row.Input.Should().Be("\"from the trigger\"", "the run had the merged map's input, where the trigger's wins");
    }

    [Test]
    public async Task AnInputOverTheCapIsNotRecordedAndTheRowSaysSo()
    {
        ExecutionHistoryOptions bounds = new() { RecordInput = true, MaxInputBytes = 4 };

        ExecutionHistoryEntry fits = await Record(ContextWithInput(onTrigger: "éé"), bounds);
        fits.Input.Should().Be("éé", "two characters of two bytes each are exactly the cap");
        fits.InputTooLarge.Should().BeFalse();

        ExecutionHistoryEntry over = await Record(ContextWithInput(onTrigger: "ééé"), bounds);
        over.Input.Should().BeNull("a cut input is a different input, so none is kept: the cap counts bytes, not characters");
        over.InputTooLarge.Should().BeTrue("Run again has to be able to say why it has no input");
    }

    [Test]
    public async Task ARunWithNoInputOrOneThatIsNotAStringRecordsNone()
    {
        ExecutionHistoryOptions bounds = new() { RecordInput = true };

        ExecutionHistoryEntry none = await Record(ContextWithInput(onTrigger: null), bounds);
        none.Input.Should().BeNull();
        none.InputTooLarge.Should().BeFalse();

        ExecutionHistoryEntry raw = await Record(ContextWithInput(onTrigger: 42), bounds);
        raw.Input.Should().BeNull("the scheduler stores every input as a string, and only a context built by hand holds anything else");
        raw.InputTooLarge.Should().BeFalse();
    }

    /// <summary>
    /// A typed one-off's input is recorded, and handing it back to <c>TriggerJob</c> gives the job the same
    /// input again: the round trip the dashboard's Run again makes.
    /// </summary>
    [Test]
    public async Task ARecordedInputPassedBackToTriggerJobRunsTheJobWithTheSameInput()
    {
        InputJob.Received.Clear();
        await using ServiceProvider container = BuildContainer(options => options.RecordInput = true);
        IScheduler scheduler = await Start(container);

        await scheduler.ScheduleJob<InputJob, Invoice>(new Invoice(42, "café"), TimeSpan.Zero);

        ExecutionHistoryEntry first = (await Rows(container, scheduler.SchedulerName, 1)).Single();
        first.Input.Should().NotBeNull("the history records inputs, and the one-off carried one on its trigger");

        await scheduler.TriggerJob(
            new JobKey(first.JobName, first.JobGroup),
            new JobDataMap { [SchedulerConstants.JobInput] = first.Input! });

        List<ExecutionHistoryEntry> rows = await Rows(container, scheduler.SchedulerName, 2);
        await scheduler.Shutdown(waitForJobsToComplete: true);

        InputJob.Received.Should().Equal([new Invoice(42, "café"), new Invoice(42, "café")],
            "the recorded string is what the scheduler stored, so it reads back as the same input");
        rows.Should().OnlyContain(row => row.Input == first.Input, "the second run had the same input, and recorded it");
    }

    /// <summary>
    /// The database history writes <c>JOB_INPUT</c> only when told to record inputs, and <c>NULL</c> otherwise.
    /// </summary>
    [TestCase(false)]
    [TestCase(true)]
    public async Task ThePersistentHistoryWritesTheInputOnlyWhenAskedFor(bool recordInput)
    {
        InputJob.Received.Clear();
        using SqliteTestDatabase database = new("history-input");
        string connectionString = database.ConnectionString;

        ServiceCollection services = new();
        services.AddLogging();
        services.AddQuartzExecutionHistory(options => options.RecordInput = recordInput);
        services.AddQuartz(q =>
        {
            q.ConfigureScheduler(options =>
            {
                options.InstanceName = "input-" + Guid.NewGuid().ToString("N");
                options.InstanceId = "one";
            });

            q.UsePersistentStore(store =>
            {
                store.UseSqlite(SqliteFactory.Instance, connectionString);
                store.ProvisionSchema();
                store.UseExecutionHistory();
            });
        });

        await using ServiceProvider container = services.BuildServiceProvider();
        IScheduler scheduler = await Start(container);

        await scheduler.ScheduleJob<InputJob, Invoice>(new Invoice(7, "kept in the database"), TimeSpan.Zero);

        ExecutionHistoryEntry listed = (await Rows(container, scheduler.SchedulerName, 1)).Single();
        await scheduler.Shutdown(waitForJobsToComplete: true);

        ExecutionHistoryEntry read = (await container.GetRequiredService<IExecutionHistoryStore>()
            .GetExecution(scheduler.SchedulerName, listed.EntryId!))!;

        await using SqliteConnection connection = new(connectionString);
        await connection.OpenAsync();
        await using SqliteCommand select = connection.CreateCommand();
        select.CommandText = "SELECT JOB_INPUT FROM QRTZ_EXECUTION_HISTORY WHERE ENTRY_ID = @entryId";
        select.Parameters.AddWithValue("@entryId", listed.EntryId);
        object? stored = await select.ExecuteScalarAsync();

        if (recordInput)
        {
            read.Input.Should().Contain("kept in the database", "the single read carries the input the run had");
            stored.Should().Be(read.Input, "and it is kept as the scheduler stored it, in the row's own column");
        }
        else
        {
            read.Input.Should().BeNull();
            stored.Should().Be(DBNull.Value, "the history keeps no input until it is told to, so the column is NULL");
        }
    }

    // ---------------------------------------------------------------------------------------------
    // Whether the firing finalized its trigger
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// A store that keeps trigger statuses is told, with each execution, whether the trigger may fire again: a
    /// one-off's only firing and the last of a series finalize it; a retried attempt and a trigger that measures
    /// its interval from completion do not.
    /// </summary>
    /// <remarks>
    /// On a running scheduler, because the answer is read off the trigger after the run shell has completed it,
    /// which a hand-built context cannot show.
    /// </remarks>
    [Test]
    public async Task TheRecorderSaysWhetherTheFiringFinalizedItsTrigger()
    {
        FinalizationRecordingStore recording = new();
        ServiceCollection services = new();
        services.AddLogging();
        services.AddSingleton<IExecutionHistoryStore>(recording);
        services.AddQuartzExecutionHistory();
        services.AddQuartz(q => q.ConfigureScheduler(options =>
        {
            options.InstanceName = "finalized-" + Guid.NewGuid().ToString("N");
            options.InstanceId = "one";
        }));

        await using ServiceProvider container = services.BuildServiceProvider();
        IScheduler scheduler = await Start(container);

        IJobDetail succeeding = JobBuilder.Create<RecordingJob>().WithIdentity("succeeding", "plugin").StoreDurably().Build();
        IJobDetail throwing = JobBuilder.Create<ThrowingJob>().WithIdentity("throwing", "plugin").StoreDurably().Build();
        await scheduler.AddJob(succeeding);
        await scheduler.AddJob(throwing);

        TimeSpan interval = TimeSpan.FromMilliseconds(50);
        await scheduler.ScheduleJob(TriggerBuilder.Create().WithIdentity("one-off", "plugin").ForJob(succeeding).StartNow().Build());
        await scheduler.ScheduleJob(TriggerBuilder.Create().WithIdentity("series", "plugin").ForJob(succeeding).StartNow()
            .WithSimpleSchedule(x => x.WithInterval(interval).WithRepeatCount(2)).Build());
        await scheduler.ScheduleJob(TriggerBuilder.Create().WithIdentity("retried", "plugin").ForJob(throwing).StartNow()
            .WithRetryPolicy(RetryPolicy.Fixed(1, interval)).Build());
        await scheduler.ScheduleJob(TriggerBuilder.Create().WithIdentity("fixed-delay", "plugin").ForJob(succeeding).StartNow()
            .WithSimpleSchedule(x => x.WithInterval(interval).RepeatForever().MeasureIntervalFromCompletion()).Build());

        await Eventually(() => Task.FromResult(
            recording.Flags("one-off").Count >= 1
            && recording.Flags("series").Count >= 3
            && recording.Flags("retried").Count >= 2
            && recording.Flags("fixed-delay").Count >= 2
                ? "recorded"
                : null));

        await scheduler.Shutdown(waitForJobsToComplete: true);

        recording.Flags("one-off").Should().Equal([true], "a one-off's only firing leaves its trigger nothing to fire");
        recording.Flags("series").Should().Equal([false, false, true],
            "a series is finalized by its last firing alone");
        recording.Flags("retried").Should().Equal([false, true],
            "a retry sets the trigger's next fire time before the listeners are told, so the first attempt keeps it");
        recording.Flags("fixed-delay").Should().AllSatisfy(finalized => finalized.Should().Be(false,
            "a trigger that measures from completion holds a provisional next fire time while its firing runs"));
    }

    /// <summary>
    /// The history store <see cref="TheRecorderSaysWhetherTheFiringFinalizedItsTrigger" /> reads: it records what
    /// the recorder told it, by trigger, in fire order.
    /// </summary>
    private sealed class FinalizationRecordingStore : IExecutionHistoryStore, ITriggerStatusHistoryStore
    {
        private readonly ConcurrentQueue<(ExecutionHistoryEntry Entry, bool? Finalized)> recorded = new();

        /// <summary>What the recorder said of each firing of the trigger; <see langword="null" /> where it said nothing.</summary>
        public List<bool?> Flags(string triggerName) => recorded
            .Where(x => x.Entry.TriggerName == triggerName)
            .OrderBy(x => x.Entry.FiredAtUtc)
            .Select(x => x.Finalized)
            .ToList();

        public ValueTask AddExecution(ExecutionHistoryEntry entry, bool triggerFinalized, CancellationToken cancellationToken = default)
        {
            recorded.Enqueue((entry, triggerFinalized));
            return default;
        }

        public ValueTask AddExecution(ExecutionHistoryEntry entry, CancellationToken cancellationToken = default)
        {
            recorded.Enqueue((entry, null));
            return default;
        }

        public ValueTask<PagedResult<ExecutionHistoryEntry>> QueryExecutions(ExecutionHistoryQuery query, CancellationToken cancellationToken = default)
        {
            return new ValueTask<PagedResult<ExecutionHistoryEntry>>(new PagedResult<ExecutionHistoryEntry>([], HasMore: false));
        }

        public ValueTask AddMisfire(MisfireHistoryEntry entry, CancellationToken cancellationToken = default) => default;

        public ValueTask<PagedResult<MisfireHistoryEntry>> QueryMisfires(MisfireHistoryQuery query, CancellationToken cancellationToken = default)
        {
            return new ValueTask<PagedResult<MisfireHistoryEntry>>(new PagedResult<MisfireHistoryEntry>([], HasMore: false));
        }

        public ValueTask<int> CountMisfires(string schedulerName, DateTimeOffset since, CancellationToken cancellationToken = default)
        {
            return new ValueTask<int>(0);
        }
    }

    // ---------------------------------------------------------------------------------------------

    private static InMemoryExecutionHistoryStore Store() => new(Options.Create(new ExecutionHistoryOptions()), new FakeTimeProvider(now));

    /// <summary>
    /// A firing whose trigger, and optionally whose job, carry <paramref name="onTrigger" /> and
    /// <paramref name="onJob" /> under <see cref="SchedulerConstants.JobInput" />.
    /// </summary>
    private static JobExecutionContextImpl ContextWithInput(object? onTrigger, object? onJob = null)
    {
        IScheduler scheduler = A.Fake<IScheduler>();
        A.CallTo(() => scheduler.SchedulerName).Returns(SchedulerName);
        A.CallTo(() => scheduler.SchedulerInstanceId).Returns("node-a");

        IJobDetail job = JobBuilder.Create<RecordingJob>().WithIdentity("reconcile", "billing").Build();
        if (onJob is not null)
        {
            job.JobDataMap[SchedulerConstants.JobInput] = onJob;
        }

        ITrigger trigger = TriggerBuilder.Create().ForJob(job).StartAt(now).Build();
        if (onTrigger is not null)
        {
            trigger.JobDataMap[SchedulerConstants.JobInput] = onTrigger;
        }

        return JobExecutionContextBuilder.For(new RecordingJob())
            .WithJob(job)
            .WithTrigger(trigger)
            .WithScheduler(scheduler)
            .FiredAt(now)
            .Build();
    }

    private static async Task<ExecutionHistoryEntry> Record(IJobExecutionContext context, ExecutionHistoryOptions bounds)
    {
        InMemoryExecutionHistoryStore store = Store();
        ServiceCollection services = new();
        services.AddSingleton<IExecutionHistoryStore>(store);
        services.AddSingleton(Options.Create(bounds));
        await using ServiceProvider provider = services.BuildServiceProvider();

        await new ExecutionHistoryPlugin(provider, new FakeTimeProvider(now)).JobWasExecuted(context, null);

        return (await store.QueryExecutions(new ExecutionHistoryQuery { SchedulerName = SchedulerName }))
            .Items.Should().ContainSingle().Subject;
    }

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

    private static ServiceProvider BuildContainer(Action<ExecutionHistoryOptions>? configure = null)
    {
        ServiceCollection services = new();
        services.AddLogging();
        services.AddQuartzExecutionHistory(configure);
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

    /// <summary>The input a typed one-off of <see cref="InputJob" /> carries.</summary>
    public sealed record Invoice(int InvoiceId, string Note);

    /// <summary>Records every input it was run with.</summary>
    public sealed class InputJob : IJob<Invoice>
    {
        public static ConcurrentQueue<Invoice> Received { get; } = new();

        public ValueTask Execute(IJobExecutionContext context, Invoice input, CancellationToken cancellationToken = default)
        {
            Received.Enqueue(input);
            return default;
        }
    }

    public sealed class RecordingJob : IJob
    {
        public static ConcurrentDictionary<string, string> FireInstanceIds { get; } = new();

        public static ConcurrentDictionary<string, string?> ManualMarks { get; } = new();

        public static ConcurrentDictionary<string, FiringOrigin> Origins { get; } = new();

        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
        {
            FireInstanceIds[context.JobDetail.Key.Name] = context.FireInstanceId;
            ManualMarks[context.JobDetail.Key.Name] = context.MergedJobDataMap.GetString(SchedulerConstants.ManualTrigger);
            Origins[context.JobDetail.Key.Name] = context.GetOccurrence().Origin;
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
