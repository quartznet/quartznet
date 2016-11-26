using System.Threading;

using NUnit.Framework;

using Quartz.Impl;
using Quartz.Impl.AdoJobStore;
using Quartz.Impl.AdoJobStore.Common;
using Quartz.Job;
using Quartz.Simpl;
using Quartz.Util;

namespace Quartz.Tests.Integration
{
    public class JobDataMapStorageTest : IntegrationTest
    {
        [Test]
        public void TestJobDataMapDirtyFlag()
        {
            IScheduler scheduler = CreateScheduler("testBasicStorageFunctions", 2);
            scheduler.Clear();

            IJobDetail jobDetail = JobBuilder.Create<NoOpJob>()
                .WithIdentity("test")
                .UsingJobData("jfoo", "bar")
                .Build();

            ITrigger trigger = TriggerBuilder.Create()
                .WithIdentity("test")
                .WithCronSchedule("0 0 0 * * ?")
                .UsingJobData("tfoo", "bar")
                .Build();

            scheduler.ScheduleJob(jobDetail, trigger);

            IJobDetail storedJobDetail = scheduler.GetJobDetail(new JobKey("test"));
            JobDataMap storedJobMap = storedJobDetail.JobDataMap;
            Assert.That(storedJobMap.Dirty, Is.False);

            ITrigger storedTrigger = scheduler.GetTrigger(new TriggerKey("test"));
            JobDataMap storedTriggerMap = storedTrigger.JobDataMap;
            Assert.That(storedTriggerMap.Dirty, Is.False);
        }

        private IScheduler CreateScheduler(string name, int threadPoolSize)
        {
            DBConnectionManager.Instance.AddConnectionProvider("default", new DbProvider("SqlServer-20", "Server=(local);Database=quartz;Trusted_Connection=True;"));

            var jobStore = new JobStoreTX
            {
                DataSource = "default",
                TablePrefix = "QRTZ_",
                InstanceId = "AUTO",
                DriverDelegateType = typeof(SqlServerDelegate).AssemblyQualifiedName
            };

            DirectSchedulerFactory.Instance.CreateScheduler(name + "Scheduler", "AUTO", new SimpleThreadPool(threadPoolSize, ThreadPriority.Normal), jobStore);
            return SchedulerRepository.Instance.Lookup(name + "Scheduler");
        }
    }
}