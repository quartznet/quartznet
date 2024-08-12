using FakeItEasy;

using Quartz.Plugin.Xml;
using Quartz.Util;

namespace Quartz.Tests.Unit.Plugin.Xml;

public class XMLSchedulingDataProcessorPluginTest
{
    [Test]
    public async Task WhenFullPathFilesAreSeparatedByCommaSpaceThenPurgeSpaces()
    {
        string fp1 = Path.GetTempFileName();
        using (File.Create(fp1))
        {
        }
        string fp2 = Path.GetTempFileName();
        using (File.Create(fp2))
        {
        }

        var dataProcessor = new XMLSchedulingDataProcessorPlugin
        {
            FileNames = fp1 + ", " + fp2
        };
        var mockScheduler = A.Fake<IScheduler>();

        await dataProcessor.Initialize("something", mockScheduler);

        Assert.That(dataProcessor.JobFiles, Has.Count.EqualTo(2));
        Assert.That(dataProcessor.JobFiles.Select(x => x.Key), Is.EqualTo(new[] { fp1, fp2 }));
    }

    [Test]
    [Category("fragile")]
    public async Task WhenRelativePathFilesAreSeparatedByCommaSpaceThenPurgeSpaces()
    {
        string configuredFileName1 = "~/File1.xml";
        string expectedPathFile1 = FileUtil.ResolveFile(configuredFileName1);
        if (!File.Exists(expectedPathFile1))
        {
            File.Create(expectedPathFile1).Close();
        }

        string configuredFileName2 = "~/File2.xml";
        string expectedPathFile2 = FileUtil.ResolveFile(configuredFileName2);
        if (!File.Exists(expectedPathFile2))
        {
            File.Create(expectedPathFile2).Close();
        }

        var dataProcessor = new XMLSchedulingDataProcessorPlugin();
        dataProcessor.FileNames = configuredFileName1 + ", " + configuredFileName2;
        var mockScheduler = A.Fake<IScheduler>();

        await dataProcessor.Initialize("something", mockScheduler);

        Assert.That(dataProcessor.JobFiles, Has.Count.EqualTo(2));
        Assert.That(dataProcessor.JobFiles.Select(x => x.Key).ToArray(), Is.EqualTo(new[] { expectedPathFile1, expectedPathFile2 }));
    }

    [Test]
    public async Task ShouldValidateInputXmlWhenConfigured()
    {
        var dataProcessor = new XMLSchedulingDataProcessorPlugin();
        dataProcessor.FileNames = "./Xml/TestData/JobTypeNotFound.xml";
        var mockScheduler = A.Fake<IScheduler>();

        await dataProcessor.Initialize("something", mockScheduler);
        await dataProcessor.Start();

        dataProcessor.FailOnSchedulingError = true;
        Assert.ThrowsAsync<SchedulerException>(async () => await dataProcessor.Start());
    }
}