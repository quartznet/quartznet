using System.IO;
using System.Linq;

using NUnit.Framework;

using Quartz.Plugin.Xml;
using Quartz.Util;

using Rhino.Mocks;

namespace Quartz.Tests.Unit.Plugin.Xml
{
    [TestFixture]
    public class XMLSchedulingDataProcessorPluginTest
    {
        [Test]
        public void WhenFullPathFilesAreSeparatedByCommaSpaceThenPurgeSpaces()
        {
            string fp1 = Path.GetTempFileName();
            File.Create(fp1).Close();
            string fp2 = Path.GetTempFileName();
            File.Create(fp2).Close();

            var dataProcessor = new XMLSchedulingDataProcessorPlugin();
            dataProcessor.FileNames = fp1 + ", " + fp2;
            var mockScheduler = MockRepository.GenerateMock<IScheduler>();

            dataProcessor.Initialize("something", mockScheduler);

            Assert.That(dataProcessor.JobFiles.Count(), Is.EqualTo(2));
            Assert.That(dataProcessor.JobFiles.Select(x => x.Key), Is.EqualTo(new[] {fp1, fp2}));
        }

        [Test]
        public void WhenRelativePathFilesAreSeparatedByCommaSpaceThenPurgeSpaces()
        {
            string configuredFileName1 = "~/File1.xml";
            string expectedPathFile1 = FileUtil.ResolveFile(configuredFileName1);
            if (!File.Exists(expectedPathFile1))
            {
                File.Create(expectedPathFile1);
            }

            string configuredFileName2 = "~/File2.xml";
            string expectedPathFile2 = FileUtil.ResolveFile(configuredFileName2);
            if (!File.Exists(expectedPathFile2))
            {
                File.Create(expectedPathFile2);
            }

            var dataProcessor = new XMLSchedulingDataProcessorPlugin();
            dataProcessor.FileNames = configuredFileName1 + ", " + configuredFileName2;
            var mockScheduler = MockRepository.GenerateMock<IScheduler>();

            dataProcessor.Initialize("something", mockScheduler);

            Assert.That(dataProcessor.JobFiles.Count(), Is.EqualTo(2));
            Assert.That(dataProcessor.JobFiles.Select(x => x.Key).ToArray(), Is.EqualTo(new[] {expectedPathFile1, expectedPathFile2}));
        }
    }
}