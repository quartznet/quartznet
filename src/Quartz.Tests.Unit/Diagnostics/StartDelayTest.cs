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

using System.Diagnostics;
using System.Globalization;
using System.Reflection;

using FakeItEasy;

using Microsoft.Extensions.Diagnostics.Metrics.Testing;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

using Quartz.Core;
using Quartz.Diagnostics;
using Quartz.Extensibility;
using Quartz.Impl;
using Quartz.Impl.AdoJobStore;
using Quartz.Tests.Impl;

namespace Quartz.Tests.Unit.Diagnostics;

/// <summary>
/// <c>quartz.job.execution.start_delay</c>: how late a firing's job started against the time its attempt was
/// due, and the two attributes the <c>Quartz.Job.Execute</c> span gained beside it (#4242).
/// </summary>
/// <remarks>
/// <para>
/// Every case runs on the in-memory store and on a real SQLite one, on a clock the test moves, and runs the
/// firing through the run shell the scheduler's loop would hand it to. Nothing reads real time, so every
/// expected value is exact.
/// </para>
/// <para>
/// Each case has a meter of its own, so nothing another fixture runs is collected here.
/// </para>
/// </remarks>
[TestFixture]
[NonParallelizable]
public sealed class StartDelayTest : MisfireThroughAStoreTestBase
{
    // Spelled out rather than read from the constants the product publishes them from: the wire name is
    // the contract a dashboard is written against, and reading both sides from one constant would let a
    // rename pass unnoticed.
    private const string StartDelay = "quartz.job.execution.start_delay";
    private const string ExecuteDuration = "quartz.job.execution.duration";
    private const string OriginTag = "quartz.firing.origin";
    private const string ScheduledFireTimeTag = "quartz.trigger.scheduled_fire_time";

    private static readonly TimeSpan RetryDelay = TimeSpan.FromMinutes(5);

    [Test]
    public async Task AScheduledFiringThatStartsLateRecordsHowLate()
    {
        foreach (MisfireStoreUnderTest store in await BothStores(Anchor()))
        {
            using Harness harness = new(store);
            DateTimeOffset due = store.Clock.GetUtcNow();
            IOperableTrigger trigger = await StoreHourly<NoOpJob>(store, due);

            // Late at both ends: the store fires it three seconds after it was due, and the job starts four
            // seconds after that, as it would behind a full thread pool.
            store.Clock.Advance(TimeSpan.FromSeconds(3));
            TriggerFiredBundle bundle = await Fire(store, trigger.Key);
            store.Clock.Advance(TimeSpan.FromSeconds(4));
            await harness.Run(bundle);

            CollectedMeasurement<double> measurement = harness.StartDelays().Should().ContainSingle("{0}: one firing, one measurement", store.Name).Subject;
            measurement.Value.Should().Be(7, "{0}: the job started seven seconds after its trigger was due", store.Name);
            measurement.Tags.Should().BeEquivalentTo(new Dictionary<string, object>
                {
                    ["quartz.scheduler.name"] = harness.SchedulerName,
                    ["quartz.scheduler.id"] = Harness.InstanceId,
                    ["quartz.trigger.group"] = trigger.Key.Group,
                    ["quartz.trigger.name"] = trigger.Key.Name,
                    ["quartz.job.group"] = trigger.JobKey.Group,
                    ["quartz.job.name"] = trigger.JobKey.Name,
                    [OriginTag] = "schedule",
                },
                "{0}: the duration histogram's identity attributes, and why the firing ran", store.Name);
        }
    }

    [Test]
    public async Task AFiringStartedBeforeItWasDueRecordsZero()
    {
        foreach (MisfireStoreUnderTest store in await BothStores(Anchor()))
        {
            using Harness harness = new(store);
            DateTimeOffset now = store.Clock.GetUtcNow();
            IOperableTrigger trigger = await StoreHourly<NoOpJob>(store, now.AddSeconds(5));

            // Fired now, five seconds ahead of its time, as BatchTriggerAcquisitionFireAheadTimeWindow lets the
            // loop do.
            await harness.Run(await Fire(store, trigger.Key));

            harness.StartDelays().Should().ContainSingle().Which.Value.Should().Be(0,
                "{0}: a firing that started early was not late, and a negative delay would drag every percentile down", store.Name);
        }
    }

