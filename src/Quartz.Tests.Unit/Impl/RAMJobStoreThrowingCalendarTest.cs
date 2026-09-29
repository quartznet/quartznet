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
using System.Diagnostics;
using System.Globalization;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;

using Quartz.Diagnostics;
using Quartz.Extensibility;
using Quartz.Impl;
using Quartz.Impl.Triggers;
using Quartz.Tests.Unit.Plugin.History;

namespace Quartz.Tests.Unit.Impl;

/// <summary>
/// A calendar that throws while the in-memory store fires its trigger fails that trigger's fire alone:
/// the rest of the batch fires once, and nothing is left reserved, blocked or executing (#3974).
/// </summary>
/// <remarks>
/// <para>
/// The throw used to leave <c>TriggersFired</c> part-way through the batch. The fires before it were
/// recorded, and their bundles went nowhere: each read <c>Executing</c> for good, and its
/// <see cref="DisallowConcurrentExecutionAttribute" /> job-mates <c>Blocked</c>. The trigger that threw
/// stayed reserved, out of the schedule.
/// </para>
/// <para>
/// A trigger whose every fire fails is set to <c>ERROR</c> after
/// <see cref="InMemoryJobStoreOptions.MaxConsecutiveFireFailures" /> failures in a row, as the persistent
/// store does since #3963, rather than released and acquired again ahead of its job-mates for good.
/// </para>
/// </remarks>
[NonParallelizable]
public sealed class RAMJobStoreThrowingCalendarTest
{
    private const string Group = "throwing-calendar";
    private const string CalendarName = "faulty";

    /// <summary><c>InMemoryJobStoreOptions.MaxConsecutiveFireFailures</c> as it ships.</summary>
    private const int DefaultMaxConsecutiveFireFailures = 5;

    /// <summary>Log event <c>RAMJobStoreLog.TriggerFireFailed</c>.</summary>
    private const int TriggerFireFailed = 2008;

    /// <summary>Log event <c>RAMJobStoreLog.FailingTriggerSetToError</c>.</summary>
    private const int FailingTriggerSetToError = 2009;

