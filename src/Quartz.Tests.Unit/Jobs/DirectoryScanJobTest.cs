
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using Quartz.Impl;
using Quartz.Jobs;

namespace Quartz.Tests.Unit.Job;

[NonParallelizable]
public class DirectoryScanJobTest
{
    /// <summary>
    /// The listener the job data names is resolved from the container, either from a keyed registration
    /// or by matching the type's name against what is registered as an <see cref="IDirectoryScanListener" />.
    /// </summary>
    /// <remarks>
    /// It used to be found by sweeping every loaded assembly with <c>GetTypes()</c> on the first miss and
    /// caching the answer, misses included, in a process-lifetime dictionary keyed on caller-controlled
    /// job data. The tests that covered this asserted only that <c>TriggerJob</c> did not throw, which it
    /// never would: a job's failure does not come back out of the call that fired it.
    /// </remarks>
    [TestCase(true, TestName = "TheListenerIsResolvedFromTheContainer(keyed)")]
    [TestCase(false, TestName = "TheListenerIsResolvedFromTheContainer(by type name)")]
    public async Task TheListenerIsResolvedFromTheContainer(bool keyed)
    {
        string testDirectory = Path.Combine(Path.GetTempPath(), $"QuartzTest_{Guid.NewGuid()}");
        Directory.CreateDirectory(testDirectory);

        try
        {
            RecordingDirectoryScanListener listener = new();
            ServiceCollection services = [];
            if (keyed)
            {
                services.AddKeyedSingleton<IDirectoryScanListener>("inbox", listener);
            }
            else
            {
                services.AddSingleton<IDirectoryScanListener>(listener);
            }

            await using ServiceProvider serviceProvider = services.BuildServiceProvider();

            DirectoryScanJob job = new(serviceProvider, TimeProvider.System);
            IJobExecutionContext context = TestUtil.NewJobExecutionContextFor(job, new SchedulerContext());

            await GivenAFileIn(testDirectory, "report.csv");

            JobDataMap data = new DirectoryScanOptions
            {
                Directories = [testDirectory],
                ScanListenerName = keyed ? "inbox" : nameof(RecordingDirectoryScanListener),
                MinimumUpdateAge = TimeSpan.FromMilliseconds(1),
            }.ToJobData();

            foreach (KeyValuePair<string, object> pair in data)
            {
                context.MergedJobDataMap[pair.Key] = pair.Value;
            }

            await job.Execute(context);

            listener.UpdatedFileNames.Should().Contain("report.csv",
                "the listener the job data named is the one that was called");
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
    /// A name the container does not answer for still reaches the <see cref="SchedulerContext" />, which
    /// is how <c>FileScanJob</c> has always found its own listener.
    /// </summary>
    [Test]
    public async Task TheListenerIsResolvedFromTheSchedulerContextWhenTheContainerDoesNotAnswer()
    {
        string testDirectory = Path.Combine(Path.GetTempPath(), $"QuartzTest_{Guid.NewGuid()}");
        Directory.CreateDirectory(testDirectory);

        try
        {
            RecordingDirectoryScanListener listener = new();
            ServiceCollection services = [];
            await using ServiceProvider serviceProvider = services.BuildServiceProvider();

            SchedulerContext schedulerContext = new() { ["inbox"] = listener };
            DirectoryScanJob job = new(serviceProvider, TimeProvider.System);
            IJobExecutionContext context = TestUtil.NewJobExecutionContextFor(job, schedulerContext);

            await GivenAFileIn(testDirectory, "report.csv");

            JobDataMap data = new DirectoryScanOptions
            {
                Directories = [testDirectory],
                ScanListenerName = "inbox",
                MinimumUpdateAge = TimeSpan.FromMilliseconds(1),
            }.ToJobData();

            foreach (KeyValuePair<string, object> pair in data)
            {
                context.MergedJobDataMap[pair.Key] = pair.Value;
            }

            await job.Execute(context);

            listener.UpdatedFileNames.Should().Contain("report.csv");
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
    /// A name nothing answers for is a configuration mistake, and the message says every place the job
    /// looked.
    /// </summary>
    [Test]
    public async Task AListenerNameNothingAnswersForNamesWhereTheJobLooked()
    {
        string testDirectory = Path.Combine(Path.GetTempPath(), $"QuartzTest_{Guid.NewGuid()}");
        Directory.CreateDirectory(testDirectory);

        try
        {
            ServiceCollection services = [];
            await using ServiceProvider serviceProvider = services.BuildServiceProvider();

            DirectoryScanJob job = new(serviceProvider, TimeProvider.System);
            IJobExecutionContext context = TestUtil.NewJobExecutionContextFor(job, new SchedulerContext());

            JobDataMap data = new DirectoryScanOptions
            {
                Directories = [testDirectory],
                ScanListenerName = "NoSuchListener",
            }.ToJobData();

            foreach (KeyValuePair<string, object> pair in data)
            {
                context.MergedJobDataMap[pair.Key] = pair.Value;
            }

            Func<Task> act = async () => await job.Execute(context);

            await act.Should().ThrowAsync<JobExecutionException>()
                .WithMessage("*NoSuchListener*")
                .WithMessage("*SchedulerContext*");
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
    /// The scan's bookkeeping is stored as something a job store can write back. The job is
    /// <c>[PersistJobDataAfterExecution]</c>, and a <c>List&lt;FileInfo&gt;</c> is a value both shipped
    /// serializers refuse outright - so the first firing against a persistent store failed to persist,
    /// and reading the job's data map over the HTTP API refused it too.
    /// </summary>
    [Test]
    public async Task TheScannedFileListIsStoredAsSomethingAJobStoreCanWrite()
    {
        string testDirectory = Path.Combine(Path.GetTempPath(), $"QuartzTest_{Guid.NewGuid()}");
        Directory.CreateDirectory(testDirectory);

        try
        {
            RecordingDirectoryScanListener listener = new();
            SchedulerContext schedulerContext = new() { ["inbox"] = listener };
            ServiceCollection services = [];
            await using ServiceProvider serviceProvider = services.BuildServiceProvider();
            DirectoryScanJob job = new(serviceProvider, TimeProvider.System);
            IJobExecutionContext context = TestUtil.NewJobExecutionContextFor(job, schedulerContext);

            string path = await GivenAFileIn(testDirectory, "report.csv");

            JobDataMap data = new DirectoryScanOptions
            {
                Directories = [testDirectory],
                ScanListenerName = "inbox",
                MinimumUpdateAge = TimeSpan.FromMilliseconds(1),
            }.ToJobData();

            foreach (KeyValuePair<string, object> pair in data)
            {
                context.MergedJobDataMap[pair.Key] = pair.Value;
            }

            await job.Execute(context);

            object stored = context.JobDetail.JobDataMap["CURRENT_FILE_LIST"];
            stored.Should().BeOfType<Dictionary<string, string>>(
                "a string-to-string dictionary is a shape both serializers admit; a List<FileInfo> is not")
                .Which.Should().ContainKey(path);
        }
        finally
        {
            if (Directory.Exists(testDirectory))
            {
                Directory.Delete(testDirectory, true);
            }
        }
    }

    private static async Task<string> GivenAFileIn(string directory, string fileName)
    {
        string path = Path.Combine(directory, fileName);
        await File.WriteAllTextAsync(path, "content");
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(-1));
        return path;
    }

    private sealed class RecordingDirectoryScanListener : IDirectoryScanListener
    {
        public List<string> UpdatedFileNames { get; } = [];

        public List<string> DeletedFileNames { get; } = [];

        public ValueTask FilesUpdatedOrAdded(IReadOnlyCollection<FileInfo> updatedFiles, CancellationToken cancellationToken = default)
        {
            foreach (FileInfo file in updatedFiles)
            {
                UpdatedFileNames.Add(file.Name);
            }

            return default;
        }

        public ValueTask FilesDeleted(IReadOnlyCollection<FileInfo> deletedFiles, CancellationToken cancellationToken = default)
        {
            foreach (FileInfo file in deletedFiles)
            {
                DeletedFileNames.Add(file.Name);
            }

            return default;
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
