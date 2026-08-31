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

using Microsoft.Extensions.Time.Testing;

using Quartz.Extensibility;
using Quartz.Impl;
using Quartz.Impl.Triggers;
using Quartz.Jobs;

namespace Quartz.Tests.Unit.Impl;

/// <summary>
/// The in-memory store raises its notifications with its lock released, so a listener may call back
/// into the scheduler from inside one.
/// </summary>
/// <remarks>
/// <para>
/// A notification runs listener code on the calling thread, and a listener is entitled to do what any
/// other caller does: pause a trigger, reschedule one, ask the store a question. That call goes back
/// through the scheduler and into the store it came from, and the store's lock is a
/// <see cref="SemaphoreSlim" /> — not re-entrant, so a notification raised from inside the critical
/// section deadlocks the caller against itself. That is #3472; the misfire notification predated the
/// rule the trigger-in-error notifications were written to.
/// </para>
/// <para>
/// Each test drives the store directly rather than through a scheduler, because a scheduler adds
/// nothing here: <c>SchedulerSignalerImpl</c> forwards to the listeners and the listener's call comes
/// straight back to <see cref="RAMJobStore" />. Driving the store makes the re-entrant call the test
/// writes the same call the listener would make, and makes the misfire happen at a decided moment
/// rather than whenever a scheduler thread gets round to it.
/// </para>
/// </remarks>
[TestFixture]
public sealed class RAMJobStoreNotifiesAfterUnlockTest
{
    /// <summary>
    /// How long a store operation gets before the test calls it deadlocked. Long enough that a loaded
    /// build agent does not fail it, short enough that a deadlock is reported rather than hung on —
    /// nothing here waits for time to pass, so a passing run never spends any of it.
    /// </summary>
    private static readonly TimeSpan DeadlockTimeout = TimeSpan.FromSeconds(30);

    private static readonly DateTimeOffset Now = new DateTimeOffset(2026, 8, 26, 12, 0, 0, TimeSpan.Zero);

    private static readonly JobKey MisfiringJobKey = new JobKey("misfiring", "jobs");
    private static readonly JobKey OtherJobKey = new JobKey("other", "jobs");
    private static readonly TriggerKey OverdueKey = new TriggerKey("overdue", "triggers");
    private static readonly TriggerKey OtherKey = new TriggerKey("other", "triggers");

    private FakeTimeProvider clock;
    private ReentrantSignaler signaler;
    private RAMJobStore store;

    [SetUp]
    public async Task BuildStore()
    {
        clock = new FakeTimeProvider(Now);
        signaler = new ReentrantSignaler();
        store = TestJobStores.Ram(signaler, clock);
        signaler.Store = store;

        await store.Initialize(TestJobStores.Identity());
    }

    [TearDown]
    public async Task ShutDownStore()
    {
        await store.Shutdown();
    }

    [Test]
    public async Task AMisfireAppliedWhileAcquiringLetsTheListenerBackIntoTheStore()
    {
        await GivenAnOverdueTrigger();
        await GivenATriggerToPauseFromTheListener();

        signaler.OnMisfire = s => s.PauseTrigger(OtherKey);

        Func<Task<List<IOperableTrigger>>> acquire = async () => await store.AcquireNextTriggers(Acquisition());

        await acquire.Should().CompleteWithinAsync(DeadlockTimeout,
            "a listener that pauses a trigger from inside TriggerMisfired is doing what any listener may "
            + "do, and the store must not be holding the lock that call needs");

        signaler.LockHeldWhenNotified.Should().NotContain(true,
            "the store's critical section is over before a notification runs listener code");

        (await store.GetTriggerState(OtherKey)).Should().Be(TriggerState.Paused,
            "the listener's call reached the store and took effect, rather than being swallowed");
    }

