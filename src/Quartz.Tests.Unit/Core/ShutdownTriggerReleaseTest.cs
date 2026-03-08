using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Threading;
using System.Threading.Tasks;

using NUnit.Framework;

using Quartz.Impl;
using Quartz.Simpl;
using Quartz.Spi;

namespace Quartz.Tests.Unit.Core;

/// <summary>
/// Tests that acquired triggers are properly released when the scheduler
/// shuts down while triggers are acquired but not yet fired.
/// </summary>
[TestFixture]
public class ShutdownTriggerReleaseTest
{
    /// <summary>
    /// Reproduces the bug where shutdown happens after triggers are acquired
    /// but before TriggersFired is called, causing triggers to remain in ACQUIRED
    /// state (never released, never fired).
    ///
    /// The scenario:
    /// 1. Scheduler acquires a trigger whose fire time is now/past (timeUntilTrigger &lt;= 0)
    /// 2. During the acquire, shutdown sets halted=true
    /// 3. AcquireNextTriggers returns, trigger wait loop is skipped (time already passed)
    /// 4. goAhead = !halted = false → TriggersFired is skipped
    /// 5. BUG: triggers are neither fired nor released
    /// </summary>
    [Test]
    public async Task Shutdown_ReleasesAcquiredTriggers_WhenHaltedBeforeTriggersFired()
    {
        var properties = new NameValueCollection
        {
            ["quartz.jobStore.type"] = typeof(BlockingAcquireJobStore).AssemblyQualifiedName,
            ["quartz.serializer.type"] = TestConstants.DefaultSerializerType,
            ["quartz.scheduler.instanceName"] = "TriggerReleaseTest",
        };

        ISchedulerFactory sf = new StdSchedulerFactory(properties);
        IScheduler scheduler = await sf.GetScheduler();

        var store = BlockingAcquireJobStore.LastInstance;

        var job = JobBuilder.Create<NoOpJob>()
            .WithIdentity("job1", "group1")
            .Build();

        // StartNow so timeUntilTrigger <= 0 when the scheduler thread
        // resumes after AcquireNextTriggers — the trigger wait loop is skipped.
        var trigger = TriggerBuilder.Create()
            .WithIdentity("trigger1", "group1")
            .ForJob(job)
            .StartNow()
            .Build();

        await scheduler.ScheduleJob(job, trigger);
        await scheduler.Start();

        // Wait for the scheduler thread to call AcquireNextTriggers and
        // acquire our trigger (it's now blocked inside the custom store).
        await WithTimeout(store.AcquiresReady.Task, TimeSpan.FromSeconds(5));

        // Start shutdown in a background task. This will:
        //   1. Standby() → paused=true, scheduling change signal
        //   2. Halt(false) → halted=true, cancel token
        //   3. schedThread.Shutdown() → wait for thread (blocked on our store)
        // So this task won't complete until we unblock the store.
        var shutdownTask = Task.Run(() => scheduler.Shutdown(false));

        // Give Standby + Halt enough time to set halted=true
        await Task.Delay(500);

        // Now unblock AcquireNextTriggers. The scheduler thread will resume
        // with halted=true. Since timeUntilTrigger <= 0, the trigger wait
        // loop is skipped. goAhead = !halted = false.
        // Without the fix: triggers are NOT released (bug).
        // With the fix: triggers ARE released.
        store.ProceedWithAcquire.TrySetResult(true);

        // Wait for shutdown to complete
        await WithTimeout(shutdownTask, TimeSpan.FromSeconds(10));

        // Verify that ReleaseAcquiredTrigger was called for our trigger
        Assert.That(store.ReleasedTriggerKeys, Does.Contain(new TriggerKey("trigger1", "group1")),
            "Acquired triggers must be released during shutdown, not left in ACQUIRED state");
    }

    private static async Task WithTimeout(Task task, TimeSpan timeout)
    {
        var completed = await Task.WhenAny(task, Task.Delay(timeout));
        if (completed != task)
        {
            throw new TimeoutException($"Operation did not complete within {timeout}.");
        }

        await task; // propagate exceptions
    }

    public class NoOpJob : IJob
    {
        public Task Execute(IJobExecutionContext context) => Task.CompletedTask;
    }
}

/// <summary>
/// A RAMJobStore that blocks after the first successful AcquireNextTriggers call,
/// allowing the test to trigger a shutdown while triggers are acquired.
/// Also tracks ReleaseAcquiredTrigger calls.
/// </summary>
public class BlockingAcquireJobStore : RAMJobStore
{
    public static BlockingAcquireJobStore LastInstance { get; private set; }

    public readonly TaskCompletionSource<bool> AcquiresReady = new TaskCompletionSource<bool>();
    public readonly TaskCompletionSource<bool> ProceedWithAcquire = new TaskCompletionSource<bool>();
    public ConcurrentBag<TriggerKey> ReleasedTriggerKeys { get; } = new ConcurrentBag<TriggerKey>();

    private int acquireCount;

    public BlockingAcquireJobStore()
    {
        LastInstance = this;
    }

    public override async Task<IReadOnlyCollection<IOperableTrigger>> AcquireNextTriggers(
        DateTimeOffset noLaterThan,
        int maxCount,
        TimeSpan timeWindow,
        CancellationToken cancellationToken = default)
    {
        var result = await base.AcquireNextTriggers(noLaterThan, maxCount, timeWindow, cancellationToken);

        // On the first successful acquire, block until the test signals us.
        // This gives the test time to call Shutdown() and set halted=true
        // while triggers are still acquired.
        if (result.Count > 0 && Interlocked.Increment(ref acquireCount) == 1)
        {
            AcquiresReady.TrySetResult(true);
            await ProceedWithAcquire.Task.ConfigureAwait(false);
        }

        return result;
    }

    public override Task ReleaseAcquiredTrigger(
        IOperableTrigger trigger,
        CancellationToken cancellationToken = default)
    {
        ReleasedTriggerKeys.Add(trigger.Key);
        return base.ReleaseAcquiredTrigger(trigger, cancellationToken);
    }
}
