using Quartz.Extensibility;
using Quartz.Impl;

namespace Quartz.Tests.Unit.Simpl;

public class TaskSchedulingThreadPoolTest
{
    [Test]
    public async Task MaxConcurrencyIsRespected()
    {
        CustomTaskSchedulingThreadPool threadPool = new CustomTaskSchedulingThreadPool(TaskScheduler.Default, 1);
        await threadPool.Initialize();

        List<string> logBook = [];
        TaskCompletionSource firstStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource releaseFirst = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource firstFinished = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource secondFinished = new(TaskCreationOptions.RunContinuationsAsynchronously);

        // Both work items are handed over before either is awaited: awaiting in between would
        // serialize them in the test itself, which is what used to make this assertion vacuous.
        ValueTask<bool> first = threadPool.TryRun(async () =>
        {
            lock (logBook)
            {
                logBook.Add("START #1");
            }

            firstStarted.TrySetResult();
            await releaseFirst.Task.ConfigureAwait(false);

            lock (logBook)
            {
                logBook.Add("END #1");
            }

            firstFinished.TrySetResult();
        });

        ValueTask<bool> second = threadPool.TryRun(async () =>
        {
            lock (logBook)
            {
                logBook.Add("START #2");
            }

            await Task.Yield();

            lock (logBook)
            {
                logBook.Add("END #2");
            }

            secondFinished.TrySetResult();
        });

        bool firstScheduled = await first;
        await firstStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));

        // The only slot is held by item 1 until it is released, so the second TryRun cannot have
        // handed its work over yet. This is the property the test exists to prove.
        second.IsCompleted.Should().BeFalse(
            "the single slot is still occupied, so the second work item must not have been scheduled yet");

        releaseFirst.TrySetResult();

        bool secondScheduled = await second;
        await Task.WhenAll(firstFinished.Task, secondFinished.Task).WaitAsync(TimeSpan.FromSeconds(10));

        firstScheduled.Should().BeTrue("a free slot has to accept the first work item");
        secondScheduled.Should().BeTrue("the second work item has to be scheduled once the slot frees up");

        string[] expectedOrder = ["START #1", "END #1", "START #2", "END #2"];
        logBook.Should().Equal(expectedOrder,
            "a max concurrency of 1 has to run the work items one after another rather than interleaving them");
    }

    [Test]
    public async Task TryRunRefusesWorkBeforeInitialization()
    {
        CustomTaskSchedulingThreadPool threadPool = new CustomTaskSchedulingThreadPool(TaskScheduler.Default, 1);

        bool ran = false;
        bool scheduled = await threadPool.TryRun(() =>
        {
            ran = true;
            return ValueTask.CompletedTask;
        });

        scheduled.Should().BeFalse("a pool without a semaphore or a task scheduler cannot accept work");
        ran.Should().BeFalse("refusing the work item means never invoking it");
    }

    [Test]
    public async Task TryRunRefusesWorkAfterShutdown()
    {
        CustomTaskSchedulingThreadPool threadPool = new CustomTaskSchedulingThreadPool(TaskScheduler.Default, 1);
        await threadPool.Initialize();
        await threadPool.Shutdown(waitForJobsToComplete: true);

        bool ran = false;
        bool scheduled = await threadPool.TryRun(() =>
        {
            ran = true;
            return ValueTask.CompletedTask;
        });

        scheduled.Should().BeFalse("work handed over after shutdown has to be refused rather than silently dropped");
        ran.Should().BeFalse("a refused work item must not run, since nothing will wait for it any more");
    }

    [TestCase(true)]
    [TestCase(false)]
    public async Task ShutdownOfAnUninitializedPoolDoesNotThrow(bool waitForJobsToComplete)
    {
        CustomTaskSchedulingThreadPool threadPool = new CustomTaskSchedulingThreadPool(TaskScheduler.Default, 1);

        Func<Task> act = async () => await threadPool.Shutdown(waitForJobsToComplete);

        await act.Should().NotThrowAsync(
            "a pool that was never initialized has no countdown, no semaphore and nothing running, so there is nothing to tear down");
    }

    [Test]
    public async Task WaitForAvailableThreadsReportsTheIdleCapacityAndZeroOtherwise()
    {
        CustomTaskSchedulingThreadPool threadPool = new CustomTaskSchedulingThreadPool(TaskScheduler.Default, 3);

        int beforeInitialize = await threadPool.WaitForAvailableThreads();
        beforeInitialize.Should().Be(0, "an uninitialized pool has no threads to offer");

        await threadPool.Initialize();

        int idle = await threadPool.WaitForAvailableThreads();
        idle.Should().BeGreaterThanOrEqualTo(1, "an idle pool has to report at least the one thread the caller waited for")
            .And.BeLessThanOrEqualTo(threadPool.MaxConcurrency, "the pool cannot offer more threads than it is allowed to run");

        await threadPool.Shutdown(waitForJobsToComplete: true);

        int afterShutdown = await threadPool.WaitForAvailableThreads();
        afterShutdown.Should().Be(0, "a shut down pool must report no capacity rather than block the scheduling loop");
    }

    [Test]
    public async Task ShutdownWaitingForJobsDoesNotReturnUntilInFlightWorkFinishes()
    {
        // A single slot, so that the capacity probe below cannot be satisfied by a free thread and
        // completes only once shutdown cancels - which is the barrier this test needs.
        CustomTaskSchedulingThreadPool threadPool = new CustomTaskSchedulingThreadPool(TaskScheduler.Default, 1);
        await threadPool.Initialize();

        using ManualResetEventSlim release = new ManualResetEventSlim(false);
        TaskCompletionSource started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        List<string> order = [];

        bool scheduled = await threadPool.TryRun(() =>
        {
            started.TrySetResult();
            release.Wait(TimeSpan.FromSeconds(30));

            lock (order)
            {
                order.Add("work item finished");
            }

            return ValueTask.CompletedTask;
        });

        scheduled.Should().BeTrue("an idle initialized pool has to accept the work item");
        await started.Task.WaitAsync(TimeSpan.FromSeconds(10));

        // Started before the shutdown, this cannot complete until shutdown cancels its token: the
        // only slot is held by the blocked work item.
        Task<int> capacityProbe = threadPool.WaitForAvailableThreads().AsTask();

        Task shutdown = Task.Run(async () =>
        {
            await threadPool.Shutdown(waitForJobsToComplete: true);

            lock (order)
            {
                order.Add("shutdown returned");
            }
        });

        int capacity = await capacityProbe.WaitAsync(TimeSpan.FromSeconds(10));
        capacity.Should().Be(0, "shutdown has cancelled the pool, so there is no capacity to report");

        shutdown.IsCompleted.Should().BeFalse(
            "Shutdown(waitForJobsToComplete: true) must not return while a work item is still running");

        release.Set();

        await shutdown.WaitAsync(TimeSpan.FromSeconds(10));

        string[] expectedOrder = ["work item finished", "shutdown returned"];
        order.Should().Equal(expectedOrder,
            "the shutdown may only return once the running work item has completed");
    }

    [Test]
    public async Task ShutdownWithoutWaitingReturnsWhileWorkIsStillRunning()
    {
        CustomTaskSchedulingThreadPool threadPool = new CustomTaskSchedulingThreadPool(TaskScheduler.Default, 2);
        await threadPool.Initialize();

        using ManualResetEventSlim release = new ManualResetEventSlim(false);
        TaskCompletionSource started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource finished = new(TaskCreationOptions.RunContinuationsAsynchronously);

        bool scheduled = await threadPool.TryRun(() =>
        {
            started.TrySetResult();
            release.Wait(TimeSpan.FromSeconds(30));
            finished.TrySetResult();
            return ValueTask.CompletedTask;
        });

        scheduled.Should().BeTrue("an idle initialized pool has to accept the work item");
        await started.Task.WaitAsync(TimeSpan.FromSeconds(10));

        // Shutdown is a blocking call inside a ValueTask, so it is pushed onto another thread to keep
        // a regression from hanging the test run instead of failing it.
        Task shutdown = Task.Run(async () => await threadPool.Shutdown(waitForJobsToComplete: false));
        await shutdown.WaitAsync(TimeSpan.FromSeconds(10));

        finished.Task.IsCompleted.Should().BeFalse(
            "Shutdown(waitForJobsToComplete: false) has to return without waiting for in-flight work");

        release.Set();
        await finished.Task.WaitAsync(TimeSpan.FromSeconds(10));
    }

    [Test]
    public async Task ShuttingDownTwiceInSequenceIsSafe()
    {
        CustomTaskSchedulingThreadPool threadPool = new CustomTaskSchedulingThreadPool(TaskScheduler.Default, 2);
        await threadPool.Initialize();

        await threadPool.Shutdown(waitForJobsToComplete: true);

        Func<Task> act = async () => await threadPool.Shutdown(waitForJobsToComplete: true);

        await act.Should().NotThrowAsync(
            "a repeated shutdown must not signal the countdown a second time, which would throw once it has already reached zero");
    }

    [Test]
    public async Task ShuttingDownConcurrentlyIsSafe()
    {
        CustomTaskSchedulingThreadPool threadPool = new CustomTaskSchedulingThreadPool(TaskScheduler.Default, 2);
        await threadPool.Initialize();

        Func<Task> act = async () =>
        {
            Task first = Task.Run(async () => await threadPool.Shutdown(waitForJobsToComplete: true));
            Task second = Task.Run(async () => await threadPool.Shutdown(waitForJobsToComplete: true));
            await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(10));
        };

        await act.Should().NotThrowAsync("two shutdowns racing each other still have to leave the pool shut down, not faulted");
    }

    [Test]
    public async Task DrainReportsThatItDrainedOnceRunningWorkFinishes()
    {
        CustomTaskSchedulingThreadPool threadPool = new CustomTaskSchedulingThreadPool(TaskScheduler.Default, 2);
        await threadPool.Initialize();

        using ManualResetEventSlim release = new ManualResetEventSlim(false);
        TaskCompletionSource started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        bool finished = false;

        bool scheduled = await threadPool.TryRun(() =>
        {
            started.TrySetResult();
            release.Wait(TimeSpan.FromSeconds(30));
            Volatile.Write(ref finished, true);
            return ValueTask.CompletedTask;
        });

        scheduled.Should().BeTrue("an idle initialized pool has to accept the work item");
        await started.Task.WaitAsync(TimeSpan.FromSeconds(10));

        using CancellationTokenSource deadline = new(TimeSpan.FromSeconds(30));
        ValueTask<bool> drain = threadPool.Drain(deadline.Token);

        drain.IsCompleted.Should().BeFalse("the work item is still running, so the drain cannot be over yet");

        release.Set();

        bool drained = await drain;

        drained.Should().BeTrue("the work item finished well inside the deadline, so the pool really did drain");
        Volatile.Read(ref finished).Should().BeTrue("a drain that reports success has to have waited for the work item to finish");
    }

    [Test]
    public async Task DrainReportsThatItGaveUpWhenTheDeadlineExpiresFirst()
    {
        CustomTaskSchedulingThreadPool threadPool = new CustomTaskSchedulingThreadPool(TaskScheduler.Default, 2);
        await threadPool.Initialize();

        using ManualResetEventSlim release = new ManualResetEventSlim(false);
        TaskCompletionSource started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource finished = new(TaskCreationOptions.RunContinuationsAsynchronously);

        await threadPool.TryRun(() =>
        {
            started.TrySetResult();
            release.Wait(TimeSpan.FromSeconds(30));
            finished.TrySetResult();
            return ValueTask.CompletedTask;
        });

        await started.Task.WaitAsync(TimeSpan.FromSeconds(10));

        using CancellationTokenSource deadline = new(TimeSpan.FromMilliseconds(50));

        bool drained = true;
        Func<Task> act = async () => drained = await threadPool.Drain(deadline.Token);

        await act.Should().NotThrowAsync(
            "a deadline that expires is an answer, not a failure - the caller still has the rest of its shutdown to run");

        drained.Should().BeFalse("the work item outlived the deadline, so the pool did not drain");
        finished.Task.IsCompleted.Should().BeFalse("giving up on the wait must not have waited for the work item after all");

        release.Set();
        await finished.Task.WaitAsync(TimeSpan.FromSeconds(10));
    }

    [Test]
    public async Task DrainCancelsTheWaitWithoutCancellingTheWork()
    {
        CustomTaskSchedulingThreadPool threadPool = new CustomTaskSchedulingThreadPool(TaskScheduler.Default, 2);
        await threadPool.Initialize();

        TaskCompletionSource started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource<bool> ranToCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously);

        using CancellationTokenSource deadline = new();

        // The work item is handed the very token the drain will be given, which is what passing a
        // shutdown deadline through looks like from here.
        await threadPool.TryRun(async () =>
        {
            started.TrySetResult();
            await release.Task.ConfigureAwait(false);
            ranToCompletion.TrySetResult(deadline.IsCancellationRequested);
        }, deadline.Token);

        await started.Task.WaitAsync(TimeSpan.FromSeconds(10));

        await deadline.CancelAsync();

        bool drained = await threadPool.Drain(deadline.Token);

        drained.Should().BeFalse("the token had already fired while the work item was running, so the drain had to give up rather than wait");
        ranToCompletion.Task.IsCompleted.Should().BeFalse(
            "the work item waits on the test rather than on the token, so giving up on the drain must not have ended it");

        release.TrySetResult();
        bool tokenHadFiredWhileTheWorkRan = await ranToCompletion.Task.WaitAsync(TimeSpan.FromSeconds(10));

        tokenHadFiredWhileTheWorkRan.Should().BeTrue(
            "cancelling the drain cancels the waiting and nothing else - the work ran on with the token already fired, "
            + "which is the guarantee that keeps a shutdown deadline from killing jobs mid-write");
    }

    [Test]
    public async Task DrainDoesNotBlockTheCallingThread()
    {
        CustomTaskSchedulingThreadPool threadPool = new CustomTaskSchedulingThreadPool(TaskScheduler.Default, 2);
        await threadPool.Initialize();

        using ManualResetEventSlim release = new ManualResetEventSlim(false);
        TaskCompletionSource started = new(TaskCreationOptions.RunContinuationsAsynchronously);

        await threadPool.TryRun(() =>
        {
            started.TrySetResult();
            release.Wait(TimeSpan.FromSeconds(30));
            return ValueTask.CompletedTask;
        });

        await started.Task.WaitAsync(TimeSpan.FromSeconds(10));

        // No Task.Run around this: if the drain blocked, this thread would never get here, and the test
        // would hang rather than fail. The assertion is that control came back at all.
        ValueTask<bool> drain = threadPool.Drain(CancellationToken.None);

        drain.IsCompleted.Should().BeFalse(
            "the drain has to hand control back to its caller and finish asynchronously rather than block the thread on the running work");

        release.Set();
        (await drain).Should().BeTrue();
    }

    [Test]
    public async Task DrainOfAnUninitializedPoolReportsThatItDrained()
    {
        CustomTaskSchedulingThreadPool threadPool = new CustomTaskSchedulingThreadPool(TaskScheduler.Default, 1);

        bool drained = await threadPool.Drain();

        drained.Should().BeTrue("a pool that was never initialized has nothing running, so there is nothing left to wait for");
    }

    [Test]
    public async Task DrainRefusesWorkTheWayShutdownDoes()
    {
        CustomTaskSchedulingThreadPool threadPool = new CustomTaskSchedulingThreadPool(TaskScheduler.Default, 1);
        await threadPool.Initialize();

        await threadPool.Drain();

        bool ran = false;
        bool scheduled = await threadPool.TryRun(() =>
        {
            ran = true;
            return ValueTask.CompletedTask;
        });

        scheduled.Should().BeFalse("a drained pool is a shut down pool, so work handed over afterwards has to be refused");
        ran.Should().BeFalse("a refused work item must not run, since nothing will wait for it any more");
    }

    [Test]
    public async Task DrainingAndShuttingDownInEitherOrderIsSafe()
    {
        CustomTaskSchedulingThreadPool threadPool = new CustomTaskSchedulingThreadPool(TaskScheduler.Default, 2);
        await threadPool.Initialize();

        Func<Task> act = async () =>
        {
            (await threadPool.Drain()).Should().BeTrue();
            (await threadPool.Drain()).Should().BeTrue();
            await threadPool.Shutdown(waitForJobsToComplete: true);
            await threadPool.Shutdown(waitForJobsToComplete: false);
        };

        await act.Should().NotThrowAsync(
            "the guard count is dropped once, so repeating either form of shutdown must not end a wait early or fault");
    }

    [Test]
    public async Task DrainOfAPoolThatDoesNotImplementItFallsBackToAnUnboundedShutdown()
    {
        using CancellationTokenSource alreadyCancelled = new();
        await alreadyCancelled.CancelAsync();

        PoolWithoutADrainOfItsOwn threadPool = new PoolWithoutADrainOfItsOwn();

        bool drained = await ((IThreadPool) threadPool).Drain(alreadyCancelled.Token);

        drained.Should().BeTrue(
            "a pool written before Drain existed cannot give up on its wait, so the only truthful answer is that it drained");
        threadPool.ShutdownWaitedForJobs.Should().BeTrue(
            "the fallback has to be the waiting form of shutdown, which is the behaviour it preserves");
        threadPool.ShutdownTokenCouldBeCancelled.Should().BeFalse(
            "the caller's token must not reach a Shutdown that would throw out of it rather than report");
    }

    /// <summary>
    /// The pool releases its shutdown token source once it is down, and a released source answers
    /// <c>Cancel</c> with an <see cref="ObjectDisposedException" /> rather than doing nothing. Both
    /// teardown paths end there and either can follow the other — the scheduler calls
    /// <see cref="IThreadPool.Drain" /> or <see cref="IThreadPool.Shutdown" /> depending on
    /// <c>waitForJobsToComplete</c>, and a caller is free to call the other one afterwards.
    /// </summary>
    [Test]
    public async Task TearingDownTwiceIsATeardownAndThenNothing()
    {
        CustomTaskSchedulingThreadPool threadPool = new(TaskScheduler.Default, 1);
        await threadPool.Initialize();

        (await threadPool.Drain()).Should().BeTrue("nothing is running, so the pool is drained already");

        Func<Task> act = async () =>
        {
            await threadPool.Shutdown(waitForJobsToComplete: false);
            await threadPool.Drain();
        };

        await act.Should().NotThrowAsync(
            "a pool that has already given back what it owns has nothing left to cancel, and saying so "
            + "by throwing would fail a shutdown that has otherwise finished");
    }

    private sealed class CustomTaskSchedulingThreadPool : TaskSchedulingThreadPool
    {
        private readonly TaskScheduler taskScheduler;

        public CustomTaskSchedulingThreadPool(TaskScheduler taskScheduler, int maximumConcurrency)
            : base(maximumConcurrency)
        {
            this.taskScheduler = taskScheduler;
        }

        protected override TaskScheduler GetDefaultScheduler()
        {
            return taskScheduler;
        }
    }

    /// <summary>
    /// A third-party pool as one was written before <see cref="IThreadPool.Drain" /> existed: it implements
    /// the four original members and inherits the default drain.
    /// </summary>
    private sealed class PoolWithoutADrainOfItsOwn : IThreadPool
    {
        public bool? ShutdownWaitedForJobs { get; private set; }

        public bool ShutdownTokenCouldBeCancelled { get; private set; }

        public int PoolSize => 1;

        public ValueTask Initialize(CancellationToken cancellationToken = default) => default;

        public ValueTask<int> WaitForAvailableThreads(CancellationToken cancellationToken = default) => new ValueTask<int>(1);

        public ValueTask<bool> TryRun(Func<ValueTask> action, CancellationToken cancellationToken = default) => new ValueTask<bool>(false);

        public ValueTask Shutdown(bool waitForJobsToComplete = true, CancellationToken cancellationToken = default)
        {
            ShutdownWaitedForJobs = waitForJobsToComplete;
            ShutdownTokenCouldBeCancelled = cancellationToken.CanBeCanceled;
            return default;
        }
    }
}