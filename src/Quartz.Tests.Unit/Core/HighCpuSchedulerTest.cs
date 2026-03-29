using System.Collections.Specialized;

using Quartz.Impl;

namespace Quartz.Tests.Unit.Core;

/// <summary>
/// Tests that triggers scheduled with StartNow() fire reliably,
/// validating the fix for GitHub issue #781 where the scheduler loop
/// was started via Task.Run (thread pool) instead of a dedicated thread.
/// </summary>
[NonParallelizable]
public class HighCpuSchedulerTest
{
    /// <summary>
    /// Verifies a StartNow() trigger fires promptly. With the fix
    /// (LongRunning dedicated thread), the scheduler loop starts
    /// immediately rather than waiting for a thread pool slot.
    /// </summary>
    [Test]
    public async Task StartNow_FiresPromptly()
    {
        var fired = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        SignalJob.Signal = fired;

        var properties = new NameValueCollection
        {
            ["quartz.serializer.type"] = TestConstants.DefaultSerializerType,
            ["quartz.scheduler.instanceName"] = "HighCpuTest",
        };

        ISchedulerFactory sf = new StdSchedulerFactory(properties);
        IScheduler scheduler = await sf.GetScheduler();

        try
        {
            var job = JobBuilder.Create<SignalJob>()
                .WithIdentity("highCpuJob", "test")
                .Build();

            var trigger = TriggerBuilder.Create()
                .WithIdentity("highCpuTrigger", "test")
                .StartNow()
                .Build();

            await scheduler.ScheduleJob(job, trigger);
            await scheduler.Start();

            bool didFire = await Task.WhenAny(fired.Task, Task.Delay(TimeSpan.FromSeconds(10))) == fired.Task;
            Assert.That(didFire, Is.True, "StartNow() trigger did not fire within 10s");
        }
        finally
        {
            await scheduler.Shutdown(false);
        }
    }

    /// <summary>
    /// Runs the StartNow + verify pattern many times in rapid succession
    /// to catch intermittent failures. The original issue (#781) was
    /// reported as needing "5-10 runs to hit" under high CPU load.
    /// Rapid scheduler start/stop cycles stress the thread startup path
    /// that the LongRunning fix addresses.
    /// </summary>
    [Test]
    public async Task StartNow_FiresReliablyUnderRepeatedUse()
    {
        for (int iteration = 0; iteration < 20; iteration++)
        {
            var fired = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            SignalJob.Signal = fired;

            var properties = new NameValueCollection
            {
                ["quartz.serializer.type"] = TestConstants.DefaultSerializerType,
                ["quartz.scheduler.instanceName"] = $"HighCpuStress_{iteration}",
            };

            ISchedulerFactory sf = new StdSchedulerFactory(properties);
            IScheduler scheduler = await sf.GetScheduler();

            try
            {
                var job = JobBuilder.Create<SignalJob>()
                    .WithIdentity($"stressJob_{iteration}", "test")
                    .Build();

                var trigger = TriggerBuilder.Create()
                    .WithIdentity($"stressTrigger_{iteration}", "test")
                    .StartNow()
                    .Build();

                await scheduler.ScheduleJob(job, trigger);
                await scheduler.Start();

                bool didFire = await Task.WhenAny(fired.Task, Task.Delay(TimeSpan.FromSeconds(10))) == fired.Task;
                Assert.That(didFire, Is.True,
                    $"StartNow() trigger did not fire on iteration {iteration}");
            }
            finally
            {
                await scheduler.Shutdown(false);
            }
        }
    }

    public class SignalJob : IJob
    {
        internal static TaskCompletionSource<bool> Signal = null!;

        public ValueTask Execute(IJobExecutionContext context)
        {
            Signal.TrySetResult(true);
            return default;
        }
    }
}
