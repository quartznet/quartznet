using System.Collections.Specialized;

using Quartz.Impl;
using Quartz.Impl.Triggers;
using Quartz.Extensibility;

namespace Quartz.Tests.Integration.ExceptionPolicy;

[NonParallelizable]
public class ExceptionHandlingTest
{
    private IScheduler scheduler;

    [SetUp]
    public async Task SetUp()
    {
        var properties = new NameValueCollection
        {
            ["quartz.serializer.type"] = TestConstants.DefaultSerializerType
        };
        ISchedulerFactory sf = new StdSchedulerFactory(properties);

        scheduler = await sf.GetScheduler();
    }

    [Test]
    public async Task ExceptionJobUnscheduleFiringTrigger()
    {
        await scheduler.Start();
        string jobName = "ExceptionPolicyUnscheduleFiringTrigger";
        string jobGroup = "ExceptionPolicyUnscheduleFiringTriggerGroup";

        var myDesc = JobBuilder.Create()
            .OfType<ExceptionJob>()
            .WithIdentity(new JobKey(jobName, jobGroup))
            .StoreDurably(true)
            .Build();

        await scheduler.AddJob(myDesc);
        string trigGroup = "ExceptionPolicyFiringTriggerGroup";
        IOperableTrigger trigger = new CronTriggerImpl("trigName", trigGroup, "0/2 * * * * ?");
        trigger.JobKey = new JobKey(jobName, jobGroup);

        ExceptionJob.ThrowsException = true;
        ExceptionJob.LaunchCount = 0;
        ExceptionJob.Refire = false;
        ExceptionJob.UnscheduleFiringTrigger = true;
        ExceptionJob.UnscheduleAllTriggers = false;

        await scheduler.ScheduleJob(trigger);

        await Task.Delay(7 * 1000);
        await scheduler.DeleteJob(trigger.JobKey);
        Assert.That(ExceptionJob.LaunchCount, Is.EqualTo(1),
            "The job shouldn't have been refired (UnscheduleFiringTrigger)");

        ExceptionJob.LaunchCount = 0;
        ExceptionJob.UnscheduleFiringTrigger = true;
        ExceptionJob.UnscheduleAllTriggers = false;

        await scheduler.AddJob(myDesc);
        trigger = new CronTriggerImpl("trigName", trigGroup, "0/2 * * * * ?");
        trigger.JobKey = new JobKey(jobName, jobGroup);
        await scheduler.ScheduleJob(trigger);
        trigger = new CronTriggerImpl("trigName1", trigGroup, "0/3 * * * * ?");
        trigger.JobKey = new JobKey(jobName, jobGroup);
        await scheduler.ScheduleJob(trigger);
        await Task.Delay(7 * 1000);
        await scheduler.DeleteJob(trigger.JobKey);
        Assert.That(ExceptionJob.LaunchCount, Is.EqualTo(2),
            "The job shouldn't have been refired(UnscheduleFiringTrigger)");
    }

    [Test]
    public async Task ExceptionPolicyRestartImmediately()
    {
        await scheduler.Start();
        JobKey jobKey = new JobKey("ExceptionPolicyRestartJob", "ExceptionPolicyRestartGroup");
        IJobDetail exceptionJob = JobBuilder.Create<ExceptionJob>()
            .WithIdentity(jobKey)
            .StoreDurably()
            .Build();

        await scheduler.AddJob(exceptionJob);

        ExceptionJob.ThrowsException = true;
        ExceptionJob.Refire = true;
        ExceptionJob.UnscheduleAllTriggers = false;
        ExceptionJob.UnscheduleFiringTrigger = false;
        ExceptionJob.LaunchCount = 0;
        await scheduler.TriggerJob(jobKey);

        int i = 10;
        while (i > 0 && ExceptionJob.LaunchCount <= 1)
        {
            i--;
            await Task.Delay(200);
            if (ExceptionJob.LaunchCount > 1)
            {
                break;
            }
        }
        // to ensure job will not be refired in consequent tests
        // in fact, it would be better to have a separate class
        ExceptionJob.ThrowsException = false;

        await Task.Delay(1000);
        await scheduler.DeleteJob(jobKey);
        await Task.Delay(1000);
        Assert.That(ExceptionJob.LaunchCount, Is.GreaterThan(1), "The job should have been refired after exception");
    }

    [Test]
    public async Task ExceptionPolicyNoRestartImmediately()
    {
        await scheduler.Start();
        JobKey jobKey = new JobKey("ExceptionPolicyNoRestartJob", "ExceptionPolicyNoRestartGroup");
        var exceptionJob = JobBuilder.Create()
            .OfType<ExceptionJob>()
            .WithIdentity(jobKey)
            .StoreDurably(true)
            .Build();
        await scheduler.AddJob(exceptionJob);

        ExceptionJob.ThrowsException = true;
        ExceptionJob.Refire = false;
        ExceptionJob.UnscheduleAllTriggers = false;
        ExceptionJob.UnscheduleFiringTrigger = false;
        ExceptionJob.LaunchCount = 0;
        await scheduler.TriggerJob(jobKey);

        int i = 10;
        while (i > 0 && ExceptionJob.LaunchCount <= 1)
        {
            i--;
            await Task.Delay(200);
            if (ExceptionJob.LaunchCount > 1)
            {
                break;
            }
        }
        await scheduler.DeleteJob(jobKey);
        Assert.That(ExceptionJob.LaunchCount, Is.EqualTo(1), "The job should NOT have been refired after exception");
    }
}