    private static readonly DateTimeOffset epoch = new(2031, 6, 17, 10, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan waitLimit = TimeSpan.FromSeconds(20);

    private static readonly JobKey serialJobKey = new("serial", Group);
    private static readonly JobKey ordinaryJobKey = new("ordinary", Group);

    private static readonly TriggerKey firstKey = new("first", Group);
    private static readonly TriggerKey calendaredKey = new("calendared", Group);
    private static readonly TriggerKey mateKey = new("mate", Group);

    private FakeTimeProvider clock = null!;
    private RecordingSignaler signals = null!;
    private RecordingLoggerProvider logs = null!;
    private ILoggerFactory loggerFactory = null!;
    private RAMJobStore store = null!;
    private CalendarFault fault = null!;

    [SetUp]
    public async Task BuildStore()
    {
        clock = new FakeTimeProvider(epoch);
        signals = new RecordingSignaler();
        logs = new RecordingLoggerProvider();
        loggerFactory = LoggerFactory.Create(logging => logging.AddProvider(logs));
        store = new RAMJobStore(loggerFactory, signals, clock);
        await store.Initialize(TestJobStores.Identity());

        FaultyCalendar calendar = new();
        fault = calendar.Fault;
        await store.AddCalendar(CalendarName, calendar);

        RecordingJob.Reset();
    }

    [TearDown]
    public async Task ShutDownStore()
    {
        await store.Shutdown();
        loggerFactory.Dispose();
        logs.Dispose();
    }

    //////////////////////////////////////////////////////////////////////////////////////////////
    // The store, driven by hand as the scheduler thread drives it
    //////////////////////////////////////////////////////////////////////////////////////////////

    /// <summary>
    /// A batch of <c>first</c> and <c>calendared</c>, whose calendar throws: <c>first</c> fires, and
    /// <c>calendared</c> is answered <c>Failed</c> with the calendar's exception and left as it was.
    /// </summary>
    [Test]
    public async Task ACalendarThatThrowsFailsItsTriggerAloneAndTheRestOfTheBatchFires()
    {
        await GivenTheBatch();
        IOperableTrigger before = (await store.GetTrigger(calendaredKey))!;
        fault.ThrowOnce();

        List<IOperableTrigger> acquired = await Acquire();
        acquired.Select(x => x.Key).Should().Equal([firstKey, calendaredKey],
            "a batch takes one trigger of a serial job, so the mate stays behind");

        List<TriggerFiredResult> results = await store.TriggersFired(acquired);

        results.Should().HaveCount(2, "one answer per trigger, in the order asked");
        results[0].TriggerFiredBundle.Should().NotBeNull("first fired before the calendar threw, and its bundle is the scheduler's to run");
        results[1].TriggerFiredBundle.Should().BeNull();
        results[1].IsDeclined.Should().BeFalse("the store settled nothing; the scheduler releases a failed result");
        results[1].Exception.Should().BeSameAs(fault.LastThrown, "the result carries what the calendar threw");

        ShouldBeAsItWas(await store.GetTrigger(calendaredKey), before);
        (await Reservations()).Should().Equal([calendaredKey],
            "the failed trigger stays reserved until the scheduler releases it; the fired one is executing");
        (await store.GetTriggerState(mateKey)).Should().Be(TriggerState.Blocked, "first's job is running");

        await store.ReleaseAcquiredTrigger(acquired[1]);
        (await Reservations()).Should().BeEmpty("released, as the scheduler thread releases any failed result");

        List<TriggerFiredResult> again = await store.TriggersFired(await Acquire());
        again.Should().ContainSingle().Which.TriggerFiredBundle.Should().NotBeNull(
            "released, the trigger is acquired again and fires once its calendar answers");

        await Complete(results[0]);
        (await store.GetTriggerState(mateKey)).Should().Be(TriggerState.Normal,
            "first's completion lets go of its job-mate, which a lost bundle never would");

        logs.Entries.Should().ContainSingle(x => x.EventId.Id == TriggerFireFailed)
            .Which.Exception.Should().BeSameAs(fault.LastThrown);
    }

    /// <summary>
    /// The calendar answers the store's own trigger and throws for the scheduler's copy, after the stored
    /// trigger has already moved on. The stored trigger is still put back as it was.
    /// </summary>
    [Test]
    public async Task ACalendarThatThrowsAfterTheStoredTriggerMovedOnStillLeavesItAsItWas()
    {
        await GivenTheBatch();
        IOperableTrigger before = (await store.GetTrigger(calendaredKey))!;

        // The store advances its own trigger and then the scheduler's copy, consulting the calendar
        // once for each.
        fault.ThrowOnce(after: 1);

        List<TriggerFiredResult> results = await store.TriggersFired(await Acquire());

        results[1].Exception.Should().BeSameAs(fault.LastThrown);
        ShouldBeAsItWas(await store.GetTrigger(calendaredKey), before);
    }

    /// <summary>
    /// Under <see cref="OverlapPolicy.Skip" />, a firing that comes due while an earlier one runs is
    /// advanced past, which consults the calendar too. A throw there fails the fire rather than skipping
    /// it, and the listeners are not told of a skip that did not happen.
    /// </summary>
    [Test]
    public async Task ACalendarThatThrowsWhileSkippingAnOverlapFailsTheFireAndReportsNoSkip()
    {
        await AddJob(ordinaryJobKey, serial: false);
        await Schedule(calendaredKey, ordinaryJobKey, onCalendar: true, policy: OverlapPolicy.Skip);

        List<TriggerFiredResult> running = await store.TriggersFired(await Acquire());
        running.Should().ContainSingle().Which.TriggerFiredBundle.Should().NotBeNull();

        clock.Advance(TimeSpan.FromHours(1));
        IOperableTrigger before = (await store.GetTrigger(calendaredKey))!;
        fault.ThrowOnce();

        List<TriggerFiredResult> overlapping = await store.TriggersFired(await Acquire());

        overlapping.Should().ContainSingle().Which.Exception.Should().BeSameAs(fault.LastThrown);
        overlapping[0].IsDeclined.Should().BeFalse("the skip did not happen, so the store settled nothing");
        signals.Skipped.Should().BeEmpty("a listener is told of a skip only once the trigger has moved past it");
        ShouldBeAsItWas(await store.GetTrigger(calendaredKey), before);
    }

    /// <summary>
    /// One failure short of the limit leaves the trigger to be released for the next round; the failure
    /// that reaches it sets the trigger ERROR and says so once, to the listeners and in the log.
    /// </summary>
    [Test]
    public async Task TheFailureThatReachesTheLimitSetsTheTriggerErrorAndSaysSoOnce()
    {
        await GivenTheBatch();
        fault.ThrowAlways();

        for (int failure = 1; failure < DefaultMaxConsecutiveFireFailures; failure++)
        {
            (await FireAndRelease(calendaredKey)).Exception.Should().NotBeNull();
            (await store.GetTriggerState(calendaredKey)).Should().Be(TriggerState.Normal,
                $"{failure} failure(s) in a row is short of the limit, and the release puts it back");
        }

        signals.TriggersInError.Should().BeEmpty();
        logs.Entries.Should().NotContain(x => x.EventId.Id == FailingTriggerSetToError);

        TriggerFiredResult last = await FireAndRelease(calendaredKey);

        last.Exception.Should().NotBeNull("the result is the failed fire's, whatever the store did about it afterwards");
        (await store.GetTriggerState(calendaredKey)).Should().Be(TriggerState.Error,
            "the limit's failure sets the trigger ERROR, and the scheduler thread's release leaves it there");
        (await Reservations()).Should().BeEmpty("an ERROR trigger holds no reservation");
        signals.TriggersInError.Should().Equal([calendaredKey]);

        LogEntry parked = logs.Entries.Should().ContainSingle(x => x.EventId.Id == FailingTriggerSetToError).Subject;
        parked.Level.Should().Be(LogLevel.Error);
        parked.Message.Should().Contain(calendaredKey.ToString())
            .And.Contain(DefaultMaxConsecutiveFireFailures.ToString(CultureInfo.InvariantCulture));
        logs.Entries.Count(x => x.EventId.Id == TriggerFireFailed).Should().Be(DefaultMaxConsecutiveFireFailures,
            "every failure is logged with its exception, the one that parks the trigger included");

        (await Acquire()).Select(x => x.Key).Should().NotContain(calendaredKey, "an ERROR trigger is not acquired");
    }

    /// <summary>
    /// The limit counts failures in a row: a fire that succeeds starts the count again.
    /// </summary>
    [Test]
    public async Task AFireThatSucceedsStartsTheCountAgain()
    {
        await AddJob(ordinaryJobKey, serial: false);
        await Schedule(calendaredKey, ordinaryJobKey, onCalendar: true);

        fault.ThrowAlways();
        await FailInARow(DefaultMaxConsecutiveFireFailures - 1);

        fault.Heal();
        TriggerFiredResult success = await FireAndRelease(calendaredKey);
        success.TriggerFiredBundle.Should().NotBeNull();
        await Complete(success);
        clock.Advance(TimeSpan.FromHours(1));

        fault.ThrowAlways();
        await FailInARow(DefaultMaxConsecutiveFireFailures - 1);
        (await store.GetTriggerState(calendaredKey)).Should().Be(TriggerState.Normal,
            "the failures either side of the success are one short of the limit each");

        await FireAndRelease(calendaredKey);
        (await store.GetTriggerState(calendaredKey)).Should().Be(TriggerState.Error,
            "the count went on from where the success left it");
    }

    /// <summary>
    /// A limit of zero never sets a failing trigger ERROR: it is released and fails again, round after
    /// round, as it would have before the limit existed.
    /// </summary>
    [Test]
    public async Task ALimitOfZeroNeverSetsAFailingTriggerError()
    {
        store.MaxConsecutiveFireFailures = 0;
        await AddJob(ordinaryJobKey, serial: false);
        await Schedule(calendaredKey, ordinaryJobKey, onCalendar: true);

        fault.ThrowAlways();
        await FailInARow(DefaultMaxConsecutiveFireFailures * 2);

        (await store.GetTriggerState(calendaredKey)).Should().Be(TriggerState.Normal);
        signals.TriggersInError.Should().BeEmpty();
        logs.Entries.Should().NotContain(x => x.EventId.Id == FailingTriggerSetToError);
    }

    /// <summary>
    /// A trigger type of the application's own is application code too, as a calendar is: one that
    /// throws after it has moved itself on, with no calendar at all, is put back as it was.
    /// </summary>
    [Test]
    public async Task ATriggerTypeOfYourOwnThatThrowsIsLeftAsItWasToo()
    {
        await AddJob(ordinaryJobKey, serial: false);
        ThrowingSimpleTrigger trigger = new(fault)
        {
            Key = calendaredKey,
            JobKey = ordinaryJobKey,
            StartTimeUtc = clock.GetUtcNow(),
            RepeatInterval = TimeSpan.FromHours(1),
            RepeatCount = SimpleTriggerImpl.RepeatIndefinitely,
        };
        trigger.ComputeFirstFireTimeUtc(calendar: null);
        await store.AddTrigger(trigger);
        IOperableTrigger before = (await store.GetTrigger(calendaredKey))!;
        fault.ThrowOnce();

        TriggerFiredResult result = await FireAndRelease(calendaredKey);

        result.Exception.Should().BeSameAs(fault.LastThrown);
        ShouldBeAsItWas(await store.GetTrigger(calendaredKey), before);
    }

    /// <summary>
    /// A trigger set ERROR comes back with <see cref="IJobStore.ResetTriggerFromErrorState" /> and fires
    /// once its calendar answers.
    /// </summary>
    [Test]
    public async Task ATriggerSetErrorIsResetAndFiresOnceTheCalendarAnswers()
    {
        await AddJob(ordinaryJobKey, serial: false);
        await Schedule(calendaredKey, ordinaryJobKey, onCalendar: true);

        fault.ThrowAlways();
        await FailInARow(DefaultMaxConsecutiveFireFailures);
        (await store.GetTriggerState(calendaredKey)).Should().Be(TriggerState.Error);

        fault.Heal();
        (await store.ResetTriggerFromErrorState(calendaredKey)).Should().BeTrue();

        (await FireAndRelease(calendaredKey)).TriggerFiredBundle.Should().NotBeNull(
            "reset, the trigger is acquired again, and nothing of its failures is left to count against it");
    }

    //////////////////////////////////////////////////////////////////////////////////////////////
    // A running scheduler
    //////////////////////////////////////////////////////////////////////////////////////////////

    /// <summary>
    /// The issue's batch on a running scheduler: the calendar throws once, <c>first</c> runs once, its
    /// job-mate runs once <c>first</c> has, and <c>calendared</c> runs in a later round.
    /// </summary>
    /// <param name="traced">
    /// Whether the store operations are being recorded, which runs the batch through the tracing
    /// decorator's recording path rather than its pass-through.
    /// </param>
    [TestCase(false)]
    [TestCase(true)]
    public async Task ACalendarThatThrowsOnceCostsItsTriggerOneRoundAndTheBatchBesideItRunsOnce(bool traced)
    {
        using ActivityListener? listener = traced ? RecordQuartzActivities() : null;
        await using ServiceProvider container = BuildContainer();
        IScheduler scheduler = await container.GetRequiredService<ISchedulerFactory>().GetScheduler();

        CalendarFault schedulerFault = await GivenTheBatch(scheduler);
        schedulerFault.ThrowOnce();

        await scheduler.Start();
        try
        {
            await WaitFor(() => RecordingJob.RunsOf(firstKey) > 0 && RecordingJob.RunsOf(mateKey) > 0 && RecordingJob.RunsOf(calendaredKey) > 0,
                "first, its job-mate and the calendared trigger to run");
        }
        finally
        {
            await scheduler.Shutdown(waitForJobsToComplete: true);
        }

        RecordingJob.RunsOf(firstKey).Should().Be(1, "the calendar's throw costs first nothing, and a fire is never repeated");
        RecordingJob.RunsOf(mateKey).Should().Be(1, "first's completion lets go of its job-mate");
        RecordingJob.RunsOf(calendaredKey).Should().Be(1, "released, the calendared trigger fires in a later round");
        schedulerFault.Thrown.Should().Be(1);
    }

    /// <summary>
    /// A calendar that throws on every fire of a trigger that leads every round, beside a job-mate on the
    /// same serial job: the trigger is set ERROR after the failures the store allows, and the job-mate
    /// runs.
    /// </summary>
    [Test]
    public async Task ACalendarThatAlwaysThrowsHasItsTriggerSetErrorAndItsSerialJobMateRuns()
    {
        await using ServiceProvider container = BuildContainer();
        IScheduler scheduler = await container.GetRequiredService<ISchedulerFactory>().GetScheduler();
        InErrorListener inError = new();
        scheduler.ListenerManager.AddSchedulerListener(inError);

        FaultyCalendar calendar = new();
        await scheduler.AddCalendar(CalendarName, calendar);
        DateTimeOffset due = TimeProvider.System.GetUtcNow().AddMilliseconds(500);
        await scheduler.AddJob(Job(serialJobKey, serial: true));
        await scheduler.ScheduleJob(Trigger(calendaredKey, serialJobKey, due, priority: 10, onCalendar: true));
        await scheduler.ScheduleJob(Trigger(mateKey, serialJobKey, due, priority: 1));
        calendar.Fault.ThrowAlways();

        await scheduler.Start();
        try
        {
            await WaitFor(() => RecordingJob.RunsOf(mateKey) > 0,
                "the job-mate to run; it is behind the failing trigger in every batch until that stops being acquired");

            (await scheduler.GetTriggerState(calendaredKey)).Should().Be(TriggerState.Error);
        }
        finally
        {
            await scheduler.Shutdown(waitForJobsToComplete: true);
        }

        calendar.Fault.Thrown.Should().Be(DefaultMaxConsecutiveFireFailures,
            "set ERROR on the last failure it allows, the trigger is not fired again");
        inError.Heard.Should().Equal([calendaredKey]);
    }

    //////////////////////////////////////////////////////////////////////////////////////////////
    // Helpers
    //////////////////////////////////////////////////////////////////////////////////////////////

    /// <summary>
    /// <c>first</c> and its job-mate on the serial job, and <c>calendared</c> on the ordinary job and the
    /// faulty calendar, all due at once, <c>first</c> ahead by priority.
    /// </summary>
    private async Task GivenTheBatch()
    {
        await AddJob(serialJobKey, serial: true);
        await AddJob(ordinaryJobKey, serial: false);
        await Schedule(firstKey, serialJobKey, priority: 10);
        await Schedule(calendaredKey, ordinaryJobKey, priority: 5, onCalendar: true);
        await Schedule(mateKey, serialJobKey, priority: 1);
    }

    private static async Task<CalendarFault> GivenTheBatch(IScheduler scheduler)
    {
        FaultyCalendar calendar = new();
        await scheduler.AddCalendar(CalendarName, calendar);

        // Far enough ahead that the loop has acquired all three before they are due, so they are one
        // batch; the same time for all three, so that the priorities order them.
        DateTimeOffset due = TimeProvider.System.GetUtcNow().AddMilliseconds(500);
        await scheduler.AddJob(Job(serialJobKey, serial: true));
        await scheduler.AddJob(Job(ordinaryJobKey, serial: false));
        await scheduler.ScheduleJob(Trigger(firstKey, serialJobKey, due, priority: 10));
        await scheduler.ScheduleJob(Trigger(calendaredKey, ordinaryJobKey, due, priority: 5, onCalendar: true));
        await scheduler.ScheduleJob(Trigger(mateKey, serialJobKey, due, priority: 1));
        return calendar.Fault;
    }

    private async Task AddJob(JobKey key, bool serial)
    {
        await store.AddJob(Job(key, serial));
    }

    private static IJobDetail Job(JobKey key, bool serial)
    {
        return serial
            ? JobBuilder.Create<SerialRecordingJob>().WithIdentity(key).StoreDurably().Build()
            : JobBuilder.Create<RecordingJob>().WithIdentity(key).StoreDurably().Build();
    }

    private async Task Schedule(
        TriggerKey key,
        JobKey jobKey,
        int priority = TriggerConstants.DefaultPriority,
        bool onCalendar = false,
        OverlapPolicy policy = OverlapPolicy.Default)
    {
        IOperableTrigger trigger = (IOperableTrigger) TriggerBuilder.Create(clock)
            .WithIdentity(key)
            .ForJob(jobKey)
            .StartAt(clock.GetUtcNow())
            .WithSimpleSchedule(x => x.WithInterval(TimeSpan.FromHours(1)).RepeatForever())
            .WithPriority(priority)
            .WithCalendarName(onCalendar ? CalendarName : null)
            .WithOverlapPolicy(policy)
            .Build();

        // Without the calendar, so the calendar is consulted only by the fires the tests count.
        trigger.ComputeFirstFireTimeUtc(calendar: null);
        await store.AddTrigger(trigger);
    }

    private static ITrigger Trigger(TriggerKey key, JobKey jobKey, DateTimeOffset due, int priority, bool onCalendar = false)
    {
        return TriggerBuilder.Create()
            .WithIdentity(key)
            .ForJob(jobKey)
            .StartAt(due)
            .WithSimpleSchedule(x => x.WithInterval(TimeSpan.FromHours(1)).RepeatForever())
            .WithPriority(priority)
            .WithCalendarName(onCalendar ? CalendarName : null)
            .Build();
    }

    private async Task<List<IOperableTrigger>> Acquire()
    {
        return await store.AcquireNextTriggers(new TriggerAcquisitionRequest
        {
            NoLaterThan = clock.GetUtcNow().AddSeconds(1),
            MaxCount = 10,
            TimeWindow = TimeSpan.Zero
        });
    }

    /// <summary>
    /// Acquires what is due, fires it, and releases whatever did not fire, as the scheduler thread does;
    /// answers the result for <paramref name="key" />.
    /// </summary>
    private async Task<TriggerFiredResult> FireAndRelease(TriggerKey key)
    {
        List<IOperableTrigger> acquired = await Acquire();
        int index = acquired.FindIndex(x => x.Key.Equals(key));
        index.Should().BeGreaterThanOrEqualTo(0, $"{key} is due and acquirable");

        List<TriggerFiredResult> results = await store.TriggersFired(acquired);
        for (int i = 0; i < results.Count; i++)
        {
            if (results[i].TriggerFiredBundle is null && !results[i].IsDeclined)
            {
                await store.ReleaseAcquiredTrigger(acquired[i]);
            }
        }

        return results[index];
    }

    private async Task FailInARow(int failures)
    {
        for (int i = 0; i < failures; i++)
        {
            (await FireAndRelease(calendaredKey)).Exception.Should().NotBeNull();
        }
    }

    private async Task Complete(TriggerFiredResult fired)
    {
        await store.TriggeredJobComplete(
            fired.TriggerFiredBundle!.Trigger,
            fired.TriggerFiredBundle.JobDetail,
            SchedulerInstruction.NoInstruction);
    }

    /// <summary>The triggers the store holds a reservation for that has not fired.</summary>
    private async Task<List<TriggerKey>> Reservations()
    {
        PagedResult<FireInstance> reserved = await store.QueryFireInstances(new FireInstanceQuery
        {
            State = FireInstanceState.Acquired,
            Take = PagedQuery.All
        });

        return reserved.Items.Select(x => x.TriggerKey).ToList();
    }

    private static void ShouldBeAsItWas(IOperableTrigger? after, IOperableTrigger before)
    {
        after.Should().NotBeNull();
        after!.NextFireTimeUtc.Should().Be(before.NextFireTimeUtc, "a failed fire does not move the trigger on");
        after.PreviousFireTimeUtc.Should().Be(before.PreviousFireTimeUtc, "only a fire that happened is a previous one");
        ((ISimpleTrigger) after).TimesTriggered.Should().Be(((ISimpleTrigger) before).TimesTriggered,
            "a fire that failed is not counted as one");
    }

    private static ServiceProvider BuildContainer()
    {
        ServiceCollection services = new();
        services.AddLogging();
        services.AddQuartz(q => q.ConfigureScheduler(options =>
        {
            options.InstanceName = "throwing-calendar-" + Guid.NewGuid().ToString("N");
            options.InstanceId = "one";

            // Batched, so a running scheduler fires the calendared trigger beside the rest of its batch.
            options.MaxBatchSize = 4;
        }));

        return services.BuildServiceProvider();
    }

    private static ActivityListener RecordQuartzActivities()
    {
        ActivityListener listener = new()
        {
            ShouldListenTo = static source => source.Name == QuartzInstrumentation.ActivitySourceName,
            Sample = static (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
        };

        ActivitySource.AddActivityListener(listener);
        return listener;
    }

    private static Task WaitFor(Func<bool> condition, string what) => WaitFor(() => Task.FromResult(condition()), what);

    private static async Task WaitFor(Func<Task<bool>> condition, string what)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow + waitLimit;
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (await condition())
            {
                return;
            }

            await Task.Delay(50);
        }

        Assert.Fail($"Timed out after {waitLimit.TotalSeconds:F0} s waiting for {what}.");
    }

