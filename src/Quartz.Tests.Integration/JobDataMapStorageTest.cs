using NUnit.Framework;

using Quartz.Impl;
using Quartz.Impl.AdoJobStore;
using Quartz.Impl.AdoJobStore.Common;
using Quartz.Job;
using Quartz.Simpl;
using Quartz.Util;

namespace Quartz.Tests.Integration
{
    [TestFixture]
    public class JobDataMapStorageTest : IntegrationTest
    {
        [Test]
        [Category("db-sqlserver")]
        public async Task TestJobDataMapDirtyFlag()
        {
            IScheduler scheduler = await CreateScheduler("testBasicStorageFunctions");
            await scheduler.Clear();

            IJobDetail jobDetail = JobBuilder.Create<NoOpJob>()
                .WithIdentity("test")
                .UsingJobData("jfoo", "bar")
                .Build();

            ITrigger trigger = TriggerBuilder.Create()
                .WithIdentity("test")
                .WithCronSchedule("0 0 0 * * ?")
                .UsingJobData("tfoo", "bar")
                .Build();

            await scheduler.ScheduleJob(jobDetail, trigger);

            IJobDetail storedJobDetail = await scheduler.GetJobDetail(new JobKey("test"));
            JobDataMap storedJobMap = storedJobDetail.JobDataMap;
            Assert.That(storedJobMap.Dirty, Is.False);

            ITrigger storedTrigger = await scheduler.GetTrigger(new TriggerKey("test"));
            JobDataMap storedTriggerMap = storedTrigger.JobDataMap;
            Assert.That(storedTriggerMap.Dirty, Is.False);
        }

        private Task<IScheduler> CreateScheduler(string name)
        {
            DBConnectionManager.Instance.AddConnectionProvider("default", new DbProvider(TestConstants.DefaultSqlServerProvider, TestConstants.SqlServerConnectionString));

            var serializer = new JsonObjectSerializer();
            serializer.Initialize();
            var jobStore = new JobStoreTX
            {
                DataSource = "default",
                TablePrefix = "QRTZ_",
                InstanceId = "AUTO",
                DriverDelegateType = typeof(SqlServerDelegate).AssemblyQualifiedName,
                ObjectSerializer = serializer
            };

            DirectSchedulerFactory.Instance.CreateScheduler(name + "Scheduler", "AUTO", new DefaultThreadPool(), jobStore);
            return SchedulerRepository.Instance.Lookup(name + "Scheduler");
        }
    }
}