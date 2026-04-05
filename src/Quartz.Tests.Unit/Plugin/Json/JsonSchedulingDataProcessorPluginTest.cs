using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

using FakeItEasy;
using FluentAssertions;

using NUnit.Framework;

using Quartz.Plugin.Json;

namespace Quartz.Tests.Unit.Plugin.Json;

public class JsonSchedulingDataProcessorPluginTest
{
    [Test]
    public async Task WhenFilesAreSeparatedByCommaSpace_ThenPurgeSpaces()
    {
        string fp1 = Path.GetTempFileName();
        string fp2 = Path.GetTempFileName();

        try
        {
            JsonSchedulingDataProcessorPlugin plugin = new JsonSchedulingDataProcessorPlugin();
            plugin.FileNames = fp1 + ", " + fp2;
            IScheduler mockScheduler = A.Fake<IScheduler>();

            await plugin.Initialize("jsonPlugin", mockScheduler);

            // Plugin is sealed so we verify via Start behavior — no exception means files were found
            plugin.Name.Should().Be("jsonPlugin");
        }
        finally
        {
            File.Delete(fp1);
            File.Delete(fp2);
        }
    }

    [Test]
    public async Task WhenFileNotFound_AndFailOnFileNotFoundTrue_ThenThrows()
    {
        JsonSchedulingDataProcessorPlugin plugin = new JsonSchedulingDataProcessorPlugin();
        plugin.FileNames = "nonexistent_quartz_jobs.json";
        plugin.FailOnFileNotFound = true;
        IScheduler mockScheduler = A.Fake<IScheduler>();

        Func<Task> act = async () => await plugin.Initialize("jsonPlugin", mockScheduler);

        await act.Should().ThrowAsync<SchedulerException>()
            .WithMessage("*does not exist*");
    }

    [Test]
    public async Task WhenFileNotFound_AndFailOnFileNotFoundFalse_ThenDoesNotThrow()
    {
        JsonSchedulingDataProcessorPlugin plugin = new JsonSchedulingDataProcessorPlugin();
        plugin.FileNames = "nonexistent_quartz_jobs.json";
        plugin.FailOnFileNotFound = false;
        IScheduler mockScheduler = A.Fake<IScheduler>();

        // Should not throw
        await plugin.Initialize("jsonPlugin", mockScheduler);
    }

    [Test]
    public async Task Start_WithValidJsonFile_SchedulesJobs()
    {
        string json = @"{
            ""Schedule"": {
                ""Jobs"": [{
                    ""Name"": ""pluginJob"",
                    ""JobType"": ""Quartz.Job.NativeJob, Quartz.Jobs"",
                    ""Durable"": true
                }],
                ""Triggers"": [{
                    ""Name"": ""pluginTrigger"",
                    ""JobName"": ""pluginJob"",
                    ""Cron"": { ""Expression"": ""0/30 * * * * ?"" }
                }]
            }
        }";

