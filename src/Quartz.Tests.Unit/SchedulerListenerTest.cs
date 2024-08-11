using System.Collections.Specialized;

using Microsoft.Extensions.Logging;

using Quartz.Impl;
using Quartz.Diagnostics;

namespace Quartz.Tests.Unit;

/// <summary>
/// Tests for <see cref="ISchedulerListener"/>.
/// </summary>
/// <author>Zemian Deng</author>
/// <author>Marko Lahma (.NET)</author>
[TestFixture]
public class SchedulerListenerTest
{
    private static readonly ILogger<SchedulerListenerTest> logger = LogProvider.CreateLogger<SchedulerListenerTest>();
    private static int jobExecutionCount;

    public class Qtz205Job : IJob
    {
        public ValueTask Execute(IJobExecutionContext context)
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

        public ValueTask TriggerMisfired(ITrigger trigger, CancellationToken cancellationToken)
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

        public ValueTask JobScheduled(ITrigger trigger, CancellationToken cancellationToken)
        {
            return default;
        }

        public ValueTask JobUnscheduled(TriggerKey triggerKey, CancellationToken cancellationToken)
        {
            return default;
        }

        public ValueTask TriggerFinalized(ITrigger trigger, CancellationToken cancellationToken)
        {
            TriggerFinalizedCount++;
            logger.LogInformation("triggerFinalized {Trigger}", trigger);
            return default;
        }

        public ValueTask TriggerPaused(TriggerKey triggerKey, CancellationToken cancellationToken)
        {
            return default;
        }

        public ValueTask TriggersPaused(string triggerGroup, CancellationToken cancellationToken)
        {
            return default;
        }

        public ValueTask TriggerResumed(TriggerKey triggerKey, CancellationToken cancellationToken)
        {
            return default;
        }

        public ValueTask TriggersResumed(string triggerGroup, CancellationToken cancellationToken)
        {
            return default;
        }

        public ValueTask JobAdded(IJobDetail jobDetail, CancellationToken cancellationToken)
        {
            return default;
        }

        public ValueTask JobDeleted(JobKey jobKey, CancellationToken cancellationToken)
        {
            return default;
        }

        public ValueTask JobPaused(JobKey jobKey, CancellationToken cancellationToken)
        {
            return default;
        }

        public ValueTask JobInterrupted(JobKey jobKey, CancellationToken cancellationToken = new())
        {
            return default;
        }

        public ValueTask JobsPaused(string jobGroup, CancellationToken cancellationToken)
        {
            return default;
        }

        public ValueTask JobResumed(JobKey jobKey, CancellationToken cancellationToken)
        {
            return default;
        }

        public ValueTask JobsResumed(string jobGroup, CancellationToken cancellationToken)
        {
            return default;
        }

        public ValueTask SchedulerError(string msg, SchedulerException cause, CancellationToken cancellationToken)
        {
            return default;
        }

        public ValueTask SchedulerInStandbyMode(CancellationToken cancellationToken)
        {
            return default;
        }

        public ValueTask SchedulerStarted(CancellationToken cancellationToken)
        {
            return default;
        }

        public ValueTask SchedulerStarting(CancellationToken cancellationToken)
        {
            return default;
        }

        public ValueTask SchedulerShutdown(CancellationToken cancellationToken)
        {
            return default;
        }

        public ValueTask SchedulerShuttingdown(CancellationToken cancellationToken)
        {
            return default;
        }

        public ValueTask SchedulingDataCleared(CancellationToken cancellationToken)
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
        IScheduler scheduler = await new StdSchedulerFactory(props).GetScheduler();
        scheduler.ListenerManager.AddSchedulerListener(schedulerListener);
        scheduler.ListenerManager.AddTriggerListener(triggerListener);

        IJobDetail job = JobBuilder.Create<Qtz205Job>().WithIdentity("test").Build();
        ITrigger trigger = TriggerBuilder.Create()
            .WithIdentity("test")
            .WithSchedule(SimpleScheduleBuilder.RepeatSecondlyForTotalCount(3))
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