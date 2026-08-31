using FakeItEasy;

using Quartz.Plugins.Json;

namespace Quartz.Tests.Unit.Plugin.Json;

/// <summary>
/// The JSON scheduling plugin, tested the way its XML twin is: the two have one surface, and what
/// holds for one of them is asked of the other here.
/// </summary>
public class JsonSchedulingDataProcessorPluginTest
{
    [Test]
    public async Task WhenFullPathFilesAreSeparatedByCommaSpaceThenPurgeSpaces()
    {
        DirectoryInfo tempDir = Directory.CreateTempSubdirectory("quartz-json-plugin-test-");
        try
        {
            string fp1 = Path.Combine(tempDir.FullName, "job-data-1.json");
            string fp2 = Path.Combine(tempDir.FullName, "job-data-2.json");
            File.WriteAllText(fp1, "");
            File.WriteAllText(fp2, "");

            JsonSchedulingDataProcessorPlugin plugin = new()
            {
                FileNames = fp1 + ", " + fp2
            };

            await plugin.Initialize("something", A.Fake<IScheduler>());

            plugin.JobFiles.Should().HaveCount(2);
            plugin.JobFiles.Select(x => x.Key).Should().Equal(fp1, fp2);
        }
        finally
        {
            tempDir.Delete(recursive: true);
        }
    }

    [Test]
    public async Task StartSchedulesWhatTheFileDeclares()
    {
        DirectoryInfo tempDir = Directory.CreateTempSubdirectory("quartz-json-plugin-test-");
        IScheduler scheduler = await CreateScheduler();
        try
        {
            string file = Path.Combine(tempDir.FullName, "quartz_jobs.json");
            await File.WriteAllTextAsync(file, JobsNamed("firstJob"));

            JsonSchedulingDataProcessorPlugin plugin = new()
            {
                FileNames = file,
                FailOnSchedulingError = true
            };

            await plugin.Initialize("json", scheduler);
            await plugin.Start();

            (await scheduler.Exists(new JobKey("firstJob"))).Should().BeTrue(
                "starting the plugin reads every file it was given");
        }
        finally
        {
            await scheduler.Shutdown();
            tempDir.Delete(recursive: true);
        }
    }

    [Test]
    public async Task AFileChangedAfterStartIsReadAgain()
    {
        DirectoryInfo tempDir = Directory.CreateTempSubdirectory("quartz-json-plugin-test-");
        IScheduler scheduler = await CreateScheduler();
        try
        {
            string file = Path.Combine(tempDir.FullName, "quartz_jobs.json");
            await File.WriteAllTextAsync(file, JobsNamed("firstJob"));

            JsonSchedulingDataProcessorPlugin plugin = new()
            {
                FileNames = file,
                FailOnSchedulingError = true
            };

            await plugin.Initialize("json", scheduler);
            await plugin.Start();

            await File.WriteAllTextAsync(file, JobsNamed("firstJob", "secondJob"));
            await plugin.FileUpdated(file);

            (await scheduler.Exists(new JobKey("secondJob"))).Should().BeTrue(
                "the file scan job reports a change through IFileScanListener.FileUpdated, which is the "
                + "whole of what ScanInterval buys");
        }
        finally
        {
            await scheduler.Shutdown();
            tempDir.Delete(recursive: true);
        }
    }

    [Test]
    public async Task AFileChangedBeforeStartIsLeftToStart()
    {
        DirectoryInfo tempDir = Directory.CreateTempSubdirectory("quartz-json-plugin-test-");
        IScheduler scheduler = await CreateScheduler();
        try
        {
            string file = Path.Combine(tempDir.FullName, "quartz_jobs.json");
            await File.WriteAllTextAsync(file, JobsNamed("firstJob"));

            JsonSchedulingDataProcessorPlugin plugin = new()
            {
                FileNames = file,
                FailOnSchedulingError = true
            };

            await plugin.Initialize("json", scheduler);
            await plugin.FileUpdated(file);

            (await scheduler.Exists(new JobKey("firstJob"))).Should().BeFalse(
                "an update before the scheduler started is not read twice: Start reads every file, and "
                + "reading one of them ahead of it would schedule against a scheduler that is not ready");
        }
        finally
        {
            await scheduler.Shutdown();
            tempDir.Delete(recursive: true);
        }
    }

    private static ValueTask<IScheduler> CreateScheduler()
    {
        return QuartzSchedulerBuilder.Create()
            .ConfigureScheduler(options => options.InstanceName = "json-plugin-test-" + Guid.NewGuid().ToString("N"))
            .UseInMemoryStore()
            .BuildScheduler();
    }

    private static string JobsNamed(params string[] names)
    {
        IEnumerable<string> jobs = names.Select(name =>
            $$"""{ "Name": "{{name}}", "JobType": "Quartz.Jobs.NativeJob, Quartz.Jobs", "Durable": true }""");

        return $$"""
        {
            "Schedule": {
                "Jobs": [{{string.Join(", ", jobs)}}]
            }
        }
        """;
    }
}
