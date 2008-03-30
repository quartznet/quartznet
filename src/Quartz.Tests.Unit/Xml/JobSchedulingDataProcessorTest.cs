using System.Collections;
using System.IO;

using NUnit.Framework;

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
        public void TestScheduling_MinimalConfiguration()
        {
            Stream s = ReadJobXmlFromEmbeddedResource("MinimalConfiguration.xml");
            processor.ProcessStream(s, null);

            mockery.ReplayAll();

            processor.ScheduleJobs(new Hashtable(), mockScheduler, false);
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
}