    [Test]
    public async Task AMisfireAppliedWhileResumingLetsTheListenerBackIntoTheStore()
    {
        await GivenAnOverdueTrigger();
        await GivenATriggerToPauseFromTheListener();

        await store.PauseTrigger(OverdueKey);

        signaler.OnMisfire = s => s.PauseTrigger(OtherKey);

        Func<Task<bool>> resume = async () => await store.ResumeTrigger(OverdueKey);

        (await resume.Should().CompleteWithinAsync(DeadlockTimeout,
            "resuming a trigger settles its misfire debt, and the notification that goes with it must "
            + "not be raised from inside the lock"))
            .Which.Should().BeTrue("the trigger was paused, so resuming it moves it");

        signaler.Misfired.Should().ContainSingle(
            "resuming an overdue trigger applies its misfire policy exactly once")
            .Which.Key.Should().Be(OverdueKey);

        (await store.GetTriggerState(OtherKey)).Should().Be(TriggerState.Paused);
    }

    [Test]
    public async Task AMisfireAppliedWhileUnblockingLetsTheListenerBackIntoTheStore()
    {
        // The path #3463 added: a trigger blocked behind a [DisallowConcurrentExecution] job cannot be
        // acquired and is not swept, so the completion that unblocks it is where its misfire policy
        // runs — inside TriggeredJobComplete, and so inside the store's lock.
        IJobDetail job = JobBuilder.Create()
            .OfType<NonConcurrentJob>()
            .WithIdentity(MisfiringJobKey)
            .Build();

        SimpleTriggerImpl running = Trigger("running", MisfiringJobKey, Now.AddSeconds(5));
        await store.ScheduleJob(job, running);

        List<IOperableTrigger> acquired = await store.AcquireNextTriggers(Acquisition());
        IOperableTrigger firing = acquired.Should().ContainSingle(
            "the trigger that does the blocking has to be handed over before it can fire").Subject;

        SimpleTriggerImpl blocked = Trigger(OverdueKey.Name, MisfiringJobKey, Now.AddHours(-1), OverdueKey.Group);
        await store.AddTrigger(blocked);

        await store.TriggersFired([firing]);

        (await store.GetTriggerState(OverdueKey)).Should().Be(TriggerState.Blocked,
            "firing one trigger of a job that forbids concurrent execution blocks the rest of them");

        await GivenATriggerToPauseFromTheListener();

        signaler.OnMisfire = s => s.PauseTrigger(OtherKey);

        Func<Task> complete = async () => await store.TriggeredJobComplete(firing, job, SchedulerInstruction.NoInstruction);

        await complete.Should().CompleteWithinAsync(DeadlockTimeout,
            "unblocking an overdue trigger applies its misfire policy, and the notification that goes "
            + "with it must not be raised from inside the lock either");

        signaler.Misfired.Should().ContainSingle(
            "the one unblocked trigger was overdue, so one misfire is announced")
            .Which.Key.Should().Be(OverdueKey);

        signaler.LockHeldWhenNotified.Should().NotContain(true,
            "the store's critical section is over before a notification runs listener code");

        (await store.GetTriggerState(OtherKey)).Should().Be(TriggerState.Paused);
    }

    [Test]
    public async Task AMisfireIsAnnouncedOnceAndBeforeTheAcquisitionHandsTheTriggerOver()
    {
        await GivenAnOverdueTrigger();

        List<IOperableTrigger> acquired = await store.AcquireNextTriggers(Acquisition());
        signaler.Observed.Add("acquired");

        acquired.Should().ContainSingle(
            "FireNow reschedules the missed firing to now, so the same acquisition pass picks it up")
            .Which.Key.Should().Be(OverdueKey);

        signaler.Observed.Should().Equal(["misfired", "acquired"],
            "the notification is raised before the batch is handed back, which is what keeps the "
            + "announcement of a misfire ahead of the firing it rescheduled");

        signaler.LockHeldWhenNotified.Should().NotContain(true,
            "the announcement comes before the batch but after the lock, not before both");

        signaler.Misfired.Should().ContainSingle("the policy ran once, so it is announced once")
            .Which.NextFireTimeUtc.Should().Be(Now.AddHours(-1),
            "the listener is told about the trigger as it was when it misfired, not as the policy left it");

        await store.TriggersFired(acquired);

        signaler.Misfired.Should().ContainSingle(
            "firing the rescheduled trigger is not a second misfire");

        (await store.AcquireNextTriggers(Acquisition())).Should().BeEmpty(
            "the trigger had one firing in it, so there is nothing left to acquire");

        signaler.Misfired.Should().ContainSingle(
            "a later acquisition pass does not re-announce a misfire that has already been settled");
    }

