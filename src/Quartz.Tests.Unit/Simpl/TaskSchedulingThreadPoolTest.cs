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
}