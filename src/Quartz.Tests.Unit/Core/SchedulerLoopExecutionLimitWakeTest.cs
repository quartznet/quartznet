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

using System.Collections.Concurrent;

using FakeItEasy;
using FakeItEasy.Core;

using Microsoft.Extensions.Time.Testing;

using Quartz.Core;
using Quartz.Extensibility;
using Quartz.Impl;

namespace Quartz.Tests.Unit.Core;

/// <summary>
/// A firing ending on this node wakes the scheduling loop when the execution-group slot it gives back
/// is one a due trigger was held back for, and only then (#4033).
/// </summary>
/// <remarks>
/// <para>
/// The clock is frozen, so the idle wait the loop parks in never ends on its own: a trigger the loop
/// fires after parking was fired because something woke the loop, and the only thing that happens
/// meanwhile is a job ending. The jobs wait on a gate the test opens by name.
/// </para>
/// <para>
/// The thread pool runs each firing on a task of its own and records when it has run to its end — the
/// run shell's outermost finally included, which is where a firing gives its slot back — so a test can
/// say that a completion did not wake the loop once the completion is over.
/// </para>
/// </remarks>
[NonParallelizable]
public sealed class SchedulerLoopExecutionLimitWakeTest
{
    private const string Batch = "batch";
    private const string Aside = "aside";

