using Quartz.Listener;

namespace Quartz.Tests.Integration;

/// <summary>
/// Test for issue where RefireImmediately causes JobChainingJobListener to fail
/// </summary>
[TestFixture]
public class JobChainingWithRefireTest
{
    private IScheduler scheduler = null!;

    [SetUp]
    public async Task SetUp()
    {
        // Reset static counters
        RefireJob.Reset();
        SimpleCountingJob.Reset();
        
        var config = SchedulerBuilder.Create("AUTO", "TestScheduler");
        config.UseDefaultThreadPool(x => x.MaxConcurrency = 5);
        scheduler = await config.BuildScheduler();
    }

    [TearDown]
    public async Task TearDown()
    {
        if (scheduler != null)
        {
            await scheduler.Shutdown(waitForJobsToComplete: false);
        }
    }

    [Test]
    public async Task TestJobChainingWithRefireImmediately()
    {
        // Create chaining listener
        var chainingListener = new JobChainingJobListener("TestChain");
        scheduler.ListenerManager.AddJobListener(chainingListener);

        // Create first job that will refire
        var firstJob = JobBuilder.Create<RefireJob>()
            .WithIdentity("FirstJob", "TestGroup")
            .Build();

        // Create second job in the chain
        var secondJob = JobBuilder.Create<SimpleCountingJob>()
            .WithIdentity("SecondJob", "TestGroup")
            .StoreDurably()
            .Build();

        // Add chain link
        chainingListener.AddJobChainLink(firstJob.Key, secondJob.Key);

        // Schedule first job
        var trigger = TriggerBuilder.Create()
            .WithIdentity("FirstTrigger", "TestGroup")
            .ForJob(firstJob)
            .StartNow()
            .Build();

        await scheduler.ScheduleJob(firstJob, trigger);

        // Add second job (without trigger, will be triggered by chain)
        await scheduler.AddJob(secondJob, replace: true);

        // Start scheduler
        await scheduler.Start();

        // Wait for jobs to complete with polling
        int maxWaitMs = 5000;
        int pollIntervalMs = 100;
        int elapsed = 0;
        while (elapsed < maxWaitMs)
        {
            if (RefireJob.ExecutionCount >= 3 && SimpleCountingJob.ExecutionCount >= 1)
            {
                break;
            }
            await Task.Delay(pollIntervalMs);
            elapsed += pollIntervalMs;
        }

        // Verify both jobs executed
        Assert.That(RefireJob.ExecutionCount, Is.EqualTo(3), "First job should have executed exactly 3 times (2 failures + 1 success)");
        Assert.That(SimpleCountingJob.ExecutionCount, Is.EqualTo(1), "Second job should have executed once after first job completed");
    }

    [DisallowConcurrentExecution]
    public class RefireJob : IJob
    {
        public static volatile int ExecutionCount;
        public static volatile int FailureCount;

        static RefireJob()
        {
            Reset();
        }

        public static void Reset()
        {
            ExecutionCount = 0;
            FailureCount = 0;
        }

        public ValueTask Execute(IJobExecutionContext context)
        {
            var currentCount = Interlocked.Increment(ref ExecutionCount);

            // Fail first 2 times, then succeed
            if (currentCount <= 2)
            {
                Interlocked.Increment(ref FailureCount);
                var exception = new JobExecutionException("Intentional failure for testing")
                {
                    RefireImmediately = true
                };
                throw exception;
            }

            return default;
        }
    }

    public class SimpleCountingJob : IJob
    {
        public static volatile int ExecutionCount;

        static SimpleCountingJob()
        {
            Reset();
        }

        public static void Reset()
        {
            ExecutionCount = 0;
        }

        public ValueTask Execute(IJobExecutionContext context)
        {
            Interlocked.Increment(ref ExecutionCount);
            return default;
        }
    }
}
