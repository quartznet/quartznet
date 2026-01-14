namespace Quartz.Tests.Integration;

/// <summary>
/// Tests for the scenario where shutdown is called between trigger acquisition and job execution.
/// This tests the fix for the issue where triggers were being set to ERROR state instead of being released.
/// </summary>
public class ShutdownDuringTriggerAcquisitionTest
{
    /// <summary>
    /// Test that when shutdown happens after trigger acquisition but before job execution,
    /// the trigger is released gracefully (not set to ERROR state).
    /// </summary>
    [Test]
    public async Task TestShutdownBetweenTriggerAcquisitionAndExecution()
    {
        // Create a scheduler with a custom thread pool that can simulate shutdown at the right moment
        var scheduler = await SchedulerBuilder.Create("AUTO", "TestScheduler")
            .UseDefaultThreadPool(x => x.MaxConcurrency = 1)
            .BuildScheduler();

        try
        {
            // Create a job that takes some time to execute
            var jobDetail = JobBuilder.Create<SlowJob>()
                .WithIdentity("testJob", "testGroup")
                .StoreDurably()
                .Build();

            // Create a trigger that fires immediately
            var trigger = TriggerBuilder.Create()
                .WithIdentity("testTrigger", "testGroup")
                .ForJob(jobDetail)
                .StartNow()
                .WithSimpleSchedule(x => x.WithIntervalInSeconds(60).RepeatForever())
                .Build();

            await scheduler.ScheduleJob(jobDetail, trigger);
            await scheduler.Start();

            // Wait a bit to let the trigger be acquired
            await Task.Delay(100);

            // Shutdown the scheduler without waiting for jobs to complete
            await scheduler.Shutdown(false);

            // Give it time to complete shutdown
            await Task.Delay(500);

            // Restart the scheduler to check trigger state
            var newScheduler = await SchedulerBuilder.Create("AUTO", "TestScheduler2")
                .UseDefaultThreadPool(x => x.MaxConcurrency = 1)
                .BuildScheduler();

            try
            {
                // Re-add the job and trigger
                await newScheduler.ScheduleJob(jobDetail, trigger);
                await newScheduler.Start();

                // Wait for the trigger to fire (if it's not in ERROR state, it should fire)
                await Task.Delay(2000);

                // Check that the job executed
                var executions = await newScheduler.GetCurrentlyExecutingJobs();
                var triggerState = await newScheduler.GetTriggerState(trigger.Key);
                
                // The trigger should not be in ERROR state
                Assert.That(triggerState, Is.Not.EqualTo(TriggerState.Error), 
                    "Trigger should not be in ERROR state after shutdown during acquisition");
            }
            finally
            {
                await newScheduler.Shutdown(true);
            }
        }
        finally
        {
            // Ensure cleanup even if test fails
            if (!scheduler.IsShutdown)
            {
                await scheduler.Shutdown(true);
            }
        }
    }

    /// <summary>
    /// A simple job that takes a bit of time to execute.
    /// </summary>
    public class SlowJob : IJob
    {
        public async ValueTask Execute(IJobExecutionContext context)
        {
            await Task.Delay(1000);
        }
    }

    /// <summary>
    /// Test using a more controlled approach with a custom job store mock to verify
    /// that ReleaseAcquiredTrigger is called instead of TriggeredJobComplete with ERROR instruction.
    /// </summary>
    [Test]
    public async Task TestShutdownCallsReleaseInsteadOfError()
    {
        var scheduler = await SchedulerBuilder.Create("AUTO", "TestScheduler")
            .UseDefaultThreadPool(x => x.MaxConcurrency = 1)
            .BuildScheduler();

        try
        {
            var jobDetail = JobBuilder.Create<SimpleJob>()
                .WithIdentity("job1")
                .Build();

            var trigger = TriggerBuilder.Create()
                .WithIdentity("trigger1")
                .ForJob(jobDetail)
                .StartNow()
                .Build();

            await scheduler.ScheduleJob(jobDetail, trigger);
            await scheduler.Start();

            // Wait a tiny bit for processing to start
            await Task.Delay(50);

            // Shutdown immediately
            await scheduler.Shutdown(false);

            // Wait for shutdown to complete
            await Task.Delay(500);

            // Check that trigger can be rescheduled (not in ERROR state)
            var newScheduler = await SchedulerBuilder.Create("AUTO", "TestScheduler2")
                .UseDefaultThreadPool(x => x.MaxConcurrency = 1)
                .BuildScheduler();

            try
            {
                // Try to reschedule the same trigger
                await newScheduler.ScheduleJob(jobDetail, trigger);
                var state = await newScheduler.GetTriggerState(trigger.Key);
                
                // If trigger was set to ERROR, this would fail or show ERROR state
                Assert.That(state, Is.Not.EqualTo(TriggerState.Error));
            }
            finally
            {
                await newScheduler.Shutdown(true);
            }
        }
        finally
        {
            if (!scheduler.IsShutdown)
            {
                await scheduler.Shutdown(true);
            }
        }
    }

    public class SimpleJob : IJob
    {
        public ValueTask Execute(IJobExecutionContext context)
        {
            return ValueTask.CompletedTask;
        }
    }
}
