using FakeItEasy;

using Quartz.Plugins.Xml;
using Quartz.Util;

namespace Quartz.Tests.Unit.Plugin.Xml;

public class XmlSchedulingDataProcessorPluginTest
{
    [Test]
    public async Task WhenFullPathFilesAreSeparatedByCommaSpaceThenPurgeSpaces()
    {
        DirectoryInfo tempDir = Directory.CreateTempSubdirectory("quartz-xml-plugin-test-");
        try
        {
            string fp1 = Path.Combine(tempDir.FullName, "job-data-1.xml");
            string fp2 = Path.Combine(tempDir.FullName, "job-data-2.xml");
            File.WriteAllText(fp1, "");
            File.WriteAllText(fp2, "");

            var dataProcessor = new XmlSchedulingDataProcessorPlugin
            {
                FileNames = fp1 + ", " + fp2
            };
            var mockScheduler = A.Fake<IScheduler>();

            await dataProcessor.Initialize("something", mockScheduler);

            dataProcessor.JobFiles.Should().HaveCount(2);
            dataProcessor.JobFiles.Select(x => x.Key).Should().Equal(fp1, fp2);
        }
        finally
        {
            tempDir.Delete(recursive: true);
        }
    }

    [Test]
    public async Task WhenRelativePathFilesAreSeparatedByCommaSpaceThenPurgeSpaces()
    {
        // '~' always resolves against the application base directory, so the files must not be
        // created there; FailOnFileNotFound = false lets initialization resolve missing files.
        string configuredFileName1 = $"~/{Guid.NewGuid():N}.xml";
        string configuredFileName2 = $"~/{Guid.NewGuid():N}.xml";
        string expectedPathFile1 = FileUtil.ResolveFile(configuredFileName1);
        string expectedPathFile2 = FileUtil.ResolveFile(configuredFileName2);

        var dataProcessor = new XmlSchedulingDataProcessorPlugin
        {
            FileNames = configuredFileName1 + ", " + configuredFileName2,
            FailOnFileNotFound = false
        };
        var mockScheduler = A.Fake<IScheduler>();

        await dataProcessor.Initialize("something", mockScheduler);

        dataProcessor.JobFiles.Should().HaveCount(2);
        dataProcessor.JobFiles.Select(x => x.Key).Should().Equal(expectedPathFile1, expectedPathFile2);
    }

    [Test]
    public async Task ShouldValidateInputXmlWhenConfigured()
    {
        var dataProcessor = new XmlSchedulingDataProcessorPlugin();
        dataProcessor.FileNames = "./Xml/TestData/JobTypeNotFound.xml";
        var mockScheduler = A.Fake<IScheduler>();

        await dataProcessor.Initialize("something", mockScheduler);
        await dataProcessor.Start();

        dataProcessor.FailOnSchedulingError = true;
        Assert.ThrowsAsync<SchedulerException>(async () => await dataProcessor.Start());
    }

    [Test]
    public async Task ShouldLogErrorAndNotifyListenersForInvalidCronExpressionWithFailOnSchedulingErrorTrue()
    {
        // Arrange
        var dataProcessor = new XmlSchedulingDataProcessorPlugin();
        dataProcessor.FileNames = "./Xml/TestData/InvalidCronExpression.xml";
        dataProcessor.FailOnSchedulingError = true;

        var mockScheduler = A.Fake<IScheduler>();
        var mockListenerManager = A.Fake<IListenerManager>();
        var mockSchedulerListener = A.Fake<ISchedulerListener>();

        A.CallTo(() => mockScheduler.ListenerManager).Returns(mockListenerManager);
        A.CallTo(() => mockListenerManager.GetSchedulerListeners())
            .Returns(new[] { mockSchedulerListener });

        await dataProcessor.Initialize("testPlugin", mockScheduler);

        // Act & Assert
        var exception = Assert.ThrowsAsync<SchedulerException>(async () => await dataProcessor.Start());

        // Verify that the error message contains helpful context
        Assert.That(exception!.Message, Does.Contain("Could not schedule jobs and triggers from file"));
        Assert.That(exception.Message, Does.Contain("InvalidCronExpression.xml"));

        // Verify that SchedulerListener.SchedulerError was called
        A.CallTo(() => mockSchedulerListener.SchedulerError(
                A<string>.That.Contains("Could not schedule jobs and triggers from file"),
                A<SchedulerException>._,
                A<System.Threading.CancellationToken>._))
            .MustHaveHappenedOnceExactly();
    }

