using FakeItEasy;

using Quartz.Core;
using Quartz.Extensibility;
using Quartz.Impl;

namespace Quartz.Tests.Unit.Core;

/// <summary>
/// What <see cref="QuartzSchedulerThread" /> does with a round its store fired as it acquired it (#3864):
/// the fired triggers are dispatched at once, with no <c>TriggersFired</c> call behind them, a result that
/// did not fire is released as any other, and the pending ones are waited for and fired as an acquired
/// batch always was.
/// </summary>
/// <remarks>
/// The store is the in-memory one behind a decorator that opts in to firing on acquisition by forwarding
/// the member to it, and records what the loop asks of it. As in <c>QuartzSchedulerThreadLoopTest</c>, the
/// thread is constructed on a scheduler whose own loop is never started, and every assertion is about a
/// call the loop made.
/// </remarks>
[TestFixture]
[NonParallelizable]
public sealed class QuartzSchedulerThreadFireOnAcquireTest
{
    private static readonly TimeSpan observationDeadline = TimeSpan.FromSeconds(30);

    private const int AvailableThreads = 4;

    private FiringOnAcquireStore store;
    private IThreadPool threadPool;
    private ScriptedJobRunShellFactory shellFactory;
    private QuartzSchedulerResources resources;
    private QuartzScheduler scheduler;
    private QuartzSchedulerThread thread;

    [SetUp]
    public async Task SetUp()
    {
        store = new FiringOnAcquireStore();
        await store.Initialize(TestJobStores.Identity());

        threadPool = A.Fake<IThreadPool>();
        A.CallTo(() => threadPool.PoolSize).Returns(AvailableThreads);
        A.CallTo(() => threadPool.WaitForAvailableThreads(A<CancellationToken>.Ignored))
            .Returns(new ValueTask<int>(AvailableThreads));

        // Accepted but never run, so a dispatched firing stays in flight for the rest of the test.
        A.CallTo(() => threadPool.TryRunWithState(A<Func<object, ValueTask>>.Ignored, A<object>.Ignored, A<CancellationToken>.Ignored))
            .Returns(new ValueTask<bool>(true));

        shellFactory = new ScriptedJobRunShellFactory();

        resources = new QuartzSchedulerResources
        {
            Name = "fireOnAcquireLoopTest",
            InstanceId = "fireOnAcquireLoopTestInstance",
            IdleWaitTime = TimeSpan.FromSeconds(1),
            MaxBatchSize = 5,

            // Wide enough that a trigger due shortly after the round's first is in its batch, pending.
            BatchTimeWindow = TimeSpan.FromSeconds(5),
            JobStore = store,
            ThreadPool = threadPool,
            JobRunShellFactory = shellFactory,
        };

        scheduler = new QuartzScheduler(resources);
        shellFactory.Initialize(A.Fake<IScheduler>());
        thread = new QuartzSchedulerThread(scheduler, resources);
    }

    [TearDown]
    public async Task TearDown()
    {
        await thread.Halt(wait: true);
        await thread.Shutdown();
        await store.Shutdown();
    }

    /// <summary>
    /// Triggers the store fired as it acquired them are handed to the thread pool straight away: the round
    /// trip that fired them was the acquisition, and there is no second one.
    /// </summary>
    [Test]
    public async Task FiredTriggersAreDispatchedWithoutASecondCallToTheStore()
    {
        IReadOnlyList<TriggerKey> scheduled = await GivenScheduledJobs(3, DateTimeOffset.UtcNow);

        StartLoop();

        await ShouldObserve(shellFactory.Created.Reaches(3), "the three due triggers came back fired, and fired triggers are run");

        shellFactory.Created.Entries.Should().BeEquivalentTo(scheduled);
        store.TriggersFiredCalls.Count.Should().Be(0,
            "what the store fired as it acquired it is not fired again; the round was one call");
        store.Releases.Count.Should().Be(0, "every trigger fired, so there is nothing to release");
    }

    /// <summary>
    /// A result in the fired part of the round that did not fire is released, exactly as the same result
    /// from <c>TriggersFired</c> is, and the triggers beside it are dispatched.
    /// </summary>
    [Test]
    public async Task AFiredResultThatFailedIsReleasedAndTheRestAreDispatched()
    {
        await GivenScheduledJobs(3, DateTimeOffset.UtcNow);

        TriggerKey failed = null;
        store.OnAcquireAndFire = async (call, callThrough) =>
        {
            TriggerAcquisitionResult round = await callThrough();
            if (call == 1)
            {
                failed = round.Due[0].Key;
                round.Fired[0] = TriggerFiredResult.Failed(new FiredTriggerWriteException());
            }

            return round;
        };

        StartLoop();

        await ShouldObserve(shellFactory.Created.Reaches(2), "one failed fire must not cost the rest of the round their dispatch");
        await ShouldObserve(store.Releases.Reaches(1), "the trigger whose fire came back failed is the scheduler's to release");

        store.Releases.Entries.Should().Equal([failed]);
        shellFactory.Created.Entries.Should().NotContain(failed);
    }