    [Test]
    public async Task ARetryIsMeasuredFromItsOwnDueTimeNotTheOccurrence()
    {
        foreach (MisfireStoreUnderTest store in await BothStores(Anchor()))
        {
            using Harness harness = new(store);
            DateTimeOffset occurrence = store.Clock.GetUtcNow();
            IOperableTrigger trigger = await StoreHourly<FailsFirstAttemptJob>(store, occurrence, RetryPolicy.Fixed(2, RetryDelay));

            await harness.Run(await Fire(store, trigger.Key));

            // The retry is due five minutes on; it starts two seconds after that.
            store.Clock.Advance(RetryDelay + TimeSpan.FromSeconds(2));
            TriggerFiredBundle retry = await Fire(store, trigger.Key);
            retry.Trigger.RetryAttempt.Should().Be(1, "{0}: the premise: this firing is the retry", store.Name);
            retry.ScheduledFireTimeUtc.Should().Be(occurrence, "{0}: the premise: ScheduledFireTimeUtc stays on the occurrence", store.Name);
            await harness.Run(retry);

            harness.StartDelays().Select(x => x.Value).Should().Equal([0, 2],
                "{0}: the retry was two seconds late; measured from the occurrence it would report the backoff as lateness", store.Name);
        }
    }

    [Test]
    public async Task ABackfillIsMeasuredFromWhenTheBackfillStartedItNotFromItsSlot()
    {
        foreach (MisfireStoreUnderTest store in await BothStores(Anchor()))
        {
            using Harness harness = new(store);
            DateTimeOffset now = store.Clock.GetUtcNow();
            IOperableTrigger hourly = await StoreHourly<NoOpJob>(store, now.AddHours(1), startUtc: now.AddDays(-1));

            BackfillResult backfill = await harness.Scheduler.Backfill(hourly.Key, now.AddHours(-2), now.AddHours(-1));
            backfill.FirstSlot.Should().Be(now.AddHours(-2), "{0}: the premise: one slot, two hours back", store.Name);

            TriggerFiredBundle bundle = await Fire(store, backfill.ScheduledTriggers.Should().ContainSingle().Which);
            store.Clock.Advance(TimeSpan.FromSeconds(2));
            await harness.Run(bundle);

            CollectedMeasurement<double> measurement = harness.StartDelays().Should().ContainSingle().Subject;
            measurement.Value.Should().Be(2,
                "{0}: the backfill started the firing now, and the job ran two seconds later; the slot is in the past by design", store.Name);
            measurement.Tags.Should().Contain(OriginTag, "backfill");
        }
    }

    [Test]
    public async Task AManualFiringIsTaggedManual()
    {
        foreach (MisfireStoreUnderTest store in await BothStores(Anchor()))
        {
            using Harness harness = new(store);
            JobKey jobKey = new("manual-" + Guid.NewGuid().ToString("N"), Group);
            await harness.Scheduler.AddJob(JobBuilder.Create<NoOpJob>().WithIdentity(jobKey).StoreDurably().Build());
            await harness.Scheduler.TriggerJob(jobKey);

            TriggerKey manual = (await store.Store.GetTriggersForJob(jobKey)).Should().ContainSingle().Which.Key;
            TriggerFiredBundle bundle = await Fire(store, manual);
            store.Clock.Advance(TimeSpan.FromSeconds(1));
            await harness.Run(bundle);

            CollectedMeasurement<double> measurement = harness.StartDelays().Should().ContainSingle().Subject;
            measurement.Value.Should().Be(1, "{0}: a manual run is due when it is asked for", store.Name);
            measurement.Tags.Should().Contain(OriginTag, "manual",
                "a manual run's delay is near zero by construction, and the tag is how an alert leaves it out");
        }
    }

    [Test]
    public async Task ARefireIsNotMeasuredAgain()
    {
        foreach (MisfireStoreUnderTest store in await BothStores(Anchor()))
        {
            using Harness harness = new(store);
            IOperableTrigger trigger = await StoreHourly<RefiresOnceJob>(store, store.Clock.GetUtcNow());

            await harness.Run(await Fire(store, trigger.Key));

            harness.Durations().Should().HaveCount(2, "{0}: the premise: the job ran twice in the one firing", store.Name);
            harness.StartDelays().Should().ContainSingle(
                "{0}: a refire runs the same firing again at once, and measured from the same due time it would report the first run as lateness", store.Name);
        }
    }

