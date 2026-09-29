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

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using FakeItEasy;

using Microsoft.Extensions.Time.Testing;

using Quartz.Impl;
using Quartz.Impl.Triggers;
using Quartz.Simpl;
using Quartz.Spi;

namespace Quartz.Tests.Unit.Simpl;

/// <summary>
/// A calendar that throws while <see cref="RAMJobStore" /> fires its trigger fails that trigger's fire
/// alone: the rest of the batch fires once, and nothing is left reserved or blocked (#3974).
/// </summary>
/// <remarks>
/// <para>
/// The throw used to leave <c>TriggersFired</c> part-way through the batch. The fires before it were
/// recorded, and their bundles went nowhere, so each one's <see cref="DisallowConcurrentExecutionAttribute" />
/// job-mates read <c>Blocked</c> for good. The trigger that threw stayed reserved, out of the schedule.
/// </para>
/// <para>
/// A trigger whose every fire fails is set to <c>ERROR</c> after
/// <see cref="RAMJobStore.MaxConsecutiveFireFailures" /> failures in a row, as the database job store
/// does (#3963), rather than released and acquired again ahead of its job-mates for good.
/// </para>
/// </remarks>
[NonParallelizable]
public class RAMJobStoreThrowingCalendarTest
{
    private const string Group = "throwing-calendar";
    private const string CalendarName = "faulty";

    /// <summary><c>RAMJobStore.MaxConsecutiveFireFailures</c> as it ships.</summary>
    private const int DefaultMaxConsecutiveFireFailures = 5;

