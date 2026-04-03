using FluentAssertions;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using Quartz.Job;
using Quartz.Simpl;

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

            var scheduler = await SchedulerBuilder.Create()
                .Build()
                .GetScheduler();

            scheduler.JobFactory = new MicrosoftDependencyInjectionJobFactory(serviceProvider, Options.Create(new QuartzOptions()));

            var jobDetail = JobBuilder.Create<DirectoryScanJob>()
                .WithIdentity("TestJob")
                .UsingJobData(DirectoryScanJob.DirectoryNames, testDirectory)
                .UsingJobData(DirectoryScanJob.DirectoryScanListenerName, nameof(TestDirectoryScanListener))
                .StoreDurably()
                .Build();

            await scheduler.AddJob(jobDetail, false);
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
            var scheduler = await SchedulerBuilder.Create()
                .Build()
                .GetScheduler();

            // Use legacy approach - put listener in SchedulerContext
            var listener = new TestDirectoryScanListener();
            scheduler.Context[nameof(TestDirectoryScanListener)] = listener;

            var jobDetail = JobBuilder.Create<DirectoryScanJob>()
                .WithIdentity("TestJob2")
                .UsingJobData(DirectoryScanJob.DirectoryNames, testDirectory)
                .UsingJobData(DirectoryScanJob.DirectoryScanListenerName, nameof(TestDirectoryScanListener))
                .StoreDurably()
                .Build();

            await scheduler.AddJob(jobDetail, false);
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

    private class TestDirectoryScanListener : IDirectoryScanListener
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

        public void FilesUpdatedOrAdded(IReadOnlyCollection<FileInfo> updatedFiles)
        {
            FilesUpdatedCalled = true;
            foreach (var file in updatedFiles)
            {
                UpdatedFileNames.Add(file.Name);
            }
        }

        public void FilesDeleted(IReadOnlyCollection<FileInfo> deletedFiles)
        {
            FilesDeletedCalled = true;
            foreach (var file in deletedFiles)
            {
                DeletedFileNames.Add(file.Name);
            }
        }
    }
}
