using System.Collections;
using System.IO;

using NUnit.Framework;

using Quartz.Listener;
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

    public class NoOpJobListener : JobListenerSupport
    {
        public override string Name
        {
            get { return GetType().Name; }
        }
    }
}