    [Test]
    public async Task ShouldLogErrorAndNotifyListenersForInvalidCronExpressionWithFailOnSchedulingErrorFalse()
    {
        // Arrange
        var dataProcessor = new XmlSchedulingDataProcessorPlugin();
        dataProcessor.FileNames = "./Xml/TestData/InvalidCronExpression.xml";
        dataProcessor.FailOnSchedulingError = false;

        var mockScheduler = A.Fake<IScheduler>();
        var mockListenerManager = A.Fake<IListenerManager>();
        var mockSchedulerListener = A.Fake<ISchedulerListener>();

        A.CallTo(() => mockScheduler.ListenerManager).Returns(mockListenerManager);
        A.CallTo(() => mockListenerManager.GetSchedulerListeners())
            .Returns(new[] { mockSchedulerListener });

        await dataProcessor.Initialize("testPlugin", mockScheduler);

        // Act - should NOT throw
        await dataProcessor.Start();

        // Verify that SchedulerListener.SchedulerError was called even when not throwing
        A.CallTo(() => mockSchedulerListener.SchedulerError(
                A<string>.That.Contains("Could not schedule jobs and triggers from file"),
                A<SchedulerException>._,
                A<System.Threading.CancellationToken>._))
            .MustHaveHappenedOnceExactly();
    }

    [Test]
    public async Task ShouldContinueNotifyingListenersWhenOneListenerThrows()
    {
        // Arrange
        var dataProcessor = new XmlSchedulingDataProcessorPlugin();
        dataProcessor.FileNames = "./Xml/TestData/InvalidCronExpression.xml";
        dataProcessor.FailOnSchedulingError = false;

        var mockScheduler = A.Fake<IScheduler>();
        var mockListenerManager = A.Fake<IListenerManager>();

        // Create three listeners: one that throws, and two that succeed
        var throwingListener = A.Fake<ISchedulerListener>();
        var successListener1 = A.Fake<ISchedulerListener>();
        var successListener2 = A.Fake<ISchedulerListener>();

        // Configure the throwing listener to throw when SchedulerError is called
        A.CallTo(() => throwingListener.SchedulerError(
                A<string>._,
                A<SchedulerException>._,
                A<System.Threading.CancellationToken>._))
            .Throws(new Exception("Listener failed to process error"));

        A.CallTo(() => mockScheduler.ListenerManager).Returns(mockListenerManager);
        A.CallTo(() => mockListenerManager.GetSchedulerListeners())
            .Returns(new[] { throwingListener, successListener1, successListener2 });

        await dataProcessor.Initialize("testPlugin", mockScheduler);

        // Act - should NOT throw even though one listener throws
        await dataProcessor.Start();

        // Assert - verify that all listeners had SchedulerError called, including the throwing one
        A.CallTo(() => throwingListener.SchedulerError(
                A<string>.That.Contains("Could not schedule jobs and triggers from file"),
                A<SchedulerException>._,
                A<System.Threading.CancellationToken>._))
            .MustHaveHappenedOnceExactly();

        // Verify that the non-throwing listeners were still notified despite the first one throwing
        A.CallTo(() => successListener1.SchedulerError(
                A<string>.That.Contains("Could not schedule jobs and triggers from file"),
                A<SchedulerException>._,
                A<System.Threading.CancellationToken>._))
            .MustHaveHappenedOnceExactly();

        A.CallTo(() => successListener2.SchedulerError(
                A<string>.That.Contains("Could not schedule jobs and triggers from file"),
                A<SchedulerException>._,
                A<System.Threading.CancellationToken>._))
            .MustHaveHappenedOnceExactly();
    }
}
