using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;


using Microsoft.Extensions.DependencyInjection;
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

    /// <summary>
    /// The job is <c>[PersistJobDataAfterExecution]</c>, so what it keeps between scans is written to
    /// the job store by whichever serializer is configured. A <c>List&lt;FileInfo&gt;</c> is not a value
    /// System.Text.Json can read back — it is refused on write — so the first firing against such a
    /// store failed to persist; a string map of path to last-write ticks is a shape every serializer
    /// admits, and it is what the next scan reads back.
    /// </summary>
    [Test]
    public async Task TheScannedFileListIsStoredAsSomethingAJobStoreCanWrite()
    {
        string testDirectory = Path.Combine(Path.GetTempPath(), $"QuartzTest_{Guid.NewGuid()}");
        Directory.CreateDirectory(testDirectory);
        string file = Path.Combine(testDirectory, "seen.txt");
        File.WriteAllText(file, "seen");
        // Older than the job's minimum update age, so the first scan counts it as new and records it.
        File.SetLastWriteTimeUtc(file, DateTime.UtcNow.AddMinutes(-1));

        try
        {
            IScheduler scheduler = await SchedulerBuilder.Create()
                .Build()
                .GetScheduler();

            TestDirectoryScanListener listener = new TestDirectoryScanListener();
            scheduler.Context.Put(nameof(TestDirectoryScanListener), listener);

            ExecutionRecordingJobListener executions = new ExecutionRecordingJobListener();
            scheduler.ListenerManager.AddJobListener(executions);

            IJobDetail jobDetail = JobBuilder.Create<DirectoryScanJob>()
                .WithIdentity("StoredShape")
                .UsingJobData(DirectoryScanJob.DirectoryNames, testDirectory)
                .UsingJobData(DirectoryScanJob.DirectoryScanListenerName, nameof(TestDirectoryScanListener))
                .StoreDurably()
                .Build();

            await scheduler.AddJob(jobDetail, false);
            await scheduler.Start();

            await scheduler.TriggerJob(jobDetail.Key);
            await executions.Completed(1);

            executions.Failures.Should().BeEmpty("the first scan has a listener to tell and a directory to read");

            // The job listeners hear of the firing before the store writes the job data back, so the
            // stored map is read once it carries what the scan recorded.
            JobDataMap stored = await StoredJobDataWith(scheduler, jobDetail.Key, "CURRENT_FILE_LIST");

            stored.Should().ContainKey("CURRENT_FILE_LIST");
            stored["CURRENT_FILE_LIST"].Should().BeOfType<Dictionary<string, string>>(
                    "a string map is a value both JSON serializers read back, and a List<FileInfo> is not")
                .Which.Should().ContainKey(Path.GetFullPath(file), "the path is what the next scan compares");

            SystemTextJsonObjectSerializer serializer = new SystemTextJsonObjectSerializer();
            serializer.Initialize();
            Action write = () => serializer.Serialize(stored);
            write.Should().NotThrow("this is the write a persistent store makes after the firing");

            // The next scan reads the stored shape back, and keeps working.
            await scheduler.TriggerJob(jobDetail.Key);
            await executions.Completed(2);

            executions.Failures.Should().BeEmpty("a scan that cannot read what the previous one stored fails the job");

            await scheduler.Shutdown(waitForJobsToComplete: true);
        }
        finally
        {
            if (Directory.Exists(testDirectory))
            {
                Directory.Delete(testDirectory, true);
            }
        }
    }

    /// <summary>
    /// The job's stored data map once it carries <paramref name="key" />, or the map as it stands after
    /// thirty seconds of it not appearing.
    /// </summary>
    private static async Task<JobDataMap> StoredJobDataWith(IScheduler scheduler, JobKey jobKey, string key)
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(30);
        JobDataMap stored = (await scheduler.GetJobDetail(jobKey)).JobDataMap;

        while (!stored.ContainsKey(key) && DateTime.UtcNow < deadline)
        {
            await Task.Delay(25);
            stored = (await scheduler.GetJobDetail(jobKey)).JobDataMap;
        }

        return stored;
    }

    private sealed class ExecutionRecordingJobListener : IJobListener
    {
        private readonly List<Exception> failures = new List<Exception>();
        private readonly List<(int Count, TaskCompletionSource<bool> Source)> waiters = new List<(int, TaskCompletionSource<bool>)>();
        private int completed;

        public string Name => nameof(ExecutionRecordingJobListener);

        public IReadOnlyList<Exception> Failures
        {
            get
            {
                lock (failures)
                {
                    return failures.ToArray();
                }
            }
        }

        /// <summary>A task that completes once <paramref name="count" /> firings have been executed.</summary>
        public Task Completed(int count)
        {
            TaskCompletionSource<bool> source = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            lock (failures)
            {
                if (completed >= count)
                {
                    return Task.CompletedTask;
                }

                waiters.Add((count, source));
            }

            return Task.WhenAny(source.Task, Task.Delay(TimeSpan.FromSeconds(30))).ContinueWith(t =>
            {
                if (!source.Task.IsCompleted)
                {
                    throw new TimeoutException($"The job did not complete {count} firings within 30 seconds.");
                }
            });
        }

        public Task JobToBeExecuted(IJobExecutionContext context, System.Threading.CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task JobExecutionVetoed(IJobExecutionContext context, System.Threading.CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task JobWasExecuted(IJobExecutionContext context, JobExecutionException jobException, System.Threading.CancellationToken cancellationToken = default)
        {
            List<TaskCompletionSource<bool>> ready;
            lock (failures)
            {
                if (jobException != null)
                {
                    failures.Add(jobException);
                }

                completed++;
                ready = new List<TaskCompletionSource<bool>>();
                for (int i = waiters.Count - 1; i >= 0; i--)
                {
                    if (waiters[i].Count <= completed)
                    {
                        ready.Add(waiters[i].Source);
                        waiters.RemoveAt(i);
                    }
                }
            }

            foreach (TaskCompletionSource<bool> source in ready)
            {
                source.TrySetResult(true);
            }

            return Task.CompletedTask;
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
