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

using FakeItEasy;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

using Quartz.Extensibility;
using Quartz.Impl;

namespace Quartz.Tests.Unit.Impl;

/// <summary>
/// The in-memory store acquires the next triggers and fires the ones already due under one hold of its
/// lock, and what it answers is what <c>AcquireNextTriggers</c> and <c>TriggersFired</c> would have
/// answered between them (#3864).
/// </summary>
public sealed class RAMJobStoreFireOnAcquireTest
{
    private const string Group = "fire-on-acquire";
    private const string CalendarName = "faulty";

    /// <summary><c>InMemoryJobStoreOptions.MaxConsecutiveFireFailures</c> as it ships.</summary>
    private const int DefaultMaxConsecutiveFireFailures = 5;

    private static readonly DateTimeOffset epoch = new(2031, 6, 17, 10, 0, 0, TimeSpan.Zero);

    private static readonly JobKey serialJobKey = new("serial", Group);
    private static readonly JobKey ordinaryJobKey = new("ordinary", Group);

    private FakeTimeProvider clock = null!;
    private ISchedulerSignaler signaler = null!;
    private RAMJobStore store = null!;

    [SetUp]
    public async Task BuildStore()
    {
        clock = new FakeTimeProvider(epoch);
        signaler = A.Fake<ISchedulerSignaler>();
        store = new RAMJobStore(NullLoggerFactory.Instance, signaler, clock);
        await store.Initialize(TestJobStores.Identity());

        await store.AddJob(JobBuilder.Create<NoOpJob>().WithIdentity(ordinaryJobKey).StoreDurably().Build());
        await store.AddJob(JobBuilder.Create<SerialJob>().WithIdentity(serialJobKey).StoreDurably().Build());
    }

    [TearDown]
    public async Task ShutDownStore()
    {
        await store.Shutdown();
    }

    /// <summary>
    /// The triggers due now come back fired, and the one due later in the batch window comes back pending,
    /// acquired and reserved, for the scheduler to fire when it is due.
    /// </summary>
    [Test]
    public async Task DueTriggersComeBackFiredAndTheRestOfTheBatchPending()
    {
        await Schedule("due-1", ordinaryJobKey, epoch);
        await Schedule("due-2", ordinaryJobKey, epoch);
        await Schedule("later", ordinaryJobKey, epoch.AddMilliseconds(500));

        TriggerAcquisitionResult round = await store.AcquireNextTriggersAndFireDue(Request(maxCount: 10, window: TimeSpan.FromSeconds(1)));

        round.Due.Select(x => x.Key.Name).Should().Equal(["due-1", "due-2"]);
        round.Fired.Should().HaveCount(2, "one result per due trigger, at the same index")
            .And.OnlyContain(x => x.TriggerFiredBundle != null, "both were due, and nothing stops them firing");
        round.Fired[0].TriggerFiredBundle!.Trigger.Key.Should().Be(round.Due[0].Key);
        round.Fired[0].TriggerFiredBundle!.ScheduledFireTimeUtc.Should().Be(epoch);
        round.Pending.Select(x => x.Key.Name).Should().Equal(["later"],
            "due half a second from now, it is in the batch window but not due, so it waits to be fired when it is");

        (await FireInstances(FireInstanceState.Executing)).Should().BeEquivalentTo(["due-1", "due-2"],
            "a trigger fired as it was acquired is executing, as one fired by TriggersFired is");
        (await FireInstances(FireInstanceState.Acquired)).Should().Equal(["later"], "the pending one is reserved");

        clock.Advance(TimeSpan.FromMilliseconds(500));
        List<TriggerFiredResult> later = await store.TriggersFired(round.Pending);
        later.Should().ContainSingle().Which.TriggerFiredBundle.Should().NotBeNull(
            "a pending trigger is fired by TriggersFired, exactly as an acquired one always was");
    }

    /// <summary>
    /// Nothing due is a round of pending triggers only, and an empty store an empty round.
    /// </summary>
    [Test]
    public async Task ARoundWithNothingDueFiresNothing()
    {
        (await store.AcquireNextTriggersAndFireDue(Request(maxCount: 10))).Pending.Should().BeEmpty("the store holds no trigger yet");

        await Schedule("later", ordinaryJobKey, epoch.AddMilliseconds(500));

        TriggerAcquisitionResult round = await store.AcquireNextTriggersAndFireDue(Request(maxCount: 10));

        round.Due.Should().BeEmpty();
        round.Fired.Should().BeEmpty();
        round.Pending.Select(x => x.Key.Name).Should().Equal(["later"]);
        (await FireInstances(FireInstanceState.Executing)).Should().BeEmpty("nothing was due, so nothing fired");
    }

    /// <summary>
    /// A fire in the round blocks its serial job's other triggers, as a fire by <c>TriggersFired</c>
    /// does, and its completion lets go of them.
    /// </summary>
    [Test]
    public async Task AFireInTheRoundBlocksItsSerialJobMates()
    {
        await Schedule("first", serialJobKey, epoch, priority: 10);
        await Schedule("mate", serialJobKey, epoch, priority: 1);

        TriggerAcquisitionResult round = await store.AcquireNextTriggersAndFireDue(Request(maxCount: 10));

        round.Due.Select(x => x.Key.Name).Should().Equal(["first"], "a batch takes one trigger of a serial job");
        (await store.GetTriggerState(new TriggerKey("mate", Group))).Should().Be(TriggerState.Blocked);

        TriggerFiredBundle bundle = round.Fired[0].TriggerFiredBundle!;
        await store.TriggeredJobComplete(bundle.Trigger, bundle.JobDetail, SchedulerInstruction.NoInstruction);

        (await store.GetTriggerState(new TriggerKey("mate", Group))).Should().Be(TriggerState.Normal);
    }