    /// <summary>
    /// A catch-up is due when its misfire policy said, which for <c>FireNow</c> is the misfire pass that found
    /// it. How much it missed is <c>quartz.trigger.misfire</c>'s to report. A backfill's trigger misfires with
    /// <c>FireNow</c> too, and is measured the same way.
    /// </summary>
    [TestCase(FiringOrigin.Schedule)]
    [TestCase(FiringOrigin.Backfill)]
    public async Task AMisfireCatchUpIsMeasuredFromTheTimeItsMisfirePolicyGaveIt(FiringOrigin origin)
    {
        foreach (MisfireStoreUnderTest store in await BothStores(Anchor()))
        {
            using Harness harness = new(store);
            DateTimeOffset missed = store.Clock.GetUtcNow();
            IOperableTrigger hourly = await StoreHourly<NoOpJob>(
                store,
                origin == FiringOrigin.Backfill ? missed.AddHours(1) : missed,
                startUtc: origin == FiringOrigin.Backfill ? missed.AddDays(-1) : null);

            TriggerKey key = hourly.Key;
            if (origin == FiringOrigin.Backfill)
            {
                BackfillResult backfill = await harness.Scheduler.Backfill(hourly.Key, missed.AddHours(-2), missed.AddHours(-1));
                key = backfill.ScheduledTriggers.Should().ContainSingle().Which;
            }

            // Ten minutes late, far past the one-minute threshold: the misfire pass moves the firing to now, and
            // the job starts two seconds after that.
            store.Clock.Advance(TimeSpan.FromMinutes(10));
            await store.Sweep(store.Clock.GetUtcNow().AddMinutes(-1));
            store.Clock.Advance(TimeSpan.FromSeconds(2));

            TriggerFiredBundle bundle = await Fire(store, key);
            bundle.ScheduledFireTimeUtc.Should().Be(missed, "{0}: the premise: the catch-up keeps the time it missed", store.Name);
            await harness.Run(bundle);

            CollectedMeasurement<double> measurement = harness.StartDelays().Should().ContainSingle().Subject;
            measurement.Value.Should().Be(2,
                "{0}: the catch-up was due when the misfire pass rescheduled it; from the time it missed it would read 602", store.Name);
            measurement.Tags.Should().Contain(OriginTag, origin == FiringOrigin.Backfill ? "backfill" : "schedule");
        }
    }

    /// <summary>
    /// A recovery runs again a firing a stopped node left behind, and is due when that firing was: the
    /// recovery trigger starts at the fired-trigger row's scheduled time and ignores the misfire policy. So
    /// the outage is in its delay, and its trigger's group says what it is.
    /// </summary>
    [Test]
    public async Task ARecoveryIsMeasuredFromWhenTheFiringItRunsAgainWasDue()
    {
        MisfireStoreUnderTest store = await SqliteStore(Anchor());
        using Harness harness = new(store);
        DateTimeOffset due = store.Clock.GetUtcNow();
        IOperableTrigger trigger = await StoreHourly<NoOpJob>(store, due, requestRecovery: true);

        // Fired and never run: the node stopped with the firing in flight, and starts again ten minutes later.
        await Fire(store, trigger.Key);
        store.Clock.Advance(TimeSpan.FromMinutes(10));
        await RecoverAtStartup(store);

        IOperableTrigger recovery = (await store.Store.GetTriggersForJob(trigger.JobKey))
            .Should().ContainSingle(x => x.Key.Group == SchedulerConstants.DefaultRecoveryGroup, "the premise: startup recovery stored a recovery trigger")
            .Subject;

        store.Clock.Advance(TimeSpan.FromSeconds(2));
        TriggerFiredBundle bundle = await Fire(store, recovery.Key);
        bundle.Recovering.Should().BeTrue("the premise: this firing is the recovery");
        await harness.Run(bundle);

        CollectedMeasurement<double> measurement = harness.StartDelays().Should().ContainSingle().Subject;
        measurement.Value.Should().Be(602, "the recovered firing was due ten minutes and two seconds before the recovery ran it");
        measurement.Tags.Should().Contain("quartz.trigger.group", SchedulerConstants.DefaultRecoveryGroup,
            "a recovery's trigger is in RECOVERING_JOBS, which is how an alert leaves the outage out");
    }

