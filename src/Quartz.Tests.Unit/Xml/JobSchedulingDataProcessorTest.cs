#region License
/* 
 * Copyright 2001-2009 Terracotta, Inc. 
 * 
 * Licensed under the Apache License, Version 2.0 (the "License"); you may not 
 * use this file except in compliance with the License. You may obtain a copy 
 * of the License at 
 * 
 *   http://www.apache.org/licenses/LICENSE-2.0 
 *   
 * Unless required by applicable law or agreed to in writing, software 
 * distributed under the License is distributed on an "AS IS" BASIS, WITHOUT 
 * WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the 
 * License for the specific language governing permissions and limitations 
 * under the License.
 * 
 */
#endregion

using System.Collections.Generic;
using System.IO;
using Quartz.Xml;
using Quartz.Xml.JobSchedulingData10;

using Rhino.Mocks;

using NUnit.Framework;

namespace Quartz.Tests.Unit.Xml
{
    /// <author>Marko Lahma (.NET)</author>
    [TestFixture]
    public class JobSchedulingDataProcessorTest 
    {
        private JobSchedulingDataProcessor processor;
        private IScheduler mockScheduler;

        [SetUp]
        public void SetUp()
        {
            processor = new JobSchedulingDataProcessor(true, true);
            mockScheduler =  MockRepository.GenerateMock<IScheduler>();
        }

        [Test]
        public void TestScheduling_RichConfiguration()
        {
            Stream s = ReadJobXmlFromEmbeddedResource("RichConfiguration.xml");
            processor.ProcessStream(s, null);
           

            processor.ScheduleJobs(new Dictionary<string, JobSchedulingBundle>(), mockScheduler, false);
        }

        [Test]
        public void TestScheduling_RichConfiguration_ShouldReadJobDataMapFromTrigger()
        {
            Stream s = ReadJobXmlFromEmbeddedResource("RichConfiguration.xml");
            processor.ProcessStream(s, null);

            processor.ScheduleJobs(new Dictionary<string, JobSchedulingBundle>(), mockScheduler, false);

            JobSchedulingBundle job = processor.GetScheduledJob("jobGroup1.jobName1");
            foreach (Trigger trigger in job.Triggers)
            {
                string keyValuePrefix = trigger is CronTrigger ? "Cron" : "Simple";
                Assert.AreEqual(2, trigger.JobDataMap.Count, "Should have had 2 items in job data map");
                for (int i = 1; i <= 2; ++i)
                {
                    string entryKey = keyValuePrefix + "Entry_" + i;
                    Assert.IsTrue(trigger.JobDataMap.Keys.Contains(entryKey));
                    Assert.AreEqual(keyValuePrefix + "Value_" + i, trigger.JobDataMap[entryKey]);
                }
            }

        }

        [Test]
        public void TestScheduling_RichConfiguration_ShouldReadTriggerListeners()
        {
            Stream s = ReadJobXmlFromEmbeddedResource("RichConfiguration.xml");
            processor.ProcessStream(s, null);

            processor.ScheduleJobs(new Dictionary<string, JobSchedulingBundle>(), mockScheduler, false);

            JobSchedulingBundle job = processor.GetScheduledJob("jobGroup1.jobName1");
            foreach (Trigger trigger in job.Triggers)
            {
                if (trigger is CronTrigger)
                {
                }
            }

        }

        [Test]
        public void TestScheduling_RichConfiguration_ShouldSetDecriptionToTriggers()
        {
            Stream s = ReadJobXmlFromEmbeddedResource("RichConfiguration.xml");
            processor.ProcessStream(s, null);

            processor.ScheduleJobs(new Dictionary<string, JobSchedulingBundle>(), mockScheduler, false);

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

            processor.ScheduleJobs(new Dictionary<string, JobSchedulingBundle>(), mockScheduler, false);
			Assert.IsFalse(processor.OverwriteExistingJobs);
        }

        private static Stream ReadJobXmlFromEmbeddedResource(string resourceName)
        {
            string fullName = "Quartz.Tests.Unit.Xml.TestData." + resourceName;
            return new StreamReader(typeof (JobSchedulingDataProcessorTest).Assembly.GetManifestResourceStream(fullName)).BaseStream;
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
		private string _name;

		public string Name
		{
			get { return _name ?? GetType().Name; }
			set { _name = value; }
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