    /// <summary>
    /// Throws on the calls it is told to, and answers every other time as included. One instance is
    /// shared by every clone of the calendar, because a store keeps a clone of the one it is given.
    /// </summary>
    private sealed class CalendarFault
    {
        private readonly Lock gate = new();
        private int skip;
        private int remaining;
        private int thrown;
        private Exception? lastThrown;

        /// <summary>How many times the calendar has thrown.</summary>
        public int Thrown
        {
            get
            {
                lock (gate)
                {
                    return thrown;
                }
            }
        }

        /// <summary>What the calendar threw last.</summary>
        public Exception? LastThrown
        {
            get
            {
                lock (gate)
                {
                    return lastThrown;
                }
            }
        }

        /// <summary>Throws once, on the call after the next <paramref name="after" /> ones.</summary>
        public void ThrowOnce(int after = 0)
        {
            lock (gate)
            {
                skip = after;
                remaining = 1;
            }
        }

        public void ThrowAlways()
        {
            lock (gate)
            {
                skip = 0;
                remaining = -1;
            }
        }

        public void Heal()
        {
            lock (gate)
            {
                remaining = 0;
            }
        }

        public void Consult()
        {
            Exception failure;
            lock (gate)
            {
                if (remaining == 0)
                {
                    return;
                }

                if (skip > 0)
                {
                    skip--;
                    return;
                }

                if (remaining > 0)
                {
                    remaining--;
                }

                thrown++;
                failure = new InvalidOperationException("The holiday feed is unreachable.");
                lastThrown = failure;
            }

            throw failure;
        }
    }