    [Test]
    public async Task ADeletedJobIsAnnouncedWithTheLockFree()
    {
        await GivenAnOverdueTrigger();

        signaler.OnJobDeleted = s => s.PauseTrigger(OverdueKey);

        SimpleTriggerImpl onlyTrigger = Trigger("only", OtherJobKey, Now.AddMinutes(5), "triggers");
        await store.ScheduleJob(
            JobBuilder.Create().OfType<NoOpJob>().WithIdentity(OtherJobKey).Build(),
            onlyTrigger);

        Func<Task<bool>> unschedule = async () => await store.DeleteTrigger(onlyTrigger.Key);

        (await unschedule.Should().CompleteWithinAsync(DeadlockTimeout,
            "removing the last trigger of a non-durable job deletes the job and announces it, and that "
            + "announcement is listener code too"))
            .Which.Should().BeTrue();

        signaler.JobsDeleted.Should().Equal([OtherJobKey]);
        signaler.LockHeldWhenNotified.Should().NotContain(true,
            "the store's critical section is over before a notification runs listener code");

        (await store.GetTriggerState(OverdueKey)).Should().Be(TriggerState.Paused,
            "the listener's call reached the store");
    }

    [Test]
    public async Task ATriggerParkedInErrorIsAnnouncedAfterTheChangeThatCausedIt()
    {
        IJobDetail job = JobBuilder.Create()
            .OfType<NoOpJob>()
            .WithIdentity(MisfiringJobKey)
            .Build();

        SimpleTriggerImpl trigger = Trigger("failing", MisfiringJobKey, Now.AddSeconds(5));
        await store.ScheduleJob(job, trigger);

        List<IOperableTrigger> acquired = await store.AcquireNextTriggers(Acquisition());
        await store.TriggersFired(acquired);

        signaler.OnTriggerInError = s => s.GetTriggerState(trigger.Key).AsTask();

        Func<Task> complete = async () => await store.TriggeredJobComplete(
            acquired[0], job, SchedulerInstruction.SetTriggerError);

        await complete.Should().CompleteWithinAsync(DeadlockTimeout,
            "a listener told a trigger is in error may ask the store about it, which needs the lock");

        signaler.Observed.Should().Equal(["scheduling-change", "trigger-in-error"],
            "the scheduler thread is poked first and the listeners told after, which is the order the "
            + "store raised them in when only the error notification was deferred");

        signaler.TriggerStateSeenByListener.Should().Be(TriggerState.Error,
            "the state the notification announces is already visible to anyone who asks");
    }

    private async Task GivenAnOverdueTrigger()
    {
        IJobDetail job = JobBuilder.Create()
            .OfType<NoOpJob>()
            .WithIdentity(MisfiringJobKey)
            .StoreDurably()
            .Build();

        await store.AddJob(job);
        await store.AddTrigger(
            Trigger(OverdueKey.Name, MisfiringJobKey, Now.AddHours(-1), OverdueKey.Group));
    }

    private async Task GivenATriggerToPauseFromTheListener()
    {
        IJobDetail job = JobBuilder.Create()
            .OfType<NoOpJob>()
            .WithIdentity(OtherJobKey)
            .StoreDurably()
            .Build();

        await store.AddJob(job);
        await store.AddTrigger(
            Trigger(OtherKey.Name, OtherJobKey, Now.AddHours(1), OtherKey.Group));
    }