    private static readonly DateTimeOffset epoch = new DateTimeOffset(2031, 6, 17, 10, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan waitLimit = TimeSpan.FromSeconds(20);

    private static readonly JobKey serialJobKey = new JobKey("serial", Group);
    private static readonly JobKey ordinaryJobKey = new JobKey("ordinary", Group);

    private static readonly TriggerKey firstKey = new TriggerKey("first", Group);
    private static readonly TriggerKey calendaredKey = new TriggerKey("calendared", Group);
    private static readonly TriggerKey mateKey = new TriggerKey("mate", Group);

    private Func<DateTimeOffset> originalUtcNow = null!;
    private FakeTimeProvider clock = null!;
    private RAMJobStore store = null!;
    private CalendarFault fault = null!;

    [SetUp]
    public async Task BuildStore()
    {
        originalUtcNow = SystemTime.UtcNow;
        clock = new FakeTimeProvider(epoch);

        store = new RAMJobStore();
        await store.Initialize(new SimpleTypeLoadHelper(), A.Fake<ISchedulerSignaler>());
        await store.SchedulerStarted();

        FaultyCalendar calendar = new FaultyCalendar();
        fault = calendar.Fault;
        await store.StoreCalendar(CalendarName, calendar, replaceExisting: false, updateTriggers: false);

        RecordingJob.Reset();
        ThrowOnceJobStore.Reset();
    }

    [TearDown]
    public async Task ShutDownStore()
    {
        SystemTime.UtcNow = originalUtcNow;
        await store.Shutdown();
    }

    //////////////////////////////////////////////////////////////////////////////////////////////
    // The store, driven by hand as the scheduler thread drives it, on a clock the test moves
    //////////////////////////////////////////////////////////////////////////////////////////////

    /// <summary>
    /// A batch of <c>first</c> and <c>calendared</c>, whose calendar throws: <c>first</c> fires, and
    /// <c>calendared</c> is answered failed with the calendar's exception and left as it was.
    /// </summary>
    [Test]
    public async Task ACalendarThatThrowsFailsItsTriggerAloneAndTheRestOfTheBatchFires()
    {
        FreezeClock();
        await GivenTheBatch();
        IOperableTrigger before = (await store.RetrieveTrigger(calendaredKey))!;
        fault.ThrowOnce();

        List<IOperableTrigger> acquired = await Acquire();
        acquired.Select(x => x.Key).Should().Equal(new[] { firstKey, calendaredKey },
            "a batch takes one trigger of a serial job, so the mate stays behind");

        List<TriggerFiredResult> results = (await store.TriggersFired(acquired)).ToList();

        results.Should().HaveCount(2, "one answer per trigger, in the order asked");
        results[0].TriggerFiredBundle.Should().NotBeNull("first fired before the calendar threw, and its bundle is the scheduler's to run");
        results[1].TriggerFiredBundle.Should().BeNull();
        results[1].Exception.Should().BeSameAs(fault.LastThrown, "the result carries what the calendar threw");

        ShouldBeAsItWas(await store.RetrieveTrigger(calendaredKey), before);
        (await Acquire()).Should().BeEmpty("the failed trigger stays reserved until the scheduler releases it, and first's job-mate is blocked");
        (await store.GetTriggerState(mateKey)).Should().Be(TriggerState.Blocked, "first's job is running");

        await store.ReleaseAcquiredTrigger(acquired[1]);

        List<TriggerFiredResult> again = (await store.TriggersFired(await Acquire())).ToList();
        again.Should().ContainSingle().Which.TriggerFiredBundle.Should().NotBeNull(
            "released, the trigger is acquired again and fires once its calendar answers");

        await Complete(results[0]);
        (await store.GetTriggerState(mateKey)).Should().Be(TriggerState.Normal,
            "first's completion lets go of its job-mate, which a lost bundle never would");
    }

    /// <summary>
    /// The calendar answers the store's own trigger and throws for the scheduler's copy, after the stored
    /// trigger has already moved on. The stored trigger is still put back as it was.
    /// </summary>
    [Test]
    public async Task ACalendarThatThrowsAfterTheStoredTriggerMovedOnStillLeavesItAsItWas()
    {
        FreezeClock();
        await GivenTheBatch();
        IOperableTrigger before = (await store.RetrieveTrigger(calendaredKey))!;

        // The store advances its own trigger and then the scheduler's copy, consulting the calendar
        // once for each.
        fault.ThrowOnce(after: 1);

        List<TriggerFiredResult> results = (await store.TriggersFired(await Acquire())).ToList();

        results[1].Exception.Should().BeSameAs(fault.LastThrown);
        ShouldBeAsItWas(await store.RetrieveTrigger(calendaredKey), before);
    }

    /// <summary>
    /// One failure short of the limit leaves the trigger to be released for the next round; the failure
    /// that reaches it sets the trigger ERROR, and it is not acquired again.
    /// </summary>
    [Test]
    public async Task TheFailureThatReachesTheLimitSetsTheTriggerError()
    {
        FreezeClock();
        await GivenTheBatch();
        fault.ThrowAlways();

        for (int failure = 1; failure < DefaultMaxConsecutiveFireFailures; failure++)
        {
            (await FireAndRelease(calendaredKey)).Exception.Should().NotBeNull();
            (await store.GetTriggerState(calendaredKey)).Should().Be(TriggerState.Normal,
                $"{failure} failure(s) in a row is short of the limit, and the release puts it back");
        }

        TriggerFiredResult last = await FireAndRelease(calendaredKey);

        last.Exception.Should().NotBeNull("the result is the failed fire's, whatever the store did about it afterwards");
        (await store.GetTriggerState(calendaredKey)).Should().Be(TriggerState.Error,
            "the limit's failure sets the trigger ERROR, and the scheduler thread's release leaves it there");
        (await Acquire()).Select(x => x.Key).Should().NotContain(calendaredKey, "an ERROR trigger is not acquired");
    }

    /// <summary>
    /// The limit counts failures in a row: a fire that succeeds starts the count again.
    /// </summary>
    [Test]
    public async Task AFireThatSucceedsStartsTheCountAgain()
    {
        FreezeClock();
        await StoreJob(ordinaryJobKey, serial: false);
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
        FreezeClock();
        store.MaxConsecutiveFireFailures = 0;
        await StoreJob(ordinaryJobKey, serial: false);
        await Schedule(calendaredKey, ordinaryJobKey, onCalendar: true);

        fault.ThrowAlways();
        await FailInARow(DefaultMaxConsecutiveFireFailures * 2);

        (await store.GetTriggerState(calendaredKey)).Should().Be(TriggerState.Normal);
    }

    /// <summary>
    /// A trigger type of the application's own is application code too, as a calendar is: one that
    /// throws after it has moved itself on, with no calendar at all, is put back as it was.
    /// </summary>
    [Test]
    public async Task ATriggerTypeOfYourOwnThatThrowsIsLeftAsItWasToo()
    {
        FreezeClock();
        await StoreJob(ordinaryJobKey, serial: false);
        ThrowingSimpleTrigger trigger = new ThrowingSimpleTrigger(fault)
        {
            Key = calendaredKey,
            JobKey = ordinaryJobKey,
            StartTimeUtc = clock.GetUtcNow(),
            RepeatInterval = TimeSpan.FromHours(1),
            RepeatCount = SimpleTriggerImpl.RepeatIndefinitely,
        };
        trigger.ComputeFirstFireTimeUtc(null);
        await store.StoreTrigger(trigger, replaceExisting: false);
        IOperableTrigger before = (await store.RetrieveTrigger(calendaredKey))!;
        fault.ThrowOnce();

        TriggerFiredResult result = await FireAndRelease(calendaredKey);

        result.Exception.Should().BeSameAs(fault.LastThrown);
        ShouldBeAsItWas(await store.RetrieveTrigger(calendaredKey), before);
    }

    /// <summary>
    /// A trigger set ERROR comes back with <see cref="IJobStore.ResetTriggerFromErrorState" /> and fires
    /// once its calendar answers.
    /// </summary>
    [Test]
    public async Task ATriggerSetErrorIsResetAndFiresOnceTheCalendarAnswers()
    {
        FreezeClock();
        await StoreJob(ordinaryJobKey, serial: false);
        await Schedule(calendaredKey, ordinaryJobKey, onCalendar: true);

        fault.ThrowAlways();
        await FailInARow(DefaultMaxConsecutiveFireFailures);
        (await store.GetTriggerState(calendaredKey)).Should().Be(TriggerState.Error);

        fault.Heal();
        await store.ResetTriggerFromErrorState(calendaredKey);

        (await FireAndRelease(calendaredKey)).TriggerFiredBundle.Should().NotBeNull(
            "reset, the trigger is acquired again, and nothing of its failures is left to count against it");
    }

    /// <summary>
    /// The limit is a property of the store, so the flat key the database job store reads reaches the
    /// in-memory store the same way: by name.
    /// </summary>
    [Test]
    public async Task TheLimitIsSetThroughTheSameFlatKeyAsTheDatabaseJobStores()
    {
        NameValueCollection properties = SchedulerProperties("flat-key", typeof(ThrowOnceJobStore));
        properties["quartz.jobStore.maxConsecutiveFireFailures"] = "2";

        IScheduler scheduler = await new StdSchedulerFactory(properties).GetScheduler();
        try
        {
            ThrowOnceJobStore.LastInstance!.MaxConsecutiveFireFailures.Should().Be(2);
        }
        finally
        {
            await scheduler.Shutdown();
        }
    }

    //////////////////////////////////////////////////////////////////////////////////////////////
    // A running scheduler, on the machine's clock
    //////////////////////////////////////////////////////////////////////////////////////////////

    /// <summary>
    /// The issue's batch on a running scheduler: the calendar throws once, <c>first</c> runs once, its
    /// job-mate runs once <c>first</c> has, and <c>calendared</c> runs in a later round.
    /// </summary>
    [Test]
    public async Task ACalendarThatThrowsOnceCostsItsTriggerOneRoundAndTheBatchBesideItRunsOnce()
    {
        IScheduler scheduler = await new StdSchedulerFactory(SchedulerProperties("once", typeof(RAMJobStore))).GetScheduler();

        FaultyCalendar calendar = new FaultyCalendar();
        await scheduler.AddCalendar(CalendarName, calendar, replace: false, updateTriggers: false);

        // Far enough ahead that the loop has acquired all three before they are due, so they are one
        // batch; the same time for all three, so that the priorities order them.
        DateTimeOffset due = DateTimeOffset.UtcNow.AddMilliseconds(500);
        await scheduler.AddJob(Job(serialJobKey, serial: true), replace: false);
        await scheduler.AddJob(Job(ordinaryJobKey, serial: false), replace: false);
        await scheduler.ScheduleJob(Trigger(firstKey, serialJobKey, due, priority: 10));
        await scheduler.ScheduleJob(Trigger(calendaredKey, ordinaryJobKey, due, priority: 5, onCalendar: true));
        await scheduler.ScheduleJob(Trigger(mateKey, serialJobKey, due, priority: 1));
        calendar.Fault.ThrowOnce();

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
        calendar.Fault.Thrown.Should().Be(1);
    }

    /// <summary>
    /// A calendar that throws on every fire of a trigger that leads every round, beside a job-mate on the
    /// same serial job: the trigger is set ERROR after the failures the store allows, and the job-mate
    /// runs.
    /// </summary>
    [Test]
    public async Task ACalendarThatAlwaysThrowsHasItsTriggerSetErrorAndItsSerialJobMateRuns()
    {
        IScheduler scheduler = await new StdSchedulerFactory(SchedulerProperties("always", typeof(RAMJobStore))).GetScheduler();

        FaultyCalendar calendar = new FaultyCalendar();
        await scheduler.AddCalendar(CalendarName, calendar, replace: false, updateTriggers: false);
        DateTimeOffset due = DateTimeOffset.UtcNow.AddMilliseconds(500);
        await scheduler.AddJob(Job(serialJobKey, serial: true), replace: false);
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
    }

    /// <summary>
    /// A store that fails the batch with something other than a <see cref="SchedulerException" /> is
    /// caught by the scheduler thread's outermost arm, which used to log it and release nothing, so the
    /// batch stayed reserved for a firing that never came (#3974).
    /// </summary>
    [Test]
    public async Task AStoreThatFailsTheBatchWithAnythingButASchedulerExceptionHasTheBatchReleased()
    {
        IScheduler scheduler = await new StdSchedulerFactory(SchedulerProperties("release", typeof(ThrowOnceJobStore))).GetScheduler();

        await scheduler.AddJob(Job(ordinaryJobKey, serial: false), replace: false);
        await scheduler.ScheduleJob(Trigger(firstKey, ordinaryJobKey, DateTimeOffset.UtcNow, priority: 5));

        await scheduler.Start();
        try
        {
            await WaitFor(() => RecordingJob.RunsOf(firstKey) > 0,
                "the trigger to run; its first batch failed, and only a release lets a later round acquire it");
        }
        finally
        {
            await scheduler.Shutdown(waitForJobsToComplete: true);
        }

        ThrowOnceJobStore.LastInstance!.Released.Should().Contain(firstKey, "the failed batch is released");
    }

    //////////////////////////////////////////////////////////////////////////////////////////////
    // Helpers
    //////////////////////////////////////////////////////////////////////////////////////////////

    private void FreezeClock()
    {
        SystemTime.UtcNow = () => clock.GetUtcNow();
    }

    /// <summary>
    /// <c>first</c> and its job-mate on the serial job, and <c>calendared</c> on the ordinary job and the
    /// faulty calendar, all due at once, <c>first</c> ahead by priority.
    /// </summary>
    private async Task GivenTheBatch()
    {
        await StoreJob(serialJobKey, serial: true);
        await StoreJob(ordinaryJobKey, serial: false);
        await Schedule(firstKey, serialJobKey, priority: 10);
        await Schedule(calendaredKey, ordinaryJobKey, priority: 5, onCalendar: true);
        await Schedule(mateKey, serialJobKey, priority: 1);
    }

    private async Task StoreJob(JobKey key, bool serial)
    {
        await store.StoreJob(Job(key, serial), replaceExisting: false);
    }

    private static IJobDetail Job(JobKey key, bool serial)
    {
        return serial
            ? JobBuilder.Create<SerialRecordingJob>().WithIdentity(key).StoreDurably().Build()
            : JobBuilder.Create<RecordingJob>().WithIdentity(key).StoreDurably().Build();
    }

    private async Task Schedule(TriggerKey key, JobKey jobKey, int priority = TriggerConstants.DefaultPriority, bool onCalendar = false)
    {
        IOperableTrigger trigger = (IOperableTrigger) TriggerBuilder.Create()
            .WithIdentity(key)
            .ForJob(jobKey)
            .StartAt(clock.GetUtcNow())
            .WithSimpleSchedule(x => x.WithInterval(TimeSpan.FromHours(1)).RepeatForever())
            .WithPriority(priority)
            .ModifiedByCalendar(onCalendar ? CalendarName : null)
            .Build();

        // Without the calendar, so the calendar is consulted only by the fires the tests count.
        trigger.ComputeFirstFireTimeUtc(null);
        await store.StoreTrigger(trigger, replaceExisting: false);
    }

    private static ITrigger Trigger(TriggerKey key, JobKey jobKey, DateTimeOffset due, int priority, bool onCalendar = false)
    {
        return TriggerBuilder.Create()
            .WithIdentity(key)
            .ForJob(jobKey)
            .StartAt(due)
            .WithSimpleSchedule(x => x.WithInterval(TimeSpan.FromHours(1)).RepeatForever())
            .WithPriority(priority)
            .ModifiedByCalendar(onCalendar ? CalendarName : null)
            .Build();
    }

    private async Task<List<IOperableTrigger>> Acquire()
    {
        return (await store.AcquireNextTriggers(clock.GetUtcNow().AddSeconds(1), 10, TimeSpan.Zero)).ToList();
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

        List<TriggerFiredResult> results = (await store.TriggersFired(acquired)).ToList();
        for (int i = 0; i < results.Count; i++)
        {
            if (results[i].TriggerFiredBundle == null)
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

    private static void ShouldBeAsItWas(IOperableTrigger? after, IOperableTrigger before)
    {
        after.Should().NotBeNull();
        after!.GetNextFireTimeUtc().Should().Be(before.GetNextFireTimeUtc(), "a failed fire does not move the trigger on");
        after.GetPreviousFireTimeUtc().Should().Be(before.GetPreviousFireTimeUtc(), "only a fire that happened is a previous one");
        ((ISimpleTrigger) after).TimesTriggered.Should().Be(((ISimpleTrigger) before).TimesTriggered,
            "a fire that failed is not counted as one");
    }

    private static NameValueCollection SchedulerProperties(string name, Type jobStoreType)
    {
        return new NameValueCollection
        {
            ["quartz.scheduler.instanceName"] = "throwing-calendar-" + name + "-" + Guid.NewGuid().ToString("N"),
            ["quartz.jobStore.type"] = jobStoreType.AssemblyQualifiedName,
            ["quartz.serializer.type"] = TestConstants.DefaultSerializerType,
            ["quartz.threadPool.maxConcurrency"] = "4",

            // Batched, so a running scheduler fires the calendared trigger beside the rest of its batch.
            ["quartz.scheduler.batchTriggerAcquisitionMaxCount"] = "4",
        };
    }

    private static async Task WaitFor(Func<bool> condition, string what)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow + waitLimit;
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (condition())
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
        private readonly object gate = new object();
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
    private sealed class ThrowingSimpleTrigger : SimpleTriggerImpl
    {
        private readonly CalendarFault fault;

        public ThrowingSimpleTrigger(CalendarFault fault)
        {
            this.fault = fault;
        }

        public override void Triggered(ICalendar? cal)
        {
            base.Triggered(cal);
            fault.Consult();
        }
    }

    /// <summary>
    /// The in-memory store, failing its first batch with an exception the scheduler does not expect, and
    /// recording what it is asked to release.
    /// </summary>
    public sealed class ThrowOnceJobStore : RAMJobStore
    {
        private int batches;

        public ThrowOnceJobStore()
        {
            LastInstance = this;
        }

        public static ThrowOnceJobStore? LastInstance { get; private set; }

        public ConcurrentQueue<TriggerKey> Released { get; } = new ConcurrentQueue<TriggerKey>();

        public static void Reset() => LastInstance = null;

        public override Task<IReadOnlyCollection<TriggerFiredResult>> TriggersFired(
            IReadOnlyCollection<IOperableTrigger> triggers,
            CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref batches) == 1)
            {
                throw new InvalidOperationException("the store is confused");
            }

            return base.TriggersFired(triggers, cancellationToken);
        }

        public override Task ReleaseAcquiredTrigger(IOperableTrigger trigger, CancellationToken cancellationToken = default)
        {
            Released.Enqueue(trigger.Key);
            return base.ReleaseAcquiredTrigger(trigger, cancellationToken);
        }
    }

    /// <summary>Counts its runs by trigger.</summary>
    public class RecordingJob : IJob
    {
        private static ConcurrentDictionary<TriggerKey, int> runs = new ConcurrentDictionary<TriggerKey, int>();

        public static void Reset() => runs = new ConcurrentDictionary<TriggerKey, int>();

        public static int RunsOf(TriggerKey key) => runs.TryGetValue(key, out int count) ? count : 0;

        public Task Execute(IJobExecutionContext context)
        {
            runs.AddOrUpdate(context.Trigger.Key, 1, (_, count) => count + 1);
            return Task.CompletedTask;
        }
    }

    [DisallowConcurrentExecution]
    public sealed class SerialRecordingJob : RecordingJob
    {
    }
}