    private sealed class FaultyCalendar : ICalendar
    {
        public FaultyCalendar() : this(new CalendarFault())
        {
        }

        private FaultyCalendar(CalendarFault fault)
        {
            Fault = fault;
        }

        public CalendarFault Fault { get; }

        public string? Description { get; set; }

        public ICalendar? CalendarBase { get; set; }

        public bool IsTimeIncluded(DateTimeOffset timeUtc)
        {
            Fault.Consult();
            return true;
        }

        public DateTimeOffset GetNextIncludedTimeUtc(DateTimeOffset timeUtc)
        {
            Fault.Consult();
            return timeUtc;
        }

        public ICalendar Clone() => new FaultyCalendar(Fault) { Description = Description, CalendarBase = CalendarBase };
    }

    /// <summary>
    /// Moves itself on as a simple trigger does, then consults its fault.
    /// </summary>
    private sealed class ThrowingSimpleTrigger(CalendarFault fault) : SimpleTriggerImpl
    {
        public override void Triggered(ICalendar? calendar)
        {
            base.Triggered(calendar);
            fault.Consult();
        }
    }

    /// <summary>
    /// Records what the store told the scheduler about.
    /// </summary>
    private sealed class RecordingSignaler : ISchedulerSignaler
    {
        public List<TriggerKey> Skipped { get; } = [];

