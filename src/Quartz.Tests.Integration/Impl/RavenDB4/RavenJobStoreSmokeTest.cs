using System.Collections.Specialized;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

using NUnit.Framework;

using Quartz.Impl;
using Quartz.Impl.RavenDB;
using Quartz.Logging;
using Quartz.Util;

using Raven.Client.Documents;
using Raven.Client.ServerWide;
using Raven.Client.ServerWide.Operations;

namespace Quartz.Tests.Integration.Impl.RavenDB4
{
    [TestFixture]
    [Category("database")]
    public class RavenJobStoreSmokeTest
    {
        private ILogProvider oldProvider;

        [OneTimeSetUp]
        public void FixtureSetUp()
        {
            // set Adapter to report problems
            oldProvider = (ILogProvider) typeof(LogProvider).GetField("s_currentLogProvider", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);
            LogProvider.SetCurrentLogProvider(new FailFastLoggerFactoryAdapter());
        }

        [OneTimeTearDown]
        public void FixtureTearDown()
        {
            // default back to old
            LogProvider.SetCurrentLogProvider(oldProvider);
        }

        [Test]
        [Category("ravendb")]
        public async Task TestRavenJobStore()
        {
            const string RavenUrl = "http://localhost:9999";
            const string DatabaseName = "quartznet";

            var store = new DocumentStore
            {
                Urls = new[]
                {
                    RavenUrl
                }
            };
            store.Initialize();

            var names = await store.Maintenance.Server.SendAsync(new GetDatabaseNamesOperation(0, 1024));
            if (!names.Contains(DatabaseName))
            {
                await store.Maintenance.Server.SendAsync(new CreateDatabaseOperation(new DatabaseRecord(DatabaseName)));
            }

            NameValueCollection properties = new NameValueCollection();
            properties["quartz.scheduler.instanceName"] = "TestScheduler";
            properties["quartz.scheduler.instanceId"] = "instance_one";
            properties["quartz.threadPool.type"] = "Quartz.Simpl.SimpleThreadPool, Quartz";
            properties["quartz.threadPool.threadCount"] = "10";
            properties["quartz.jobStore.misfireThreshold"] = "60000";
            properties["quartz.jobStore.type"] = typeof(RavenJobStore).AssemblyQualifiedNameWithoutVersion();
            properties["quartz.jobStore.url"] = RavenUrl;
            properties["quartz.jobStore.database"] = DatabaseName;
            // not really used
            properties["quartz.serializer.type"] = "json";

            // Clear any old errors from the log
            FailFastLoggerFactoryAdapter.Errors.Clear();

            // First we must get a reference to a scheduler
            ISchedulerFactory sf = new StdSchedulerFactory(properties);
            IScheduler sched = await sf.GetScheduler();
            SmokeTestPerformer performer = new SmokeTestPerformer();
            await performer.Test(sched, clearJobs: true, scheduleJobs: true);

            Assert.IsEmpty(FailFastLoggerFactoryAdapter.Errors, "Found error from logging output");
        }
    }
}