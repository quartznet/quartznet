using System.Collections;
using System.IO;

using NUnit.Framework;

using Quartz.Impl;
using Quartz.Xml;

using Rhino.Mocks;

namespace Quartz.Tests.Unit.Xml
{
    [TestFixture]
    public class JobSchedulingDataProcessorTest 
    {
        private JobSchedulingDataProcessor processor;
        private MockRepository mockery;
        private IScheduler mockScheduler;

        [SetUp]
        public void SetUp()
        {
            mockery = new MockRepository();
            processor = new JobSchedulingDataProcessor(true, true);
            mockScheduler =  (IScheduler) mockery.DynamicMock(typeof(IScheduler));
        }

        [Test]
        public void TestScheduling_RichConfiguration()
        {
            Stream s = ReadJobXmlFromEmbeddedResource("RichConfiguration.xml");
            processor.ProcessStream(s, null);
           
            mockery.ReplayAll();
            
            processor.ScheduleJobs(new Hashtable(), mockScheduler, false);
        }

        [Test]
        public void TestScheduling_RichConfiguration_ShouldReadJobDataMapFromTrigger()
        {
            Stream s = ReadJobXmlFromEmbeddedResource("RichConfiguration.xml");
            processor.ProcessStream(s, null);

            mockery.ReplayAll();

            processor.ScheduleJobs(new Hashtable(), mockScheduler, false);

            JobSchedulingBundle job = processor.GetScheduledJob("jobGroup1.jobName1");
            foreach (Trigger trigger in job.Triggers)
            {
                string keyValuePrefix = trigger is CronTrigger ? "Cron" : "Simple";
                Assert.AreEqual(2, trigger.JobDataMap.Count, "Should have had 2 items in job data map");
                for (int i = 1; i <= 2; ++i)
                {
                    string entryKey = keyValuePrefix + "Entry_" + i;
                    Assert.Contains(entryKey, trigger.JobDataMap.Keys);
                    Assert.AreEqual(keyValuePrefix + "Value_" + i, trigger.JobDataMap[entryKey]);
                }
            }

        }

        [Test]
        public void TestScheduling_RichConfiguration_ShouldSetDecriptionToTriggers()
        {
            Stream s = ReadJobXmlFromEmbeddedResource("RichConfiguration.xml");
            processor.ProcessStream(s, null);

            mockery.ReplayAll();

            processor.ScheduleJobs(new Hashtable(), mockScheduler, false);

            JobSchedulingBundle job = processor.GetScheduledJob("jobGroup1.jobName1");
            foreach (Trigger trigger in job.Triggers)
            {
                string keyValuePrefix = trigger is CronTrigger ? "Cron" : "Simple";
                Assert.AreEqual(keyValuePrefix + "TriggerDescription", trigger.Description, "Should have had correct description for trigger");
            }

        }

        [Test]
        public void TestScheduling_MinimalConfiguration()
        {
            Stream s = ReadJobXmlFromEmbeddedResource("MinimalConfiguration.xml");
            processor.ProcessStream(s, null);

            mockery.ReplayAll();

            processor.ScheduleJobs(new Hashtable(), mockScheduler, false);
        }

        [Test]
        public void TestScheduling_MinimalConfiguration_ShouldNotOverwriteExistingJobsIfAdviced()
        {
            Stream s = ReadJobXmlFromEmbeddedResource("MinimalConfiguration.xml");
            processor.ProcessStream(s, null);

            mockery.ReplayAll();

            processor.ScheduleJobs(new Hashtable(), mockScheduler, false);
            Assert.IsFalse(processor.OverwriteExistingJobs);
        }

        private static Stream ReadJobXmlFromEmbeddedResource(string resourceName)
        {
            string fullName = "Quartz.Tests.Unit.Xml.TestData." + resourceName;
            return new StreamReader(typeof (JobSchedulingDataProcessorTest).Assembly.GetManifestResourceStream(fullName)).BaseStream;
        }

        [TearDown]
        public void TearDown()
        {
            mockery.VerifyAll();
        }
    }

    public class NoOpJobListener : IJobListener
    {
        public string Name
        {
            get { return GetType().Name; }
            set { }
        }

        public void JobToBeExecuted(JobExecutionContext context)
        {
            
        }

        public void JobExecutionVetoed(JobExecutionContext context)
        {
            
        }

        public void JobWasExecuted(JobExecutionContext context, JobExecutionException jobException)
        {
            
        }
    }

	public class NoOpTriggerListener : ITriggerListener 
	{
		private string name;

		public string Name
		{
			get { return name == null ? GetType().Name : name; }
			set { name = value; }
		}

		public void TriggerFired(Trigger trigger, JobExecutionContext context)
		{
		}

		public bool VetoJobExecution(Trigger trigger, JobExecutionContext context)
		{
			return false;
		}

		public void TriggerMisfired(Trigger trigger)
		{
		}

		public void TriggerComplete(Trigger trigger, JobExecutionContext context, SchedulerInstruction triggerInstructionCode)
		{
		}
	}
}
