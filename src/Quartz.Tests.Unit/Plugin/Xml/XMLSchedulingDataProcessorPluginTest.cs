using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

using FakeItEasy;

using NUnit.Framework;

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

        var dataProcessor = new XMLSchedulingDataProcessorPlugin();
        dataProcessor.FileNames = fp1 + ", " + fp2;
        var mockScheduler = A.Fake<IScheduler>();

        await dataProcessor.Initialize("something", mockScheduler);

        Assert.That(dataProcessor.JobFiles.Count(), Is.EqualTo(2));
        Assert.That(dataProcessor.JobFiles.Select(x => x.Key), Is.EqualTo(new[] {fp1, fp2}));
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

        Assert.That(dataProcessor.JobFiles.Count(), Is.EqualTo(2));
        Assert.That(dataProcessor.JobFiles.Select(x => x.Key).ToArray(), Is.EqualTo(new[] {expectedPathFile1, expectedPathFile2}));
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

    [Test]
    public async Task ShouldLogErrorAndNotifyListenersForInvalidCronExpressionWithFailOnSchedulingErrorTrue()
    {
        // Arrange
        var dataProcessor = new XMLSchedulingDataProcessorPlugin();
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
        var dataProcessor = new XMLSchedulingDataProcessorPlugin();
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
        var dataProcessor = new XMLSchedulingDataProcessorPlugin();
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