        public List<TriggerKey> TriggersInError { get; } = [];

        public ValueTask NotifyTriggerListenersMisfired(ITrigger trigger, CancellationToken cancellationToken = default) => default;

        public ValueTask NotifyTriggerListenersSkipped(ITrigger trigger, CancellationToken cancellationToken = default)
        {
            Skipped.Add(trigger.Key);
            return default;
        }

        public ValueTask NotifySchedulerListenersFinalized(ITrigger trigger, CancellationToken cancellationToken = default) => default;

        public ValueTask NotifySchedulerListenersJobDeleted(JobKey jobKey, CancellationToken cancellationToken = default) => default;

        public ValueTask NotifySchedulerListenersTriggerInError(TriggerKey triggerKey, CancellationToken cancellationToken = default)
        {
            TriggersInError.Add(triggerKey);
            return default;
        }

        public ValueTask SignalSchedulingChange(DateTimeOffset? candidateNewNextFireTimeUtc, CancellationToken cancellationToken = default) => default;

        public ValueTask NotifySchedulerListenersError(SchedulerErrorContext errorContext, CancellationToken cancellationToken = default) => default;
    }

    private sealed class InErrorListener : ISchedulerListener
    {
        private readonly ConcurrentQueue<TriggerKey> heard = new();

        public List<TriggerKey> Heard => heard.ToList();

        public ValueTask TriggerInError(IScheduler scheduler, TriggerKey triggerKey, CancellationToken cancellationToken = default)
        {
            heard.Enqueue(triggerKey);
            return default;
        }
    }

    /// <summary>Counts its runs by trigger.</summary>
    public class RecordingJob : IJob
    {
        private static ConcurrentDictionary<TriggerKey, int> runs = new();

        public static void Reset() => runs = new ConcurrentDictionary<TriggerKey, int>();

        public static int RunsOf(TriggerKey key) => runs.TryGetValue(key, out int count) ? count : 0;

        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
        {
            runs.AddOrUpdate(context.Trigger.Key, 1, static (_, count) => count + 1);
            return default;
        }
    }

    [DisallowConcurrentExecution]
    public sealed class SerialRecordingJob : RecordingJob;
}