    private SimpleTriggerImpl Trigger(string name, JobKey jobKey, DateTimeOffset startAt, string group = "triggers")
    {
        SimpleTriggerImpl trigger = new SimpleTriggerImpl(clock)
        {
            Key = new TriggerKey(name, group),
            JobKey = jobKey,
            StartTimeUtc = startAt,
            RepeatCount = 0,
            MisfireInstructionCode = MisfireInstruction.SimpleTrigger.FireNow,
        };

        trigger.ComputeFirstFireTimeUtc(null);
        return trigger;
    }

    private static TriggerAcquisitionRequest Acquisition() => new TriggerAcquisitionRequest
    {
        NoLaterThan = Now.AddMinutes(1),
        MaxCount = 5,
        TimeWindow = TimeSpan.Zero,
    };

    [DisallowConcurrentExecution]
    private sealed class NonConcurrentJob : IJob
    {
        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
    }

    /// <summary>
    /// A signaler that does what a listener is entitled to do: call back into the store that is
    /// notifying it, and record whether that store was still inside its critical section at the time.
    /// </summary>
    /// <remarks>
    /// On a store that notifies from inside its lock the callback never returns, so a test that uses
    /// one fails on its completion assertion rather than on anything it asserts afterwards.
    /// </remarks>
    private sealed class ReentrantSignaler : ISchedulerSignaler
    {
        public RAMJobStore Store { get; set; }

        public Func<RAMJobStore, ValueTask<bool>> OnMisfire { get; set; }

        public Func<RAMJobStore, ValueTask<bool>> OnJobDeleted { get; set; }

        public Func<RAMJobStore, Task<TriggerState>> OnTriggerInError { get; set; }

        /// <summary>Every notification, in the order it arrived.</summary>
        public List<string> Observed { get; } = [];

        public List<ITrigger> Misfired { get; } = [];

        public List<JobKey> JobsDeleted { get; } = [];

        public List<bool> LockHeldWhenNotified { get; } = [];

        public TriggerState? TriggerStateSeenByListener { get; private set; }

        public async ValueTask NotifyTriggerListenersMisfired(ITrigger trigger, CancellationToken cancellationToken = default)
        {
            Record("misfired");
            Misfired.Add(trigger);

            if (OnMisfire is not null)
            {
                await OnMisfire(Store);
            }
        }

        public ValueTask NotifySchedulerListenersFinalized(ITrigger trigger, CancellationToken cancellationToken = default)
        {
            Record("finalized");
            return default;
        }

        public async ValueTask NotifySchedulerListenersJobDeleted(JobKey jobKey, CancellationToken cancellationToken = default)
        {
            Record("job-deleted");
            JobsDeleted.Add(jobKey);

            if (OnJobDeleted is not null)
            {
                await OnJobDeleted(Store);
            }
        }

        public async ValueTask NotifySchedulerListenersTriggerInError(TriggerKey triggerKey, CancellationToken cancellationToken = default)
        {
            Record("trigger-in-error");

            if (OnTriggerInError is not null)
            {
                TriggerStateSeenByListener = await OnTriggerInError(Store);
            }
        }

        public ValueTask NotifySchedulerListenersTriggersInError(JobKey jobKey, CancellationToken cancellationToken = default)
        {
            Record("triggers-in-error");
            return default;
        }

        public ValueTask SignalSchedulingChange(DateTimeOffset? candidateNewNextFireTimeUtc, CancellationToken cancellationToken = default)
        {
            Record("scheduling-change");
            return default;
        }

        public ValueTask NotifySchedulerListenersError(SchedulerErrorContext errorContext, CancellationToken cancellationToken = default)
        {
            Record("error");
            return default;
        }

        private void Record(string notification)
        {
            Observed.Add(notification);
            LockHeldWhenNotified.Add(Store.LockHeld);
        }
    }
}