    [Test]
    public async Task TheExecuteSpanCarriesTheScheduledFireTimeAndTheOrigin()
    {
        foreach (MisfireStoreUnderTest store in await BothStores(Anchor()))
        {
            using Harness harness = new(store);
            DateTimeOffset due = store.Clock.GetUtcNow();
            IOperableTrigger trigger = await StoreHourly<NoOpJob>(store, due);
            TriggerFiredBundle bundle = await Fire(store, trigger.Key);

            Activity span = null;
            using ActivityListener listener = new()
            {
                ShouldListenTo = source => source.Name == QuartzInstrumentation.ActivitySourceName,
                Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
                ActivityStopped = activity =>
                {
                    if (activity.OperationName == "Quartz.Job.Execute"
                        && activity.GetTagItem("quartz.fire.instance.id") as string == bundle.Trigger.FireInstanceId)
                    {
                        span = activity;
                    }
                }
            };
            ActivitySource.AddActivityListener(listener);

            await harness.Run(bundle);

            span.Should().NotBeNull("{0}: the listener records every Quartz span", store.Name);
            span.GetTagItem(ScheduledFireTimeTag).Should().Be(due.UtcDateTime.ToString("O", CultureInfo.InvariantCulture),
                "{0}: ISO 8601 in UTC, round-trip, so a trace can be matched to the occurrence it served", store.Name);
            span.GetTagItem(OriginTag).Should().Be("schedule", "{0}", store.Name);
        }
    }

    /// <summary>
    /// A store of somebody else's builds its bundles without the due time, which only Quartz's own stores can
    /// set. The delay is then measured from the scheduled fire time, and from the fire time without one.
    /// </summary>
    [TestCase(true, 3)]
    [TestCase(false, 1)]
    public void WithoutADueTimeFromTheStoreTheScheduledFireTimeIsMeasuredFrom(bool hasScheduledFireTime, double expected)
    {
        FakeTimeProvider clock = new(new DateTimeOffset(2026, 11, 13, 12, 0, 0, TimeSpan.Zero));
        using Meters meters = new(meterFactory: null);
        using MetricCollector<double> startDelays = new(meters.Meter, StartDelay);

        TriggerFiredBundle bundle = new()
        {
            JobDetail = JobBuilder.Create<NoOpJob>().WithIdentity("custom-store-job", Group).Build(),
            Trigger = (IOperableTrigger) TriggerBuilder.Create(clock).WithIdentity("custom-store-trigger", Group).ForJob("custom-store-job", Group).StartNow().Build(),
            Recovering = false,
            FireTimeUtc = clock.GetUtcNow().AddSeconds(-1),
            ScheduledFireTimeUtc = hasScheduledFireTime ? clock.GetUtcNow().AddSeconds(-3) : null,
            PreviousFireTimeUtc = null,
            NextFireTimeUtc = null,
        };
        bundle.DueTimeUtc.Should().BeNull("the premise: nothing set it");

        using JobExecutionContextImpl context = new(A.Fake<IScheduler>(), bundle, new NoOpJob());
        meters.StartJobExecute(context, bundle.DueTimeUtc, clock);

        startDelays.GetMeasurementSnapshot().Should().ContainSingle().Which.Value.Should().Be(expected);
    }

    /// <summary>
    /// Stores an hourly trigger for a new job of type <typeparamref name="TJob" />, due at
    /// <paramref name="dueUtc" />.
    /// </summary>
    private static async ValueTask<IOperableTrigger> StoreHourly<TJob>(
        MisfireStoreUnderTest store,
        DateTimeOffset dueUtc,
        RetryPolicy retryPolicy = null,
        DateTimeOffset? startUtc = null,
        bool requestRecovery = false) where TJob : IJob
    {
        string id = Guid.NewGuid().ToString("N");
        JobKey jobKey = new("job-" + id, Group);
        IJobDetail job = JobBuilder.Create<TJob>().WithIdentity(jobKey).RequestRecovery(requestRecovery).Build();

        // FireNow on a misfire, the catch-up a recurring schedule makes, and what a backfill's trigger has.
        IOperableTrigger trigger = (IOperableTrigger) TriggerBuilder.Create(store.Clock)
            .WithIdentity("trigger-" + id, Group)
            .ForJob(jobKey)
            .StartAt(startUtc ?? dueUtc)
            .WithSimpleSchedule(schedule => schedule.WithInterval(TimeSpan.FromHours(1)).RepeatForever().WithMisfireInstruction(SimpleTriggerMisfireInstruction.FireNow))
            .WithRetryPolicy(retryPolicy)
            .Build();

        return await Store(store, job, trigger, dueUtc);
    }

