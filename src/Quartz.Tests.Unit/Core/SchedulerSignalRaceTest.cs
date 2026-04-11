using System.Collections.Specialized;

using Quartz.Impl;

namespace Quartz.Tests.Unit.Core;

/// <summary>
/// Regression tests for GitHub issue #3028: triggers stuck in WAITING state
/// due to signal loss in QuartzSchedulerThread's SemaphoreSlim-based wake-up
/// mechanism. The original bug was that SemaphoreSlim(0, 1) silently dropped
/// signals via SemaphoreFullException when the semaphore already held a permit.
/// </summary>
[NonParallelizable]
public class SchedulerSignalRaceTest
{
    /// <summary>
    /// Hammers SignalSchedulingChange by scheduling many triggers in rapid
    /// succession while the scheduler is running. With the old maxCount=1
    /// semaphore, some signals were silently dropped, causing triggers to
    /// sit in WAITING state until the next idle-wait timeout (~30 s).
    /// </summary>
    [Test]
    public async Task RapidScheduling_TriggersFirePromptly()
    {
        int triggerCount = 20;
        ManualResetEventSlim allFired = new ManualResetEventSlim(false);
        RapidFireJob.Reset(triggerCount, allFired);

        NameValueCollection properties = new NameValueCollection
        {
            ["quartz.serializer.type"] = TestConstants.DefaultSerializerType,
            ["quartz.scheduler.instanceName"] = "SignalRaceTest_Rapid",
            ["quartz.threadPool.maxConcurrency"] = "5",
        };

        ISchedulerFactory sf = new StdSchedulerFactory(properties);
        IScheduler scheduler = await sf.GetScheduler();

        try
        {
            await scheduler.Start();

            // Schedule triggers one at a time in rapid succession.
            // Each ScheduleJob call signals the scheduler thread.
            for (int i = 0; i < triggerCount; i++)
            {
                IJobDetail job = JobBuilder.Create<RapidFireJob>()
                    .WithIdentity($"rapidJob_{i}", "signalRace")
                    .Build();

                ITrigger trigger = TriggerBuilder.Create()
                    .WithIdentity($"rapidTrigger_{i}", "signalRace")
                    .StartNow()
                    .Build();

                await scheduler.ScheduleJob(job, trigger);
            }

            // All 20 triggers should fire well within 10 seconds.
            // With the old bug, some would wait up to 30 s for the idle timeout.
            bool completed = allFired.Wait(TimeSpan.FromSeconds(10));
            Assert.That(completed, Is.True,
                $"Only {Volatile.Read(ref RapidFireJob.FiredCount)}/{triggerCount} triggers fired within 10 s — possible signal loss");
        }
        finally
        {
            await scheduler.Shutdown(false);
        }
    }

    /// <summary>
    /// Interleaves pause/resume (standby/start) cycles with trigger
    /// scheduling to verify that stale pause-signal permits don't cause
    /// the scheduler to miss wake-ups after resuming.
    /// </summary>
    [Test]
    public async Task PauseResume_DoesNotStallTriggers()
    {
        var fired = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        PauseResumeJob.Signal = fired;

        NameValueCollection properties = new NameValueCollection
        {
            ["quartz.serializer.type"] = TestConstants.DefaultSerializerType,
            ["quartz.scheduler.instanceName"] = "SignalRaceTest_PauseResume",
        };

        ISchedulerFactory sf = new StdSchedulerFactory(properties);
        IScheduler scheduler = await sf.GetScheduler();

        try
        {
            await scheduler.Start();

            // Cycle standby/start several times to accumulate stale
            // pauseSignal permits, then verify a trigger still fires.
            for (int i = 0; i < 5; i++)
            {
                await scheduler.Standby();
                await scheduler.Start();
            }

            IJobDetail job = JobBuilder.Create<PauseResumeJob>()
                .WithIdentity("pauseResumeJob", "signalRace")
                .Build();

            ITrigger trigger = TriggerBuilder.Create()
                .WithIdentity("pauseResumeTrigger", "signalRace")
                .StartNow()
                .Build();

            await scheduler.ScheduleJob(job, trigger);

            bool didFire = await Task.WhenAny(fired.Task, Task.Delay(TimeSpan.FromSeconds(10))) == fired.Task;
            Assert.That(didFire, Is.True,
                "Trigger did not fire within 10 s after pause/resume cycles — possible stale pause permits");
        }
        finally
        {
            await scheduler.Shutdown(false);
        }
    }

    /// <summary>
    /// Schedules a trigger, then immediately pauses and resumes the
    /// scheduler. The trigger must still fire promptly after resume.
    /// </summary>
    [Test]
    public async Task ScheduleThenPauseResume_TriggerStillFires()
    {
        var fired = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        PauseResumeJob.Signal = fired;

        NameValueCollection properties = new NameValueCollection
        {
            ["quartz.serializer.type"] = TestConstants.DefaultSerializerType,
            ["quartz.scheduler.instanceName"] = "SignalRaceTest_SchedulePause",
        };

        ISchedulerFactory sf = new StdSchedulerFactory(properties);
        IScheduler scheduler = await sf.GetScheduler();

        try
        {
            await scheduler.Start();

            IJobDetail job = JobBuilder.Create<PauseResumeJob>()
                .WithIdentity("schedPauseJob", "signalRace")
                .Build();

            ITrigger trigger = TriggerBuilder.Create()
                .WithIdentity("schedPauseTrigger", "signalRace")
                .StartAt(DateTimeOffset.UtcNow.AddSeconds(1))
                .Build();

            await scheduler.ScheduleJob(job, trigger);

            // Pause while the trigger is still waiting to fire
            await scheduler.Standby();
            await scheduler.Start();

            bool didFire = await Task.WhenAny(fired.Task, Task.Delay(TimeSpan.FromSeconds(10))) == fired.Task;
            Assert.That(didFire, Is.True,
                "Trigger did not fire within 10 s after schedule + pause/resume cycle");
        }
        finally
        {
            await scheduler.Shutdown(false);
        }
    }

    public class RapidFireJob : IJob
    {
        private static int expectedCount;
        internal static int FiredCount;
        private static ManualResetEventSlim allFiredSignal = null!;

        internal static void Reset(int expected, ManualResetEventSlim signal)
        {
            expectedCount = expected;
            FiredCount = 0;
            allFiredSignal = signal;
        }

        public ValueTask Execute(IJobExecutionContext context)
        {
            if (Interlocked.Increment(ref FiredCount) >= expectedCount)
            {
                allFiredSignal.Set();
            }

            return default;
        }
    }

    public class PauseResumeJob : IJob
    {
        internal static TaskCompletionSource<bool> Signal = null!;

        public ValueTask Execute(IJobExecutionContext context)
        {
            Signal.TrySetResult(true);
            return default;
        }
    }
}
