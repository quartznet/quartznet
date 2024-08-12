using System.Collections.Specialized;

using Quartz.Impl;
using Quartz.Job;
using Quartz.Plugin.Interrupt;
using Quartz.Util;

namespace Quartz.Tests.Unit.Plugin.Interrupt;

public class AutoInterruptableJobTest
{
    private static readonly SemaphoreSlim sync = new(0);

    private class TestInterruptableJob : IJob
    {
        internal static bool interrupted;

        public async ValueTask Execute(IJobExecutionContext context)
        {
            // Console.WriteLine("TestInterruptableJob is executing.");
            sync.Release(); // wait for test thread to notice the job is now running

            for (var i = 0; i < 200; i++)
            {
                await Task.Delay(50); // simulate being busy for a while, then checking interrupted flag...

                if (context.CancellationToken.IsCancellationRequested)
                {
                    interrupted = true;
                    // Console.WriteLine("TestInterruptableJob main loop detected interrupt signal.");
                    break;
                }
            }

            // Console.WriteLine("TestInterruptableJob exiting with interrupted = " + interrupted);
            sync.Release();
        }
    }

    [Test]
    public async Task TestJobAutoInterruption()
    {
        var scheduler = await CreateScheduler<TestInterruptableJob>();

        await sync.WaitAsync(); // make sure the job starts running...

        var executingJobs = await scheduler.GetCurrentlyExecutingJobs();

        Assert.That(executingJobs, Has.Count.EqualTo(1), "Number of executing jobs should be 1");

        await sync.WaitAsync(); // wait for the job to terminate

        Assert.That(TestInterruptableJob.interrupted, "Expected interrupted flag to be set on job class ");

        await scheduler.Clear();

        await scheduler.Shutdown();
    }

    [Test]
    public async Task TestJobAutoInterruptionWhenNoInterrupt()
    {
        var scheduler = await CreateScheduler<NoOpJob>();

        await Task.Delay(TimeSpan.FromSeconds(2));

        await scheduler.Clear();

        await scheduler.Shutdown();
    }

    private static async Task<IScheduler> CreateScheduler<T>() where T : IJob
    {
        // create a simple scheduler

        var config = new NameValueCollection
        {
            ["quartz.scheduler.instanceName"] = "InterruptableJobTest_Scheduler",
            ["quartz.scheduler.instanceId"] = "AUTO",
            ["quartz.threadPool.threadCount"] = "2",
            ["quartz.plugin.jobInterruptor.type"] = typeof(JobInterruptMonitorPlugin).AssemblyQualifiedNameWithoutVersion(),
            ["quartz.plugin.jobInterruptor.defaultMaxRunTime"] = "1000"
        };

        var scheduler = await new StdSchedulerFactory(config).GetScheduler();
        await scheduler.Start();

        // add a job with a trigger that will fire immediately

        var jobDataMap = new JobDataMap();
        jobDataMap.PutAsString(JobInterruptMonitorPlugin.JobDataMapKeyAutoInterruptable, true);
        var job = JobBuilder.Create<T>()
            .WithIdentity("j1")
            .SetJobData(jobDataMap)
            .Build();

        var trigger = TriggerBuilder.Create()
            .WithIdentity("t1")
            .ForJob(job)
            .StartNow()
            .Build();

        await scheduler.ScheduleJob(job, trigger);

        return scheduler;
    }
}