    /// <summary>
    /// Runs the recovery a node runs over its own fired-trigger rows when it starts, without starting it.
    /// </summary>
    /// <remarks>
    /// <c>SchedulerStarted()</c> would also start the misfire handler's loop, which these tests keep out.
    /// </remarks>
    private static ValueTask RecoverAtStartup(MisfireStoreUnderTest store)
    {
        MethodInfo recoverJobs = typeof(AdoJobStoreBase).GetMethod(
            "RecoverJobs",
            BindingFlags.Instance | BindingFlags.NonPublic,
            [typeof(CancellationToken)]);

        return (ValueTask) recoverJobs.Invoke(store.Store, [CancellationToken.None]);
    }

    /// <summary>
    /// Acquires and fires the trigger under test, as the scheduler's loop would.
    /// </summary>
    private static async ValueTask<TriggerFiredBundle> Fire(MisfireStoreUnderTest store, TriggerKey key)
    {
        List<IOperableTrigger> acquired = await store.Store.AcquireNextTriggers(new TriggerAcquisitionRequest
        {
            NoLaterThan = store.Clock.GetUtcNow().AddDays(400),
            MaxCount = 1,
        });

        acquired.Select(x => x.Key).Should().Equal([key], "{0} must hand back the trigger under test as the next one due", store.Name);

        List<TriggerFiredResult> results = await store.Store.TriggersFired(acquired);
        TriggerFiredBundle bundle = results.Should().ContainSingle().Which.TriggerFiredBundle;
        bundle.Should().NotBeNull("{0} must commit the firing", store.Name);
        return bundle;
    }

    /// <summary>
    /// A scheduler over the store under test that is never started, its own meter, and collectors on the two
    /// instruments these cases read.
    /// </summary>
    private sealed class Harness : IDisposable
    {
        internal const string InstanceId = "start-delay-node";

        private readonly QuartzScheduler scheduler;
        private readonly Meters meters;
        private readonly MetricCollector<double> startDelays;
        private readonly MetricCollector<double> durations;

        public Harness(MisfireStoreUnderTest store)
        {
            SchedulerName = "start-delay-" + Guid.NewGuid().ToString("N");
            meters = new Meters(meterFactory: null);
            startDelays = new MetricCollector<double>(meters.Meter, StartDelay);
            durations = new MetricCollector<double>(meters.Meter, ExecuteDuration);

            StdJobRunShellFactory shellFactory = new(NullLogger<JobRunShell>.Instance);
            QuartzSchedulerResources resources = new()
            {
                Name = SchedulerName,
                InstanceId = InstanceId,
                ThreadPool = new DefaultThreadPool { MaxConcurrency = 1 },
                JobRunShellFactory = shellFactory,
                JobStore = store.Store,
                TimeProvider = store.Clock,
                Meters = meters,
            };

            scheduler = new QuartzScheduler(resources, store.Clock);
            shellFactory.Initialize(scheduler.Scheduler);
        }

        public string SchedulerName { get; }

        public IScheduler Scheduler => scheduler.Scheduler;

        /// <summary>
        /// Runs a fired trigger's job through the run shell, as the scheduler's loop hands it to the thread pool.
        /// </summary>
        public async ValueTask Run(TriggerFiredBundle bundle)
        {
            JobRunShell shell = scheduler.resources.JobRunShellFactory.CreateJobRunShell(bundle);
            await shell.Initialize(scheduler);
            await shell.Run();
        }

        public IReadOnlyList<CollectedMeasurement<double>> StartDelays() => startDelays.GetMeasurementSnapshot();

        public IReadOnlyList<CollectedMeasurement<double>> Durations() => durations.GetMeasurementSnapshot();

        public void Dispose()
        {
            startDelays.Dispose();
            durations.Dispose();
            meters.Dispose();
        }
    }

    public sealed class NoOpJob : IJob
    {
        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
    }

    /// <summary>Fails its first attempt, so the trigger's retry policy schedules a retry.</summary>
    public sealed class FailsFirstAttemptJob : IJob
    {
        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
        {
            if (context.RetryAttempt == 0)
            {
                throw new InvalidOperationException("the first attempt fails");
            }

            return default;
        }
    }

    /// <summary>Asks to be run again at once the first time it runs.</summary>
    public sealed class RefiresOnceJob : IJob
    {
        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
        {
            if (context.RefireCount == 0)
            {
                throw new JobExecutionException("again") { RefireImmediately = true };
            }

            return default;
        }
    }
}
