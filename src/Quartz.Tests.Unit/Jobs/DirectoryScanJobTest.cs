
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using Quartz.Impl;
using Quartz.Jobs;

namespace Quartz.Tests.Unit.Job;

[NonParallelizable]
public class DirectoryScanJobTest
{
    [Test]
    public async Task DirectoryScanJob_ShouldResolveListener_FromDependencyInjection()
    {
        // Arrange
        string testDirectory = Path.Combine(Path.GetTempPath(), $"QuartzTest_{Guid.NewGuid()}");
        Directory.CreateDirectory(testDirectory);

        Exception exception = null;

        try
        {
            var serviceCollection = new ServiceCollection();
            serviceCollection.AddLogging();
            serviceCollection.AddTransient<TestDirectoryScanListener>();
            var serviceProvider = serviceCollection.BuildServiceProvider(validateScopes: true);

            QuartzSchedulerBuilder builder = QuartzSchedulerBuilder.Create();
            builder.UseJobFactory(new MicrosoftDependencyInjectionJobFactory(serviceProvider));

            IScheduler scheduler = await builder.BuildScheduler();

            var jobDetail = JobBuilder.Create<DirectoryScanJob>()
                .WithIdentity("TestJob")
                .UsingJobData(DirectoryScanJob.DirectoryNames, testDirectory)
                .UsingJobData(DirectoryScanJob.DirectoryScanListenerName, nameof(TestDirectoryScanListener))
                .StoreDurably()
                .Build();

            await scheduler.AddJob(jobDetail);
            await scheduler.Start();

            // First execution to initialize the job - this should not throw if DI resolution works
            try
            {
                await scheduler.TriggerJob(jobDetail.Key);
                await Task.Delay(1000); // Give it time to complete first scan
            }
            catch (Exception ex)
            {
                exception = ex;
            }

            await scheduler.Shutdown();

            // Assert - the main test is that no exception was thrown (listener was found via DI)
            exception.Should().BeNull("DirectoryScanJob should be able to resolve listener from DI without throwing");
        }
        finally
        {
            if (Directory.Exists(testDirectory))
            {
                Directory.Delete(testDirectory, true);
            }
        }
    }

    [Test]
    public async Task DirectoryScanJob_ShouldResolveListener_FromSchedulerContext_ForBackwardCompatibility()
    {
        // Arrange
        string testDirectory = Path.Combine(Path.GetTempPath(), $"QuartzTest_{Guid.NewGuid()}");
        Directory.CreateDirectory(testDirectory);

        try
        {
            IScheduler scheduler = await QuartzSchedulerBuilder.Create().BuildScheduler();

            // Use legacy approach - put listener in SchedulerContext
            var listener = new TestDirectoryScanListener();
            scheduler.Context[nameof(TestDirectoryScanListener)] = listener;

            var jobDetail = JobBuilder.Create<DirectoryScanJob>()
                .WithIdentity("TestJob2")
                .UsingJobData(DirectoryScanJob.DirectoryNames, testDirectory)
                .UsingJobData(DirectoryScanJob.DirectoryScanListenerName, nameof(TestDirectoryScanListener))
                .StoreDurably()
                .Build();

            await scheduler.AddJob(jobDetail);
            await scheduler.Start();

            // First execution to initialize the job - this should not throw if SchedulerContext lookup works
            Exception exception = null;
            try
            {
                await scheduler.TriggerJob(jobDetail.Key);
                await Task.Delay(1000); // Give it time to complete first scan
            }
            catch (Exception ex)
            {
                exception = ex;
            }

            await scheduler.Shutdown();

            // Assert - the main test is that no exception was thrown (listener was found in SchedulerContext)
            exception.Should().BeNull("DirectoryScanJob should be able to resolve listener from SchedulerContext without throwing");
        }
        finally
        {
            if (Directory.Exists(testDirectory))
            {
                Directory.Delete(testDirectory, true);
            }
        }
    }

    [Test]
    public async Task DirectoryScanJob_ShouldScanWhatTheTypedOptionsSay()
    {
        string testDirectory = Path.Combine(Path.GetTempPath(), $"QuartzTest_{Guid.NewGuid()}");
        string subDirectory = Path.Combine(testDirectory, "nested");
        Directory.CreateDirectory(subDirectory);

        try
        {
            string wanted = Path.Combine(testDirectory, "report.csv");
            string nested = Path.Combine(subDirectory, "nested.csv");
            string ignored = Path.Combine(testDirectory, "report.dat");
            foreach (string path in new[] { wanted, nested, ignored })
            {
                await File.WriteAllTextAsync(path, "content");
                File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(-1));
            }

            var listener = new TestDirectoryScanListener();
            var schedulerContext = new SchedulerContext { ["listener"] = listener };

            var job = new DirectoryScanJob();
            var context = TestUtil.NewJobExecutionContextFor(job, schedulerContext);

            // Everything the job needs, said once, in named settings - including the search pattern
            // and the recursion, which used to be reachable only as hardcoded string literals.
            JobDataMap data = new DirectoryScanOptions
            {
                Directories = [testDirectory],
                ScanListenerName = "listener",
                SearchPattern = "*.csv",
                IncludeSubDirectories = true,
                MinimumUpdateAge = TimeSpan.FromSeconds(1),
            }.ToJobData();

            foreach (var pair in data)
            {
                context.MergedJobDataMap[pair.Key] = pair.Value;
            }

            await job.Execute(context);

            TestDirectoryScanListener.UpdatedFileNames.Should().BeEquivalentTo(["report.csv", "nested.csv"],
                "the pattern chose the CSVs and the recursion reached the nested one");
        }
        finally
        {
            if (Directory.Exists(testDirectory))
            {
                Directory.Delete(testDirectory, true);
            }
        }
    }

    private sealed class TestDirectoryScanListener : IDirectoryScanListener
    {
        public static bool FilesUpdatedCalled { get; set; }
        public static bool FilesDeletedCalled { get; set; }
        public static List<string> UpdatedFileNames { get; } = new();
        public static List<string> DeletedFileNames { get; } = new();

        public TestDirectoryScanListener()
        {
            // Reset static state
            FilesUpdatedCalled = false;
            FilesDeletedCalled = false;
            UpdatedFileNames.Clear();
            DeletedFileNames.Clear();
        }

        public ValueTask FilesUpdatedOrAdded(IReadOnlyCollection<FileInfo> updatedFiles, CancellationToken cancellationToken = default)
        {
            FilesUpdatedCalled = true;
            foreach (var file in updatedFiles)
            {
                UpdatedFileNames.Add(file.Name);
            }
            return default;
        }

        public ValueTask FilesDeleted(IReadOnlyCollection<FileInfo> deletedFiles, CancellationToken cancellationToken = default)
        {
            FilesDeletedCalled = true;
            foreach (var file in deletedFiles)
            {
                DeletedFileNames.Add(file.Name);
            }
            return default;
        }
    }
}
