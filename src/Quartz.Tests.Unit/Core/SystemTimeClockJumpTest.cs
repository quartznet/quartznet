using System;
using System.Collections.Specialized;
using System.Threading.Tasks;

using Quartz.Impl;

namespace Quartz.Tests.Unit.Core;

/// <summary>
/// Tests that the scheduler recovers when the system clock jumps backward,
/// validating the fix for GitHub issue #1508.
/// </summary>
[NonParallelizable]
public sealed class SystemTimeClockJumpTest
{
    /// <summary>
    /// When the system clock jumps backward after triggers are acquired,
    /// the scheduler thread should recover within one idle-wait cycle
    /// and fire triggers once the clock is restored.
    /// </summary>
    [Test]
    public async Task Trigger_Fires_After_Clock_Jumps_Backward_And_Returns()
    {
        var originalUtcNow = SystemTime.UtcNow;
        var originalNow = SystemTime.Now;

        try
        {
            var fired = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            SignalJob.Signal = fired;

            var properties = new NameValueCollection
            {
                ["quartz.serializer.type"] = TestConstants.DefaultSerializerType,
                ["quartz.scheduler.instanceName"] = "ClockJumpTest",
                ["quartz.scheduler.idleWaitTime"] = "2000",
            };

            ISchedulerFactory sf = new StdSchedulerFactory(properties);
            IScheduler scheduler = await sf.GetScheduler();

            try
            {
                IJobDetail job = JobBuilder.Create<SignalJob>()
                    .WithIdentity("clockJumpJob", "test")
                    .Build();

                // Schedule trigger 4 seconds in the future to give the scheduler
                // time to acquire it before we jump the clock
                ITrigger trigger = TriggerBuilder.Create()
                    .WithIdentity("clockJumpTrigger", "test")
                    .StartAt(DateTimeOffset.UtcNow.AddSeconds(4))
                    .Build();

                await scheduler.ScheduleJob(job, trigger);
                await scheduler.Start();

                // Let the scheduler acquire the trigger
                await Task.Delay(TimeSpan.FromSeconds(1)).ConfigureAwait(false);

                // Jump clock backward by 1 hour — this makes timeUntilTrigger ~1 hour
                SystemTime.UtcNow = () => DateTimeOffset.UtcNow.AddHours(-1);
                SystemTime.Now = () => DateTimeOffset.Now.AddHours(-1);

                // Wait long enough for at least one capped idle-wait cycle (2s)
                // but not so long that an uncapped wait would expire
                await Task.Delay(TimeSpan.FromSeconds(4)).ConfigureAwait(false);

                // Restore the clock — the trigger's fire time is now in the past
                SystemTime.UtcNow = originalUtcNow;
                SystemTime.Now = originalNow;

                // The scheduler should recover within one idle-wait cycle (2s) and fire
                bool didFire = await Task.WhenAny(fired.Task, Task.Delay(TimeSpan.FromSeconds(10))) == fired.Task;
                Assert.That(didFire, Is.True,
                    "Trigger did not fire after clock was restored — scheduler thread likely stuck in unbounded wait");
            }
            finally
            {
                await scheduler.Shutdown(false);
            }
        }
        finally
        {
            SystemTime.UtcNow = originalUtcNow;
            SystemTime.Now = originalNow;
        }
    }

    public sealed class SignalJob : IJob
    {
        internal static TaskCompletionSource<bool> Signal = null!;

        public Task Execute(IJobExecutionContext context)
        {
            Signal.TrySetResult(true);
            return Task.CompletedTask;
        }
    }
}
