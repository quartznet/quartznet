using System;
using System.Collections.Specialized;
using System.Threading;
using System.Threading.Tasks;

using NUnit.Framework;

using Quartz.Impl;
using Quartz.Listener;

namespace Quartz.Tests.Unit.Core;

/// <summary>
/// Regression test for #663: RefireImmediately with JobChainingJobListener
/// should not trigger the chained job until the refiring job finally completes.
/// </summary>
[NonParallelizable]
public sealed class RefireImmediatelyJobChainingTest
{
    [Test]
    public async Task ChainedJob_ShouldNotFire_UntilRefiringJobCompletes()
    {
        // Track execution order
        RefireTrackingJob.Reset();
        ChainedTrackingJob.Reset();

        var properties = new NameValueCollection
        {
            ["quartz.scheduler.instanceName"] = "RefireChainTest",
            ["quartz.serializer.type"] = TestConstants.DefaultSerializerType
        };
        var schedulerFactory = new StdSchedulerFactory(properties);
        var scheduler = await schedulerFactory.GetScheduler();

        try
        {
            // Set up job chaining: refireJob -> chainedJob
            var chainingListener = new JobChainingJobListener("testChain");
            chainingListener.AddJobChainLink(
                new JobKey("refireJob", "test"),
                new JobKey("chainedJob", "test"));
            scheduler.ListenerManager.AddJobListener(chainingListener);

            // refireJob: throws RefireImmediately twice, then succeeds on 3rd attempt
            var refireJob = JobBuilder.Create<RefireTrackingJob>()
                .WithIdentity("refireJob", "test")
                .Build();

            var refireTrigger = TriggerBuilder.Create()
                .WithIdentity("refireTrigger", "test")
                .ForJob(refireJob)
                .StartNow()
                .Build();

            // chainedJob: records when it executes
            var chainedJob = JobBuilder.Create<ChainedTrackingJob>()
                .WithIdentity("chainedJob", "test")
                .StoreDurably()
                .Build();

            await scheduler.AddJob(chainedJob, true);
            await scheduler.ScheduleJob(refireJob, refireTrigger);

            await scheduler.Start();

            // Wait for both jobs to complete
            Assert.IsTrue(ChainedTrackingJob.Completed.Wait(TimeSpan.FromSeconds(10)),
                "Chained job should have completed within 10 seconds");

            // Verify: refireJob executed 3 times (2 refires + 1 final)
            Assert.AreEqual(3, RefireTrackingJob.ExecutionCount,
                "RefireJob should have executed 3 times (2 refires + final success)");

            // Verify: chainedJob executed exactly once
            Assert.AreEqual(1, ChainedTrackingJob.ExecutionCount,
                "ChainedJob should have executed exactly once, not on each refire attempt");

            // Verify: chainedJob started AFTER refireJob completed all executions
            Assert.IsTrue(ChainedTrackingJob.FirstExecutionTicks >= RefireTrackingJob.LastExecutionTicks,
                "ChainedJob should not start until refireJob finishes all refire attempts");
        }
        finally
        {
            await scheduler.Shutdown(true).ConfigureAwait(false);
        }
    }
}

public sealed class RefireTrackingJob : IJob
{
    private static int executionCount;
    private static long lastExecutionTicks;

    public static int ExecutionCount => executionCount;
    public static long LastExecutionTicks => Interlocked.Read(ref lastExecutionTicks);

    public static void Reset()
    {
        executionCount = 0;
        Interlocked.Exchange(ref lastExecutionTicks, 0);
    }

    public Task Execute(IJobExecutionContext context)
    {
        var count = Interlocked.Increment(ref executionCount);
        Interlocked.Exchange(ref lastExecutionTicks, DateTimeOffset.UtcNow.UtcTicks);

        if (count < 3)
        {
            throw new JobExecutionException(new Exception("Retry"), refireImmediately: true);
        }

        return Task.CompletedTask;
    }
}

public sealed class ChainedTrackingJob : IJob
{
    private static int executionCount;
    private static long firstExecutionTicks;
    private static readonly ManualResetEventSlim completed = new ManualResetEventSlim(false);

    public static int ExecutionCount => executionCount;
    public static long FirstExecutionTicks => Interlocked.Read(ref firstExecutionTicks);
    public static ManualResetEventSlim Completed => completed;

    public static void Reset()
    {
        executionCount = 0;
        Interlocked.Exchange(ref firstExecutionTicks, 0);
        completed.Reset();
    }

    public Task Execute(IJobExecutionContext context)
    {
        var count = Interlocked.Increment(ref executionCount);
        if (count == 1)
        {
            Interlocked.Exchange(ref firstExecutionTicks, DateTimeOffset.UtcNow.UtcTicks);
        }
        completed.Set();
        return Task.CompletedTask;
    }
}
