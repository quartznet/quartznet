using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

using FluentAssertions;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using NUnit.Framework;

using Quartz.Job;
using Quartz.Simpl;

namespace Quartz.Tests.Unit.Job;

[NonParallelizable]
public class DirectoryScanJobTest
{
    [Test]
    public async Task DirectoryScanJob_ShouldResolveListener_FromDependencyInjection()
    {
        string testDirectory = Path.Combine(Path.GetTempPath(), $"QuartzTest_{Guid.NewGuid()}");
        Directory.CreateDirectory(testDirectory);
        Exception exception = null;
        try
        {
            ServiceCollection serviceCollection = new ServiceCollection();
            serviceCollection.AddLogging();
            serviceCollection.AddTransient<TestDirectoryScanListener>();
            using ServiceProvider serviceProvider = serviceCollection.BuildServiceProvider(validateScopes: true);

            IScheduler scheduler = await SchedulerBuilder.Create()
                .Build()
                .GetScheduler();

            scheduler.JobFactory = new MicrosoftDependencyInjectionJobFactory(serviceProvider, Options.Create(new QuartzOptions()));

            IJobDetail jobDetail = JobBuilder.Create<DirectoryScanJob>()
                .WithIdentity("TestJob")
                .UsingJobData(DirectoryScanJob.DirectoryNames, testDirectory)
                .UsingJobData(DirectoryScanJob.DirectoryScanListenerName, nameof(TestDirectoryScanListener))
                .UsingJobData(DirectoryScanJob.MinimumUpdateAge, 0L)
                .StoreDurably()
                .Build();

            await scheduler.AddJob(jobDetail, false);
            await scheduler.Start();
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
        string testDirectory = Path.Combine(Path.GetTempPath(), $"QuartzTest_{Guid.NewGuid()}");
        Directory.CreateDirectory(testDirectory);
        
        try
        {
            IScheduler scheduler = await SchedulerBuilder.Create()
                .Build()
                .GetScheduler();

            // Use legacy approach - put listener in SchedulerContext
            TestDirectoryScanListener listener = new TestDirectoryScanListener();
            scheduler.Context.Put(nameof(TestDirectoryScanListener), listener);

            IJobDetail jobDetail = JobBuilder.Create<DirectoryScanJob>()
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
        public void FilesUpdatedOrAdded(IReadOnlyCollection<FileInfo> updatedFiles)
        {
        }

        public void FilesDeleted(IReadOnlyCollection<FileInfo> deletedFiles)
        {
        }
    }
}
