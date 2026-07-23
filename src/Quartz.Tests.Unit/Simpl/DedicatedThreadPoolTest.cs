using System;
using System.Linq;
using System.Reflection;
using System.Threading;

using Quartz.Simpl;
using Quartz.Util;

namespace Quartz.Tests.Unit.Simpl;

public sealed class DedicatedThreadPoolTest
{
    [Test]
    public void Shutdown_ShouldStopQueuedTaskSchedulerThreads()
    {
        DedicatedThreadPool pool = new DedicatedThreadPool { ThreadCount = 2 };
        pool.Initialize();

        QueuedTaskScheduler qts = (QueuedTaskScheduler) pool.Scheduler;
        Thread[] threads = GetThreads(qts);

        NUnit.Framework.Assert.That(threads.All(t => t.IsAlive), Is.True,
            "All QueuedTaskScheduler threads should be alive before shutdown");

        pool.Shutdown(waitForJobsToComplete: true);

        foreach (Thread thread in threads)
        {
            thread.Join(TimeSpan.FromSeconds(5));
        }

        NUnit.Framework.Assert.That(threads.All(t => !t.IsAlive), Is.True,
            "All QueuedTaskScheduler threads should have stopped after shutdown");
    }

    [Test]
    public void Shutdown_WithoutWaiting_ShouldStillStopThreads()
    {
        DedicatedThreadPool pool = new DedicatedThreadPool { ThreadCount = 1 };
        pool.Initialize();

        QueuedTaskScheduler qts = (QueuedTaskScheduler) pool.Scheduler;
        Thread[] threads = GetThreads(qts);

        pool.Shutdown(waitForJobsToComplete: false);

        foreach (Thread thread in threads)
        {
            thread.Join(TimeSpan.FromSeconds(5));
        }

        NUnit.Framework.Assert.That(threads.All(t => !t.IsAlive), Is.True,
            "QueuedTaskScheduler threads should stop even when not waiting for jobs");
    }

    [Test]
    public void Shutdown_DefaultThreadPool_ShouldNotThrow()
    {
        DefaultThreadPool pool = new DefaultThreadPool { ThreadCount = 2 };
        pool.Initialize();

        NUnit.Framework.Assert.DoesNotThrow(() => pool.Shutdown(waitForJobsToComplete: true));
    }

    private static Thread[] GetThreads(QueuedTaskScheduler qts)
    {
        FieldInfo threadsField = typeof(QueuedTaskScheduler)
            .GetField("_threads", BindingFlags.NonPublic | BindingFlags.Instance)!;
        return (Thread[]) threadsField.GetValue(qts)!;
    }
}
