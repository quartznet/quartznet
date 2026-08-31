namespace Quartz.Tests.Integration;

/// <summary>
/// Drives a real scheduler over <c>RAMJobStore</c> through a whole execution, so the executing state is
/// proven on the paths <c>QuartzSchedulerThread</c> and <c>JobRunShell</c> actually use rather than on a
/// job store poked directly. The store keys executions by fire instance id, and only a real run proves the
/// trigger instance handed to <c>TriggeredJobComplete</c> still carries the id that recorded them.
/// </summary>
[NonParallelizable]
public class ExecutingTriggerStateRamTest
{
    private static SemaphoreSlim jobStarted = new(0);
    private static SemaphoreSlim jobCanFinish = new(0);
    private static volatile bool finishedOnRelease;

    [SetUp]
    public void ResetSignals()
    {
        jobStarted = new SemaphoreSlim(0);
        jobCanFinish = new SemaphoreSlim(0);
        finishedOnRelease = false;
    }

    [TearDown]
    public void DisposeSignals()
    {
        jobStarted.Dispose();
        jobCanFinish.Dispose();
    }

    [Test]
    public async Task GetTriggerState_ReportsExecuting_ForTheWholeRunAndNormalAfterwards()
    {
        IScheduler scheduler = await QuartzSchedulerBuilder
            .Create(q => q.ConfigureScheduler(o =>
            {
                o.InstanceId = "AUTO";
                o.InstanceName = "ExecutingStateRamTest";
            }))
            .BuildScheduler();

        var triggerKey = new TriggerKey("executingStateTrigger", "executingStateGroup");

        try
        {
            await scheduler.Start();

            IJobDetail job = JobBuilder.Create<BlockingJob>()
                .WithIdentity("executingStateJob", "executingStateGroup")
                .Build();

            // Repeating, so the trigger stays schedulable while it runs — without consulting the
            // execution records there would be nothing to distinguish it from an idle trigger.
            ITrigger trigger = TriggerBuilder.Create()
                .WithIdentity(triggerKey)
                .ForJob(job)
                .WithSimpleSchedule(x => x.WithInterval(TimeSpan.FromHours(1)).RepeatForever())
                .StartNow()
                .Build();

            await scheduler.ScheduleJob(job, trigger);

            (await jobStarted.WaitAsync(TimeSpan.FromSeconds(30)))
                .Should().BeTrue("the job should have started");

            (await scheduler.GetTriggerState(triggerKey)).Should().Be(TriggerState.Executing);

            PagedResult<TriggerHeader> executing = await scheduler.QueryTriggers(new TriggerQuery { State = TriggerState.Executing });
            executing.Items.Select(x => x.Key).Should().Contain(triggerKey,
                "a listing filtered by Executing should return the running trigger");

            PagedResult<TriggerHeader> normal = await scheduler.QueryTriggers(new TriggerQuery { State = TriggerState.Normal });
            normal.Items.Select(x => x.Key).Should().NotContain(triggerKey,
                "filtering by Normal must not return a trigger the same listing reports as Executing");

            jobCanFinish.Release();

            // The completion travels back through JobRunShell; if the trigger it hands over no longer
            // carried the fire instance id that recorded the execution, this would never become Normal.
            DateTimeOffset deadline = DateTimeOffset.UtcNow.AddSeconds(30);
            TriggerState finalState = TriggerState.Executing;
            while (DateTimeOffset.UtcNow < deadline)
            {
                finalState = await scheduler.GetTriggerState(triggerKey);
                if (finalState == TriggerState.Normal)
                {
                    break;
                }

                await Task.Delay(TimeSpan.FromMilliseconds(100));
            }

            finalState.Should().Be(TriggerState.Normal,
                "completing the job must release the execution it recorded");
            finishedOnRelease.Should().BeTrue(
                "the job must have finished because the test released it, not because its wait timed out");
        }
        finally
        {
            jobCanFinish.Release();
            await scheduler.Shutdown(waitForJobsToComplete: true);
        }
    }

    /// <summary>
    /// Allows concurrent execution, and blocks until the test releases it.
    /// </summary>
    private sealed class BlockingJob : IJob
    {
        public async ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
        {
            jobStarted.Release();

            // Recorded rather than asserted: an exception thrown here would be caught by JobRunShell and
            // never reach the test runner.
            finishedOnRelease = await jobCanFinish.WaitAsync(TimeSpan.FromSeconds(60), cancellationToken);
        }
    }
}
