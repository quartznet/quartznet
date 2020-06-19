#if REMOTING
using System;
using System.Collections.Specialized;
using System.Threading.Tasks;

using NUnit.Framework;

using Quartz.Impl;
using Quartz.Impl.Matchers;

namespace Quartz.Tests.Unit
{
    public class TestRemoting
    {
        [Test]
        [Explicit]
        public async Task Test()
        {
            var remoteFactory = new StdSchedulerFactory(new NameValueCollection
            {
                ["quartz.scheduler.exporter.bindName"] = "remoteschedulerbinding",
                ["quartz.scheduler.exporter.channelName"] = "tcpQuartz",
                ["quartz.scheduler.exporter.channelType"] = "tcp",
                ["quartz.scheduler.exporter.port"] = "45000",
                ["quartz.scheduler.exporter.type"] = "Quartz.Simpl.RemotingSchedulerExporter, Quartz",
                ["quartz.scheduler.instanceName"] = "remote scheduler",
                ["quartz.jobStore.type"] = "Quartz.Simpl.RAMJobStore, Quartz",
                ["quartz.jobStore.misfireThreshold"] = "60000",
                ["quartz.threadPool.type"] = "Quartz.Simpl.SimpleThreadPool, Quartz",
                ["quartz.threadPool.threadCount"] = "5",
            });

            var remoteScheduler = await remoteFactory.GetScheduler();

            var job = JobBuilder.Create<SampleJob>()
                .WithIdentity("myJob", "group1") // name "myJob", group "group1"
                .Build();

            // Trigger the job to run now, and then every 40 seconds
            var trigger = TriggerBuilder.Create()
                .WithIdentity("myTrigger", "group1")
                .StartNow()
                .WithSimpleSchedule(x => x
                    .WithIntervalInSeconds(1)
                    .RepeatForever())
                .Build();

            await remoteScheduler.ScheduleJob(job, trigger);
            await remoteScheduler.Start();

            var remotingFactory = new StdSchedulerFactory(new NameValueCollection
            {
                ["quartz.scheduler.instanceName"] = "remoted scheduler",
                ["quartz.scheduler.proxy"] = "true",
                ["quartz.scheduler.proxy.address"] = "tcp://localhost:45000/remoteschedulerbinding",
                ["quartz.threadPool.threadCount"] = "0",
            });

            var remotingScheduler = await remotingFactory.GetScheduler();
            var jobGroups = await remotingScheduler.GetJobGroupNames();
            foreach (var jobGroup in jobGroups)
            {
                var groupMatcher = GroupMatcher<JobKey>.GroupContains(jobGroup);
                var jobKeys = await remotingScheduler.GetJobKeys(groupMatcher);
            }

            var executingJobs = remotingScheduler.GetCurrentlyExecutingJobs();

            await remoteScheduler.Shutdown();
        }
    }

    /// <summary>
    /// A sample job that just prints info on console for demostration purposes.
    /// </summary>
    public class SampleJob : IJob
    {
        /// <summary>
        /// Called by the <see cref="IScheduler" /> when a <see cref="ITrigger" />
        /// fires that is associated with the <see cref="IJob" />.
        /// </summary>
        /// <remarks>
        /// The implementation may wish to set a  result object on the 
        /// JobExecutionContext before this method exits.  The result itself
        /// is meaningless to Quartz, but may be informative to 
        /// <see cref="IJobListener" />s or 
        /// <see cref="ITriggerListener" />s that are watching the job's 
        /// execution.
        /// </remarks>
        /// <param name="context">The execution context.</param>
        public async Task Execute(IJobExecutionContext context)
        {
            Console.WriteLine("SampleJob running...");
            await Task.Delay(TimeSpan.FromSeconds(3600));
            Console.WriteLine("SampleJob run finished.");
        }
    }
}
#endif
