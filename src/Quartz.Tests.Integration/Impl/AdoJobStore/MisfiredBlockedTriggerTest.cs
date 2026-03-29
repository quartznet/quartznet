using System;
using System.Collections.Specialized;
using System.Threading;
using System.Threading.Tasks;

using NUnit.Framework;

using Quartz.Impl;
using Quartz.Impl.Matchers;
using Quartz.Util;

namespace Quartz.Tests.Integration.Impl.AdoJobStore;

/// <summary>
/// Tests for misfired blocked triggers cleanup issue.
/// </summary>
[NonParallelizable]
public class MisfiredBlockedTriggerTest
{
    // Test configuration constants
    private const int MisfireThresholdMilliseconds = 1000; // 1 second - triggers that don't fire within this time are considered misfired
    private const int RepeatingTriggerIntervalSeconds = 10; // 10 seconds between repeating trigger fires

    [Test]
    [Category("db-sqlserver")]
    public async Task TestMisfiredBlockedTriggerShouldBeDeleted()
    {
        // Configure scheduler with persistent job store and 1-second misfire threshold
        var properties = new NameValueCollection
        {
            ["quartz.scheduler.instanceName"] = "TestScheduler",
            ["quartz.scheduler.instanceId"] = "AUTO",
            ["quartz.jobStore.type"] = "Quartz.Impl.AdoJobStore.JobStoreTX, Quartz",
            ["quartz.jobStore.driverDelegateType"] = typeof(Quartz.Impl.AdoJobStore.SqlServerDelegate).AssemblyQualifiedNameWithoutVersion(),
            ["quartz.jobStore.dataSource"] = "default",
            ["quartz.jobStore.tablePrefix"] = "QRTZ_",
            ["quartz.dataSource.default.connectionString"] = TestConstants.SqlServerConnectionString,
            ["quartz.dataSource.default.provider"] = TestConstants.DefaultSqlServerProvider,
            ["quartz.jobStore.misfireThreshold"] = MisfireThresholdMilliseconds.ToString(),
            ["quartz.serializer.type"] = TestConstants.DefaultSerializerType,
            ["quartz.threadPool.maxConcurrency"] = "1" // Only one thread to force blocking
        };

        ISchedulerFactory sf = new StdSchedulerFactory(properties);
        IScheduler scheduler = await sf.GetScheduler();

        await scheduler.Clear(); // Clear any existing data

        try
        {
            // Drain any leftover signal from a previous run
            MisfiredBlockedTriggerTestSlowJob.Started.Wait(0);

            // Create a job that takes time to execute and disallows concurrent execution
            var job = JobBuilder.Create<MisfiredBlockedTriggerTestSlowJob>()
                .WithIdentity("slowJob", "testGroup")
                .DisallowConcurrentExecution()
                .Build();

            // Create a repeating trigger (fires every RepeatingTriggerIntervalSeconds)
            var repeatingTrigger = TriggerBuilder.Create()
                .WithIdentity("repeatingTrigger", "testGroup")
                .ForJob(job)
                .StartNow()
                .WithSimpleSchedule(x => x
                    .WithIntervalInSeconds(RepeatingTriggerIntervalSeconds)
                    .RepeatForever()
                    .WithMisfireHandlingInstructionNextWithRemainingCount())
                .Build();

            await scheduler.ScheduleJob(job, repeatingTrigger);
            await scheduler.Start();

            // Wait for the job to actually start executing (not just a delay)
            if (!await MisfiredBlockedTriggerTestSlowJob.Started.WaitAsync(TimeSpan.FromSeconds(10)))
            {
                Assert.Fail("Job did not start within 10 seconds");
            }

            // Now schedule a fire-and-forget trigger while the job is executing
            // This trigger will be blocked, then will misfire
            var fireAndForgetTrigger = TriggerBuilder.Create()
                .WithIdentity("fireAndForgetTrigger", "testGroup")
                .ForJob(job.Key)
                .StartNow()
                .WithSimpleSchedule(x => x
                    .WithRepeatCount(0) // Fire once only
                    .WithMisfireHandlingInstructionNextWithRemainingCount())
                .Build();

            await scheduler.ScheduleJob(fireAndForgetTrigger);

            // Poll for the trigger deletion with timeout
            // The trigger should misfire and then be deleted after the job completes
            var maxWaitTime = TimeSpan.FromSeconds(30);
            var pollInterval = TimeSpan.FromMilliseconds(500);
            var startTime = DateTimeOffset.UtcNow;
            ITrigger trigger = null;

            while (DateTimeOffset.UtcNow - startTime < maxWaitTime)
            {
                trigger = await scheduler.GetTrigger(fireAndForgetTrigger.Key);
                if (trigger == null)
                {
                    // Trigger was deleted, which is what we expect
                    break;
                }
                await Task.Delay(pollInterval);
            }

            // The trigger should be deleted because it misfired with RepeatCount=0
            Assert.IsNull(trigger, "The misfired fire-and-forget trigger should have been deleted");

            // Verify the repeating trigger still exists
            var repeatingTriggerCheck = await scheduler.GetTrigger(repeatingTrigger.Key);
            Assert.IsNotNull(repeatingTriggerCheck, "The repeating trigger should still exist");
        }
        finally
        {
            await scheduler.Shutdown(true);
        }
    }
}

/// <summary>
/// A job that simulates long-running work to test trigger blocking behavior.
/// The job duration is set to ensure triggers will misfire while blocked.
/// </summary>
[DisallowConcurrentExecution]
internal sealed class MisfiredBlockedTriggerTestSlowJob : IJob
{
    private const int JobDurationMilliseconds = 3000; // 3 seconds

    /// <summary>
    /// Signal that the job has started executing.
    /// </summary>
    internal static readonly SemaphoreSlim Started = new(0, 1);

    public async ValueTask Execute(IJobExecutionContext context)
    {
        try
        {
            Started.Release();
        }
        catch (SemaphoreFullException)
        {
            // Already signaled from a previous execution
        }

        // Simulate a long-running job (duration must exceed misfire threshold)
        await Task.Delay(JobDurationMilliseconds);
    }
}