    private static readonly DateTimeOffset start = new(2026, 10, 7, 8, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan idleWaitTime = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan observationDeadline = TimeSpan.FromSeconds(30);

    private ArmingRecordingTimeProvider clock;
    private RAMJobStore store;
    private IThreadPool threadPool;
    private QuartzScheduler scheduler;
    private QuartzSchedulerThread thread;

    /// <summary>Each firing the thread pool was handed, once it has run to its end.</summary>
    private CallLog<object> ended;

    [SetUp]
    public async Task SetUp()
    {
        GatedJob.Reset();
        ended = new CallLog<object>();
        clock = new ArmingRecordingTimeProvider(new FakeTimeProvider(start));
        store = TestJobStores.Ram(timeProvider: clock);
        await store.Initialize(TestJobStores.Identity());

        threadPool = A.Fake<IThreadPool>();
        A.CallTo(() => threadPool.PoolSize).Returns(4);
        A.CallTo(() => threadPool.WaitForAvailableThreads(A<CancellationToken>.Ignored)).Returns(new ValueTask<int>(4));
        A.CallTo(() => threadPool.TryRunWithState(A<Func<object, ValueTask>>.Ignored, A<object>.Ignored, A<CancellationToken>.Ignored))
            .ReturnsLazily(RunOnATaskOfItsOwn);

        ScriptedJobRunShellFactory shellFactory = new();
        shellFactory.Initialize(A.Fake<IScheduler>());

        QuartzSchedulerResources resources = new()
        {
            Name = "limitWakeTest",
            InstanceId = "limitWakeTestInstance",
            IdleWaitTime = idleWaitTime,
            MaxBatchSize = 4,
            JobStore = store,
            ThreadPool = threadPool,
            JobRunShellFactory = shellFactory,
            TimeProvider = clock,
        };

        scheduler = new QuartzScheduler(resources);
        thread = new QuartzSchedulerThread(scheduler, resources);
    }

    [TearDown]
    public async Task TearDown()
    {
        // Every job still waiting is let go, so no firing outlives the test on the thread pool.
        GatedJob.ReleaseAll();
        await thread.Halt(wait: true);
        await thread.Shutdown();
        await store.Shutdown();
    }

    /// <summary>
    /// The issue's case: a group's one slot is taken, its other due trigger is held back, and the loop
    /// parks for its idle wait. The firing ending frees the slot, and nothing else would have told the
    /// loop: a completion with no instruction is not a scheduling change.
    /// </summary>
    /// <remarks>
    /// Run with the group named on the trigger, and with the limits standing the trigger group in for an
    /// execution group the trigger does not carry: the store names the group by the key the loop counts
    /// firings against, or the completion would be looked up under one key and the trigger held under
    /// another.
    /// </remarks>
    [TestCase(false)]
    [TestCase(true)]
    public async Task AFiringEndingWakesTheLoopWhenTheSlotItFreesIsOneADueTriggerWaitsFor(bool groupStoodInByTriggerGroup)
    {
        ExecutionLimitsBuilder limits = ExecutionLimitsBuilder.Create().ForGroup(Batch, 1);
        if (groupStoodInByTriggerGroup)
        {
            limits.UseTriggerGroupWhenUnset();
        }

        scheduler.SetExecutionLimits(limits.Build());
        await GivenDueTrigger("first", groupStoodInByTriggerGroup ? null : Batch, triggerGroup: Batch);
        await GivenDueTrigger("second", groupStoodInByTriggerGroup ? null : Batch, triggerGroup: Batch);

        StartLoop();

        await ShouldObserve(GatedJob.Started.Reaches(1), "the group's one slot goes to the first trigger");
        await ShouldObserve(clock.Armed(IdleWait), "the second trigger is held back by the full limit, so the loop parks in its idle wait");
        long wakes = thread.SchedulingWakes;

        GatedJob.Release("first");

        await ShouldObserve(GatedJob.Started.Reaches(2),
            "the first firing's end freed the slot the second trigger waited for, and on a clock that never moves nothing else could have ended the idle wait");
        GatedJob.Started.Entries.Should().Equal(["first", "second"]);
        thread.SchedulingWakes.Should().Be(wakes + 1, "the completion that freed the slot is the one wake");
        clock.GetUtcNow().Should().Be(start, "the clock never moved, so the idle wait did not end on its own");
    }

    /// <summary>
    /// A recovered firing of a trigger with no execution group holds a slot of the group of the trigger it
    /// replays, not of the recovery group (#4142): the store takes the slot under that group, and the loop
    /// counts the running firing under it, so the group's other due trigger waits until the replay ends.
    /// </summary>
    [Test]
    public async Task ARecoveredFiringHoldsTheSlotOfTheTriggerGroupItReplays()
    {
        scheduler.SetExecutionLimits(ExecutionLimitsBuilder.Create().ForGroup(Batch, 1).UseTriggerGroupWhenUnset().Build());
        await GivenDueRecoveryOf(Batch, "recover_dead-node_1");
        await GivenDueTrigger("second", executionGroup: null, triggerGroup: Batch);

        StartLoop();

        await ShouldObserve(GatedJob.Started.Reaches(1), "the replay is first in line, and takes the batch group's one slot");
        await ShouldObserve(clock.Armed(IdleWait), "the batch group's other trigger is held back, so the loop parks in its idle wait");

        // Counted on the thread pool rather than on the jobs, which start on tasks of their own: the loop
        // hands a firing over before it parks, so this count is settled once it has.
        Fake.GetCalls(threadPool).Count(call => call.Method.Name == nameof(IThreadPool.TryRunWithState)).Should().Be(1,
            "the running replay is counted under the batch group, whose limit of one it fills; counted under the recovery group, "
            + "the next round would have run the second trigger beside it");
        GatedJob.Started.Entries.Should().Equal(["recover_dead-node_1"]);

        GatedJob.Release("recover_dead-node_1");

        await ShouldObserve(GatedJob.Started.Reaches(2), "the replay's end gives the batch group's slot back, and wakes the loop for the trigger waiting on it");
        GatedJob.Started.Entries[^1].Should().Be("second");
    }

    /// <summary>
    /// A completion is a wake only for the group whose limit held something back. Another group's firing
    /// ending frees a slot nothing is waiting for, and waking the loop for it would be a round for nothing
    /// on every completion while the first group stays full.
    /// </summary>
    [Test]
    public async Task AFiringOfAGroupWhoseLimitHeldNothingBackDoesNotWakeTheLoop()
    {
        scheduler.SetExecutionLimits(ExecutionLimitsBuilder.Create().ForGroup(Batch, 1).ForGroup(Aside, 1).Build());
        await GivenDueTrigger("first", Batch);
        await GivenDueTrigger("second", Batch);
        await GivenDueTrigger("aside", Aside);

        StartLoop();

        await ShouldObserve(GatedJob.Started.Reaches(2), "each group's one slot goes to its first trigger");
        await ShouldObserve(clock.Armed(IdleWait), "the batch group's second trigger is held back, so the loop parks");
        GatedJob.Started.Entries.Should().BeEquivalentTo(["first", "aside"], "the premise: the batch group is full and the aside group's one firing runs beside it");
        long wakes = thread.SchedulingWakes;

        GatedJob.Release("aside");
        await ShouldObserve(ended.Reaches(1), "the aside firing runs to its end and gives its slot back");

        thread.SchedulingWakes.Should().Be(wakes, "the slot given back is the aside group's, which nothing is waiting for");
        GatedJob.Started.Count.Should().Be(2, "the loop is still parked, and the batch group is still full");

        GatedJob.Release("first");

        await ShouldObserve(GatedJob.Started.Reaches(3), "the batch firing's end is the completion that frees the slot the second trigger waits for");
        GatedJob.Started.Entries[^1].Should().Be("second");
    }

    /// <summary>
    /// What the fix costs a scheduler that limits nothing: nothing. No group is ever at a limit, so no
    /// completion is ever a wake, and the loop is not asked to go round for every firing that ends.
    /// </summary>
    [Test]
    public async Task WithNoLimitAFiringEndingDoesNotWakeTheLoop()
    {
        await GivenDueTrigger("first", executionGroup: null);
        await GivenDueTrigger("second", executionGroup: null);

        StartLoop();

        await ShouldObserve(GatedJob.Started.Reaches(2), "nothing limits the two, so both fire at once");
        await ShouldObserve(clock.Armed(IdleWait), "nothing is left to fire, so the loop parks in its idle wait");
        long wakes = thread.SchedulingWakes;

        GatedJob.Release("first");
        GatedJob.Release("second");
        await ShouldObserve(ended.Reaches(2), "both firings run to their end");

        thread.SchedulingWakes.Should().Be(wakes, "a completion is a wake only when a limit held a due trigger back, and no limit is configured");
    }

    private void StartLoop()
    {
        thread.TogglePause(pause: false);
        thread.Start();
    }

    /// <summary>
    /// Runs the firing on a task of its own, as a thread pool does, and records it once it has ended —
    /// after the run shell's outermost finally, which is where the firing gives its slot back.
    /// </summary>
    private ValueTask<bool> RunOnATaskOfItsOwn(IFakeObjectCall call)
    {
        Func<object, ValueTask> run = (Func<object, ValueTask>) call.Arguments[0];
        object state = call.Arguments[1];
        _ = Task.Run(async () =>
        {
            await run(state);
            ended.Record(state);
        });

        return new ValueTask<bool>(true);
    }

    private async Task GivenDueTrigger(string name, string executionGroup, string triggerGroup = "loop")
    {
        IJobDetail job = JobBuilder.Create<GatedJob>()
            .WithIdentity(name, "loop")
            .Build();

        // On the test's clock: a trigger built to start now on the wall clock is due days after it.
        IOperableTrigger trigger = (IOperableTrigger) TriggerBuilder.Create()
            .WithIdentity(name, triggerGroup)
            .ForJob(job)
            .WithExecutionGroup(executionGroup)
            .StartAt(start)
            .Build();

        trigger.ComputeFirstFireTimeUtc(calendar: null);
        await store.ScheduleJob(job, trigger);
    }

    /// <summary>
    /// A recovery trigger as the ADO.NET store builds one: in the recovery group, with no execution group,
    /// and with the replayed trigger's key in its data map. Ahead of every other due trigger by priority.
    /// </summary>
    private async Task GivenDueRecoveryOf(string originalTriggerGroup, string name)
    {
        IJobDetail job = JobBuilder.Create<GatedJob>()
            .WithIdentity(name, "loop")
            .Build();

        IOperableTrigger trigger = (IOperableTrigger) TriggerBuilder.Create()
            .WithIdentity(name, SchedulerConstants.DefaultRecoveryGroup)
            .ForJob(job)
            .WithPriority(10)
            .UsingJobData(SchedulerConstants.FailedJobOriginalTriggerName, "original")
            .UsingJobData(SchedulerConstants.FailedJobOriginalTriggerGroup, originalTriggerGroup)
            .StartAt(start)
            .Build();

        trigger.ComputeFirstFireTimeUtc(calendar: null);
        await store.ScheduleJob(job, trigger);
    }

    /// <summary>
    /// The loop's idle wait on its scheduling timer, the first timer it creates: the idle wait time less
    /// up to a fifth of it.
    /// </summary>
    private static bool IdleWait(TimerArming arming)
    {
        return arming.Timer == 0 && arming.DueTime <= idleWaitTime && arming.DueTime >= idleWaitTime * 0.8;
    }

    private static async Task ShouldObserve(Task observation, string because)
    {
        Func<Task> act = () => observation;
        await act.Should().CompleteWithinAsync(observationDeadline, because);
    }

    /// <summary>
    /// A job that runs until the test lets it go, by the name of the trigger that fired it.
    /// </summary>
    public sealed class GatedJob : IJob
    {
        private static readonly ConcurrentDictionary<string, TaskCompletionSource> gates = new(StringComparer.Ordinal);

        /// <summary>The triggers whose firings have started running, in the order they did.</summary>
        public static CallLog<string> Started { get; private set; } = new();

        public static void Reset()
        {
            gates.Clear();
            Started = new CallLog<string>();
        }

        public static void Release(string triggerName) => Gate(triggerName).TrySetResult();

        public static void ReleaseAll()
        {
            foreach (TaskCompletionSource gate in gates.Values)
            {
                gate.TrySetResult();
            }
        }

        public async ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
        {
            string name = context.Trigger.Key.Name;
            Started.Record(name);
            await Gate(name).Task.WaitAsync(cancellationToken);
        }

        private static TaskCompletionSource Gate(string triggerName)
        {
            return gates.GetOrAdd(triggerName, static _ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));
        }
    }
}