    /// <summary>
    /// A calendar that throws fails its own trigger's fire in the round and nothing beside it (#3974): the
    /// trigger stays reserved for the scheduler to release, and after the failures in a row the store
    /// allows it is set ERROR.
    /// </summary>
    [Test]
    public async Task ACalendarThatThrowsFailsItsTriggerAloneAndIsCountedTowardsTheLimit()
    {
        FaultyCalendar calendar = new();
        await store.AddCalendar(CalendarName, calendar);
        await Schedule("ordinary", ordinaryJobKey, epoch, priority: 10);
        await Schedule("calendared", ordinaryJobKey, epoch, priority: 5, onCalendar: true);
        calendar.Throwing = true;

        TriggerKey calendared = new("calendared", Group);
        for (int failure = 1; failure <= DefaultMaxConsecutiveFireFailures; failure++)
        {
            TriggerAcquisitionResult round = await store.AcquireNextTriggersAndFireDue(Request(maxCount: 10));

            int index = round.Due.FindIndex(x => x.Key.Equals(calendared));
            index.Should().BeGreaterThanOrEqualTo(0, "the calendared trigger is due and leads its own rounds");
            round.Fired[index].Exception.Should().BeOfType<InvalidOperationException>("the result carries what the calendar threw");

            // What the scheduler thread does with a failed result.
            await store.ReleaseAcquiredTrigger(round.Due[index]);

            if (failure < DefaultMaxConsecutiveFireFailures)
            {
                (await store.GetTriggerState(calendared)).Should().Be(TriggerState.Normal, $"{failure} failure(s) is short of the limit");
            }
        }

        (await store.GetTriggerState(calendared)).Should().Be(TriggerState.Error, "the limit's failure sets the trigger ERROR, and the release leaves it");
        (await FireInstances(FireInstanceState.Executing)).Should().Equal(["ordinary"],
            "the trigger beside it fired in the first round, once, whatever the calendar did");
        A.CallTo(() => signaler.NotifySchedulerListenersTriggerInError(calendared, A<CancellationToken>._)).MustHaveHappenedOnceExactly();
    }

    private TriggerAcquisitionRequest Request(int maxCount, TimeSpan window = default)
    {
        return new TriggerAcquisitionRequest
        {
            NoLaterThan = clock.GetUtcNow().AddSeconds(1),
            MaxCount = maxCount,
            TimeWindow = window,
        };
    }

    private async Task Schedule(string name, JobKey jobKey, DateTimeOffset at, int priority = TriggerConstants.DefaultPriority, bool onCalendar = false)
    {
        IOperableTrigger trigger = (IOperableTrigger) TriggerBuilder.Create(clock)
            .WithIdentity(name, Group)
            .ForJob(jobKey)
            .StartAt(at)
            .WithSimpleSchedule(x => x.WithInterval(TimeSpan.FromHours(1)).RepeatForever())
            .WithPriority(priority)
            .WithCalendarName(onCalendar ? CalendarName : null)
            .Build();

        // Without the calendar, so that the calendar is consulted only by the fires the test counts.
        trigger.ComputeFirstFireTimeUtc(calendar: null);
        await store.AddTrigger(trigger);
    }

    private async Task<List<string>> FireInstances(FireInstanceState state)
    {
        PagedResult<FireInstance> instances = await store.QueryFireInstances(new FireInstanceQuery { State = state, Take = PagedQuery.All });
        return instances.Items.Select(x => x.TriggerKey.Name).ToList();
    }

    public sealed class NoOpJob : IJob
    {
        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
    }

    [DisallowConcurrentExecution]
    public sealed class SerialJob : IJob
    {
        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
    }

    /// <summary>Throws while it is told to, from both of the calendar members a fire consults.</summary>
    private sealed class FaultyCalendar : ICalendar
    {
        private readonly FaultyCalendar? origin;

        public FaultyCalendar()
        {
        }

        private FaultyCalendar(FaultyCalendar origin)
        {
            this.origin = origin;
        }

        public bool Throwing
        {
            get => origin?.Throwing ?? field;
            set => field = value;
        }

        public string? Description { get; set; }

        public ICalendar? CalendarBase { get; set; }

        public bool IsTimeIncluded(DateTimeOffset timeUtc) => Consult(true);

        public DateTimeOffset GetNextIncludedTimeUtc(DateTimeOffset timeUtc) => Consult(timeUtc);

        // The store keeps a clone of the calendar it is given; the clones answer to the original.
        public ICalendar Clone() => new FaultyCalendar(origin ?? this);

        private T Consult<T>(T answer)
        {
            if (Throwing)
            {
                throw new InvalidOperationException("The holiday feed is unreachable.");
            }

            return answer;
        }
    }
}
