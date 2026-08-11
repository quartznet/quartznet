using System.Reflection;

using Quartz.Impl;
using Quartz.Util;

namespace Quartz.Tests.Unit.Simpl;

public sealed class DedicatedThreadPoolTest
{
    [Test]
    public async Task Shutdown_ShouldStopQueuedTaskSchedulerThreads()
    {
        DedicatedThreadPool pool = new DedicatedThreadPool { MaxConcurrency = 2 };
        await pool.Initialize();

        QueuedTaskScheduler qts = (QueuedTaskScheduler) pool.Scheduler;
        Thread[] threads = GetThreads(qts);

        Assert.That(threads.All(t => t.IsAlive), Is.True,
            "All QueuedTaskScheduler threads should be alive before shutdown");

        await pool.Shutdown(waitForJobsToComplete: true);

        foreach (Thread thread in threads)
        {
            thread.Join(TimeSpan.FromSeconds(5));
        }

        Assert.That(threads.All(t => !t.IsAlive), Is.True,
            "All QueuedTaskScheduler threads should have stopped after shutdown");
    }

    [Test]
    public async Task Shutdown_WithoutWaiting_ShouldStillStopThreads()
    {
        DedicatedThreadPool pool = new DedicatedThreadPool { MaxConcurrency = 1 };
        await pool.Initialize();

        QueuedTaskScheduler qts = (QueuedTaskScheduler) pool.Scheduler;
        Thread[] threads = GetThreads(qts);

        await pool.Shutdown(waitForJobsToComplete: false);

        foreach (Thread thread in threads)
        {
            thread.Join(TimeSpan.FromSeconds(5));
        }

        Assert.That(threads.All(t => !t.IsAlive), Is.True,
            "QueuedTaskScheduler threads should stop even when not waiting for jobs");
    }

    [Test]
    public async Task Shutdown_DefaultThreadPool_ShouldNotThrow()
    {
        DefaultThreadPool pool = new DefaultThreadPool { MaxConcurrency = 2 };
        await pool.Initialize();

        Func<Task> act = async () => await pool.Shutdown(waitForJobsToComplete: true);

        await act.Should().NotThrowAsync("shutting down the default thread pool has to be safe with no work in flight");
    }

    [TestCase(true)]
    [TestCase(false)]
    public async Task Shutdown_UninitializedDedicatedThreadPool_ShouldNotThrow(bool waitForJobsToComplete)
    {
        // Nothing has created the QueuedTaskScheduler yet, so shutdown has neither threads to stop
        // nor a countdown to signal.
        DedicatedThreadPool pool = new DedicatedThreadPool { MaxConcurrency = 2 };

        Func<Task> act = async () => await pool.Shutdown(waitForJobsToComplete);

        await act.Should().NotThrowAsync("a pool that was never initialized has nothing to tear down");
    }

    [TestCase(true)]
    [TestCase(false)]
    public async Task Shutdown_UninitializedDefaultThreadPool_ShouldNotThrow(bool waitForJobsToComplete)
    {
        DefaultThreadPool pool = new DefaultThreadPool { MaxConcurrency = 2 };

        Func<Task> act = async () => await pool.Shutdown(waitForJobsToComplete);

        await act.Should().NotThrowAsync("a pool that was never initialized has nothing to tear down");
    }

    [Test]
    public async Task Shutdown_CalledTwice_ShouldNotThrowAndShouldLeaveThreadsStopped()
    {
        DedicatedThreadPool pool = new DedicatedThreadPool { MaxConcurrency = 2 };
        await pool.Initialize();

        QueuedTaskScheduler qts = (QueuedTaskScheduler) pool.Scheduler;
        Thread[] threads = GetThreads(qts);

        await pool.Shutdown(waitForJobsToComplete: true);

        Func<Task> act = async () => await pool.Shutdown(waitForJobsToComplete: true);

        await act.Should().NotThrowAsync(
            "a second shutdown must not signal the countdown again, which would throw once it has already reached zero");

        foreach (Thread thread in threads)
        {
            thread.Join(TimeSpan.FromSeconds(5));
        }

        threads.Should().OnlyContain(t => !t.IsAlive, "the dedicated threads stay stopped after a repeated shutdown");
    }

    [Test]
    public async Task TryRun_AfterShutdown_ShouldRefuseTheWork()
    {
        DefaultThreadPool pool = new DefaultThreadPool { MaxConcurrency = 2 };
        await pool.Initialize();
        await pool.Shutdown(waitForJobsToComplete: true);

        bool ran = false;
        bool scheduled = await pool.TryRun(() =>
        {
            ran = true;
            return ValueTask.CompletedTask;
        });

        scheduled.Should().BeFalse("work offered after shutdown has to be refused so the scheduler can put the trigger back");
        ran.Should().BeFalse("a refused work item must not run");
    }

    private static Thread[] GetThreads(QueuedTaskScheduler qts)
    {
        FieldInfo threadsField = typeof(QueuedTaskScheduler)
            .GetField("_threads", BindingFlags.NonPublic | BindingFlags.Instance)!;
        return (Thread[]) threadsField.GetValue(qts)!;
    }
}