    /// <summary>
    /// The due part of a round is dispatched at once, and the part due later in the batch window is waited
    /// for and fired with <c>TriggersFired</c>, as a batch acquired ahead of its time always was.
    /// </summary>
    [Test]
    public async Task PendingTriggersAreFiredWhenTheyAreDueAfterTheFiredOnesRun()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        IReadOnlyList<TriggerKey> due = await GivenScheduledJobs(1, now, namePrefix: "due-");
        IReadOnlyList<TriggerKey> later = await GivenScheduledJobs(1, now.AddMilliseconds(300), namePrefix: "later-");

        StartLoop();

        await ShouldObserve(shellFactory.Created.Reaches(2), "the due trigger runs at once and the later one when it is due");

        shellFactory.Created.Entries.Should().Equal([due[0], later[0]], "the round's fired trigger is dispatched before the loop waits for the pending one");
        store.TriggersFiredCalls.Entries.Should().ContainSingle().Which.Should().Equal(later,
            "only the pending trigger is fired by TriggersFired");
    }

    /// <summary>
    /// Something unexpected while the fired part is being dispatched ends the round, and the pending part,
    /// which nothing has fired, is released rather than left reserved (#3974).
    /// </summary>
    [Test]
    public async Task AnUnexpectedFailureDispatchingTheFiredPartReleasesThePendingPart()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        await GivenScheduledJobs(1, now, namePrefix: "due-");
        IReadOnlyList<TriggerKey> later = await GivenScheduledJobs(1, now.AddSeconds(2), namePrefix: "later-");

        shellFactory.OnCreate = (call, _) =>
        {
            if (call == 1)
            {
                throw new InvalidOperationException("the run shell factory is confused");
            }
        };

        StartLoop();

        await ShouldObserve(store.Releases.Reaches(1), "a pending trigger the round never fired is released when the round ends abnormally");

        store.Releases.Entries[0].Should().Be(later[0]);
    }

    private void StartLoop()
    {
        thread.Start();
        thread.TogglePause(pause: false);
    }

    private static async Task ShouldObserve(Task observation, string because)
    {
        Func<Task> act = () => observation;
        await act.Should().CompleteWithinAsync(observationDeadline, because);
    }

    private async Task<IReadOnlyList<TriggerKey>> GivenScheduledJobs(int count, DateTimeOffset at, string namePrefix = "")
    {
        List<TriggerKey> keys = new(count);
        for (int i = 0; i < count; i++)
        {
            IJobDetail job = JobBuilder.Create<LoopTestJob>()
                .WithIdentity($"{namePrefix}job{i}", "loop")
                .Build();

            IOperableTrigger trigger = (IOperableTrigger) TriggerBuilder.Create()
                .WithIdentity($"{namePrefix}trigger{i}", "loop")
                .ForJob(job)
                .StartAt(at)
                .Build();

            trigger.ComputeFirstFireTimeUtc(calendar: null);

            await store.ScheduleJob(job, trigger);
            keys.Add(trigger.Key);
        }

        return keys;
    }

    private sealed class LoopTestJob : IJob
    {
        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
    }

    /// <summary>A database failure the loop recognises as one, which it logs before releasing.</summary>
    private sealed class FiredTriggerWriteException() : System.Data.Common.DbException("the fired-trigger row could not be written");

    /// <summary>
    /// The in-memory store, firing on acquisition through a decorator that opts in by forwarding the
    /// member, and recording what the loop asks of it.
    /// </summary>
    private sealed class FiringOnAcquireStore()
        : DelegatingJobStore(new RAMJobStore(TestJobStores.LoggerFactory(), TestJobStores.Signaler(), TimeProvider.System))
    {
        private int acquireCalls;

        /// <summary>
        /// What each round should answer, given its 1-based number and the real store's answer;
        /// <see langword="null" /> lets the real store answer.
        /// </summary>
        public Func<int, Func<ValueTask<TriggerAcquisitionResult>>, ValueTask<TriggerAcquisitionResult>> OnAcquireAndFire { get; set; }

        public CallLog<List<TriggerKey>> TriggersFiredCalls { get; } = new();

        public CallLog<TriggerKey> Releases { get; } = new();

        public override ValueTask<TriggerAcquisitionResult> AcquireNextTriggersAndFireDue(TriggerAcquisitionRequest request, CancellationToken cancellationToken = default)
        {
            int call = Interlocked.Increment(ref acquireCalls);
            Func<int, Func<ValueTask<TriggerAcquisitionResult>>, ValueTask<TriggerAcquisitionResult>> script = OnAcquireAndFire;
            if (script is null)
            {
                return InnerJobStore.AcquireNextTriggersAndFireDue(request, cancellationToken);
            }

            return script(call, () => InnerJobStore.AcquireNextTriggersAndFireDue(request, cancellationToken));
        }

        public override ValueTask<List<TriggerFiredResult>> TriggersFired(IReadOnlyCollection<IOperableTrigger> triggers, CancellationToken cancellationToken = default)
        {
            TriggersFiredCalls.Record([.. triggers.Select(x => x.Key)]);
            return base.TriggersFired(triggers, cancellationToken);
        }

        public override ValueTask ReleaseAcquiredTrigger(IOperableTrigger trigger, CancellationToken cancellationToken = default)
        {
            Releases.Record(trigger.Key);
            return base.ReleaseAcquiredTrigger(trigger, cancellationToken);
        }
    }
}
