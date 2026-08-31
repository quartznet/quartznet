using System.Collections.Specialized;
using Microsoft.Extensions.Logging;
using Quartz.Diagnostics;

namespace Quartz.Tests.Unit;

/// <summary>
/// Tests for <see cref="ISchedulerListener"/>.
/// </summary>
/// <author>Zemian Deng</author>
/// <author>Marko Lahma (.NET)</author>
[NonParallelizable]
public class SchedulerListenerTest
{
    private static readonly ILogger<SchedulerListenerTest> logger = LogProvider.CreateLogger<SchedulerListenerTest>();
    private static int jobExecutionCount;

    public class Qtz205Job : IJob
    {
        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
        {
            jobExecutionCount++;
            logger.LogInformation("Job executed. jobExecutionCount={ExecutionCount}", jobExecutionCount);
            return default;
        }
    }

    public class Qtz205TriggerListener : ITriggerListener
    {
        public int FireCount { get; private set; }

        public string Name => "Qtz205TriggerListener";

        public ValueTask TriggerFired(ITrigger trigger, IJobExecutionContext context, CancellationToken cancellationToken)
        {
            FireCount++;
            logger.LogInformation("Trigger fired. count {FireCount}", FireCount);
            return default;
        }

        public ValueTask<bool> VetoJobExecution(ITrigger trigger, IJobExecutionContext context, CancellationToken cancellationToken)
        {
            if (FireCount >= 3)
            {
                return new ValueTask<bool>(true);
            }
            return new ValueTask<bool>(false);
        }

        public ValueTask TriggerMisfired(ITrigger trigger, IScheduler scheduler, CancellationToken cancellationToken)
        {
            return default;
        }

        public ValueTask TriggerComplete(ITrigger trigger,
            IJobExecutionContext context,
            SchedulerInstruction triggerInstructionCode,
            CancellationToken cancellationToken)
        {
            return default;
        }
    }

    public class Qtz205ScheListener : ISchedulerListener
    {
        public int TriggerFinalizedCount { get; private set; }

        public ValueTask JobScheduled(IScheduler scheduler, ITrigger trigger, CancellationToken cancellationToken)
        {
            return default;
        }

        public ValueTask JobUnscheduled(IScheduler scheduler, TriggerKey triggerKey, CancellationToken cancellationToken)
        {
            return default;
        }

        public ValueTask TriggerFinalized(IScheduler scheduler, ITrigger trigger, CancellationToken cancellationToken)
        {
            TriggerFinalizedCount++;
            logger.LogInformation("triggerFinalized {Trigger}", trigger);
            return default;
        }

        public ValueTask TriggerPaused(IScheduler scheduler, TriggerKey triggerKey, CancellationToken cancellationToken)
        {
            return default;
        }

        public ValueTask TriggersPaused(IScheduler scheduler, string triggerGroup, CancellationToken cancellationToken)
        {
            return default;
        }

        public ValueTask TriggerResumed(IScheduler scheduler, TriggerKey triggerKey, CancellationToken cancellationToken)
        {
            return default;
        }

        public ValueTask TriggersResumed(IScheduler scheduler, string triggerGroup, CancellationToken cancellationToken)
        {
            return default;
        }

        public ValueTask JobAdded(IScheduler scheduler, IJobDetail jobDetail, CancellationToken cancellationToken)
        {
            return default;
        }

        public ValueTask JobDeleted(IScheduler scheduler, JobKey jobKey, CancellationToken cancellationToken)
        {
            return default;
        }

        public ValueTask JobPaused(IScheduler scheduler, JobKey jobKey, CancellationToken cancellationToken)
        {
            return default;
        }

        public ValueTask JobInterrupted(IScheduler scheduler, JobKey jobKey, CancellationToken cancellationToken = new())
        {
            return default;
        }

        public ValueTask JobsPaused(IScheduler scheduler, string jobGroup, CancellationToken cancellationToken)
        {
            return default;
        }

        public ValueTask JobResumed(IScheduler scheduler, JobKey jobKey, CancellationToken cancellationToken)
        {
            return default;
        }

        public ValueTask JobsResumed(IScheduler scheduler, string jobGroup, CancellationToken cancellationToken)
        {
            return default;
        }

        public ValueTask SchedulerError(IScheduler scheduler, SchedulerErrorContext errorContext, CancellationToken cancellationToken)
        {
            return default;
        }

        public ValueTask SchedulerInStandbyMode(IScheduler scheduler, CancellationToken cancellationToken)
        {
            return default;
        }

        public ValueTask SchedulerStarted(IScheduler scheduler, CancellationToken cancellationToken)
        {
            return default;
        }

        public ValueTask SchedulerStarting(IScheduler scheduler, CancellationToken cancellationToken)
        {
            return default;
        }

        public ValueTask SchedulerShutdown(IScheduler scheduler, CancellationToken cancellationToken)
        {
            return default;
        }

        public ValueTask SchedulerShuttingDown(IScheduler scheduler, CancellationToken cancellationToken)
        {
            return default;
        }

        public ValueTask SchedulingDataCleared(IScheduler scheduler, CancellationToken cancellationToken)
        {
            return default;
        }
    }

    [Test]
    public async Task TestTriggerFinalized()
    {
        Qtz205TriggerListener triggerListener = new Qtz205TriggerListener();
        Qtz205ScheListener schedulerListener = new Qtz205ScheListener();
        NameValueCollection props = new NameValueCollection();
        props["quartz.scheduler.idleWaitTime"] = "1500";
        props["quartz.threadPool.threadCount"] = "2";
        props["quartz.serializer.type"] = TestConstants.DefaultSerializerType;
        IScheduler scheduler = await QuartzSchedulerBuilder.Create().UseProperties(props).BuildScheduler();
        scheduler.ListenerManager.AddSchedulerListener(schedulerListener);
        scheduler.ListenerManager.AddTriggerListener(triggerListener);

        IJobDetail job = JobBuilder.Create<Qtz205Job>().WithIdentity("test").Build();
        ITrigger trigger = TriggerBuilder.Create()
            .WithIdentity("test")
            .WithSchedule(SimpleScheduleBuilder.Create().WithInterval(TimeSpan.FromSeconds(1)).WithRepeatCount(2))
            .Build();

        await scheduler.ScheduleJob(job, trigger);
        await scheduler.Start();
        await Task.Delay(5000);

        await scheduler.Shutdown(true);

        Assert.Multiple(() =>
        {
            Assert.That(jobExecutionCount, Is.EqualTo(2));
            Assert.That(triggerListener.FireCount, Is.EqualTo(3));
            Assert.That(schedulerListener.TriggerFinalizedCount, Is.EqualTo(1));
        });
    }
}