        string tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempFile, json);

            JsonSchedulingDataProcessorPlugin plugin = new JsonSchedulingDataProcessorPlugin();
            plugin.FileNames = tempFile;

            IScheduler mockScheduler = A.Fake<IScheduler>();
            A.CallTo(() => mockScheduler.GetJobGroupNames(A<System.Threading.CancellationToken>._))
                .Returns(Array.Empty<string>());
            A.CallTo(() => mockScheduler.GetTriggerGroupNames(A<System.Threading.CancellationToken>._))
                .Returns(Array.Empty<string>());

            await plugin.Initialize("jsonPlugin", mockScheduler);
            await plugin.Start();

            // Verify the job was added to the scheduler
            A.CallTo(() => mockScheduler.AddJob(
                    A<IJobDetail>.That.Matches(j => j.Key.Name == "pluginJob"),
                    A<bool>._,
                    A<bool>._,
                    A<System.Threading.CancellationToken>._))
                .MustHaveHappened();
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Test]
    public async Task Start_WithScanInterval_SchedulesFileScanJob()
    {
        string json = @"{ ""Schedule"": { ""Jobs"": [], ""Triggers"": [] } }";
        string tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempFile, json);

            JsonSchedulingDataProcessorPlugin plugin = new JsonSchedulingDataProcessorPlugin();
            plugin.FileNames = tempFile;
            plugin.ScanInterval = TimeSpan.FromMinutes(1);

            IScheduler mockScheduler = A.Fake<IScheduler>();
            A.CallTo(() => mockScheduler.GetJobGroupNames(A<System.Threading.CancellationToken>._))
                .Returns(Array.Empty<string>());
            A.CallTo(() => mockScheduler.GetTriggerGroupNames(A<System.Threading.CancellationToken>._))
                .Returns(Array.Empty<string>());

            await plugin.Initialize("jsonPlugin", mockScheduler);
            await plugin.Start();

            // Verify a FileScanJob was scheduled for periodic scanning
            A.CallTo(() => mockScheduler.ScheduleJob(
                    A<IJobDetail>.That.Matches(j => j.JobType.Name == "FileScanJob"),
                    A<ITrigger>._,
                    A<System.Threading.CancellationToken>._))
                .MustHaveHappened();
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Test]
    public async Task Start_WithInvalidJson_AndFailOnSchedulingError_Throws()
    {
        string tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempFile, "{ invalid json }");

            JsonSchedulingDataProcessorPlugin plugin = new JsonSchedulingDataProcessorPlugin();
            plugin.FileNames = tempFile;
            plugin.FailOnSchedulingError = true;

            IScheduler mockScheduler = A.Fake<IScheduler>();
            IListenerManager mockListenerManager = A.Fake<IListenerManager>();
            A.CallTo(() => mockScheduler.ListenerManager).Returns(mockListenerManager);
            A.CallTo(() => mockListenerManager.GetSchedulerListeners())
                .Returns(Array.Empty<ISchedulerListener>());

            await plugin.Initialize("jsonPlugin", mockScheduler);

            Func<Task> act = async () => await plugin.Start();
            await act.Should().ThrowAsync<SchedulerException>()
                .WithMessage("*Could not schedule jobs*");
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Test]
    public async Task Start_WithInvalidJson_AndFailOnSchedulingErrorFalse_NotifiesListeners()
    {
        string tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempFile, "{ invalid json }");

            JsonSchedulingDataProcessorPlugin plugin = new JsonSchedulingDataProcessorPlugin();
            plugin.FileNames = tempFile;
            plugin.FailOnSchedulingError = false;

            IScheduler mockScheduler = A.Fake<IScheduler>();
            IListenerManager mockListenerManager = A.Fake<IListenerManager>();
            ISchedulerListener mockListener = A.Fake<ISchedulerListener>();
            A.CallTo(() => mockScheduler.ListenerManager).Returns(mockListenerManager);
            A.CallTo(() => mockListenerManager.GetSchedulerListeners())
                .Returns(new[] { mockListener });

            await plugin.Initialize("jsonPlugin", mockScheduler);

            // Should NOT throw
            await plugin.Start();

            // But should notify listeners
            A.CallTo(() => mockListener.SchedulerError(
                    A<string>.That.Contains("Could not schedule jobs"),
                    A<SchedulerException>._,
                    A<System.Threading.CancellationToken>._))
                .MustHaveHappenedOnceExactly();
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Test]
    public async Task Shutdown_DoesNotThrow()
    {
        JsonSchedulingDataProcessorPlugin plugin = new JsonSchedulingDataProcessorPlugin();
        IScheduler mockScheduler = A.Fake<IScheduler>();
        plugin.FailOnFileNotFound = false;
        plugin.FileNames = "nonexistent.json";

        await plugin.Initialize("jsonPlugin", mockScheduler);
        await plugin.Shutdown();
    }
}
