using Microsoft.Extensions.Logging;

using Quartz.Diagnostics;

using LogLevel = Microsoft.Extensions.Logging.LogLevel;

namespace Quartz.Jobs;

///<summary>
/// Inspects a directory and compares whether any files' "last modified dates"
/// have changed since the last time it was inspected.  If one or more files
/// have been updated (or created), the job invokes a "call-back" method on an
/// identified <see cref="IDirectoryScanListener"/> that can be resolved from
/// dependency injection or found in the <see cref="SchedulerContext"/>.
/// </summary>
/// <remarks>
/// <para>
/// What is scanned is configured through the job data keys below.
/// <see cref="DirectoryScanOptions" /> names them all, and
/// <see cref="JobConfiguratorExtensions.UsingDirectoryScanOptions{TConfigurator}" /> writes them, so
/// the settings can be given as a value rather than as string keys; the keys stay the persisted form
/// either way.
/// </para>
/// The listener can be provided in two ways:
/// <list type="number">
/// <item>
/// <description>Via dependency injection (recommended): Register the listener implementation
/// in the DI container and specify its type name using <see cref="DirectoryScanListenerName"/>.
/// </description>
/// </item>
/// <item>
/// <description>Via SchedulerContext (legacy): Store the listener instance in the SchedulerContext
/// with a key matching the value specified in <see cref="DirectoryScanListenerName"/>.
/// </description>
/// </item>
/// </list>
/// </remarks>
/// <author>pl47ypus</author>
/// <author>James House</author>
/// <author>Marko Lahma (.NET)</author>
/// <author>Chris Knight (.NET)</author>
[DisallowConcurrentExecution]
[PersistJobDataAfterExecution]
public class DirectoryScanJob : IJob
{
    ///<see cref="JobDataMap"/> key with which to specify the directory to be
    /// monitored - an absolute path is recommended.
    public const string DirectoryName = "DIRECTORY_NAME";

    ///<see cref="JobDataMap"/> key with which to specify the directories to be
    /// monitored. Directory paths should be separated by a semi-colon (;) - absolute paths are recommended.
    public const string DirectoryNames = "DIRECTORY_NAMES";

    /// <see cref="JobDataMap"/> key with which to specify the
    /// <see cref="IDirectoryProvider"/> to be used to provide
    /// the directory paths to be monitored - absolute paths are recommended.
    public const string DirectoryProviderName = "DIRECTORY_PROVIDER_NAME";

    /// <see cref="JobDataMap"/> key with which to specify the
    /// <see cref="IDirectoryScanListener"/> to be
    /// notified when the directory contents change.
    public const string DirectoryScanListenerName = "DIRECTORY_SCAN_LISTENER_NAME";

    /// <see cref="JobDataMap"/> key with which to specify a <see cref="long"/>
    /// value that represents the minimum number of milliseconds that must have
    /// passed since the file's last modified time in order to consider the file
    /// new/altered.  This is necessary because another process may still be
    /// in the middle of writing to the file when the scan occurs, and the
    ///  file may therefore not yet be ready for processing.
    /// <para>If this parameter is not specified, a default value of 5000 (five seconds) will be used.</para>
    /// <para><see cref="DirectoryScanOptions.MinimumUpdateAge" /> says the same thing as a <see cref="TimeSpan" />.</para>
    public const string MinimumUpdateAge = "MINIMUM_UPDATE_AGE";

    internal const string LastModifiedTime = "LAST_MODIFIED_TIME";

    /// <summary>
    /// <see cref="JobDataMap"/> key with which to specify the search string to match against the
    /// names of files. Can contain a combination of valid literal path and wildcard (* and ?)
    /// characters. Defaults to <c>*</c>, every file.
    /// </summary>
    public const string SearchPattern = "SEARCH_PATTERN";

    ///<see cref="JobDataMap"/> Key to specify whether to scan sub directories for file changes.
    ///<para>Defaults to <see langword="false" />.</para>
    public const string IncludeSubDirectories = "INCLUDE_SUB_DIRECTORIES";

    ///<see cref="JobDataMap"/> key to store the current file list of the scanned directories.
    ///This is required to find out deleted files during next iteration.
    internal const string CurrentFileList = "CURRENT_FILE_LIST";

    private readonly ILogger<DirectoryScanJob> logger;
    private readonly IServiceProvider? serviceProvider;
    private readonly TimeProvider timeProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="DirectoryScanJob" /> class.
    /// </summary>
    /// <param name="timeProvider">
    /// The clock deciding how recently a file must have been written to still count as being written
    /// to. <see langword="null" /> takes <see cref="TimeProvider.System" />.
    /// </param>
    public DirectoryScanJob(TimeProvider? timeProvider = null)
    {
        this.timeProvider = timeProvider ?? TimeProvider.System;
        logger = LogProvider.CreateLogger<DirectoryScanJob>();
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="DirectoryScanJob" /> class that resolves its
    /// <see cref="IDirectoryScanListener" /> from the container.
    /// </summary>
    /// <param name="serviceProvider">The container the listener is registered in.</param>
    /// <param name="timeProvider">
    /// The clock deciding how recently a file must have been written to still count as being written
    /// to. <see langword="null" /> takes <see cref="TimeProvider.System" />.
    /// </param>
    public DirectoryScanJob(IServiceProvider serviceProvider, TimeProvider? timeProvider = null)
    {
        this.serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        this.timeProvider = timeProvider ?? TimeProvider.System;
        logger = LogProvider.CreateLogger<DirectoryScanJob>();
    }

    /// <summary>
    /// This is the main entry point for job execution. The scheduler will call this method on the
    /// job once it is triggered.
    /// </summary>
    /// <param name="context">The <see cref="IJobExecutionContext"/> that
    /// the job will use during execution.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    public async ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
    {
        DirectoryScanJobModel model = DirectoryScanJobModel.GetInstance(context, serviceProvider, timeProvider);

        List<FileInfo> allFiles = new List<FileInfo>();
        List<FileInfo> updatedFiles = new List<FileInfo>();
        List<FileInfo> deletedFiles = new List<FileInfo>();
        Parallel.ForEach(model.DirectoriesToScan, d =>
        {
            DirectoryScanResult scanned = GetUpdatedOrNewFiles(
                d,
                model.LastModifiedTime,
                model.MaxAgeTime,
                model.CurrentFileList,
                model.SearchPattern,
                model.IncludeSubDirectories);

            AddToList(updatedFiles, scanned.Updated);
            AddToList(deletedFiles, scanned.Deleted);
            AddToList(allFiles, scanned.All);
        });

        if (updatedFiles.Count > 0 || deletedFiles.Count > 0)
        {
            foreach (var fileInfo in updatedFiles)
            {
                logger.DirectoryContentsUpdated(fileInfo.DirectoryName);
            }

            // notify call back...
            if (updatedFiles.Count > 0)
            {
                await model.DirectoryScanListener.FilesUpdatedOrAdded(updatedFiles, cancellationToken).ConfigureAwait(false);
                DateTimeOffset latestWriteTimeFromFiles = updatedFiles.Select(LastWriteTime).Max();
                model.UpdateLastModifiedTime(latestWriteTimeFromFiles);
            }
            if (deletedFiles.Count > 0)
            {
                await model.DirectoryScanListener.FilesDeleted(deletedFiles, cancellationToken).ConfigureAwait(false);
            }

            //Update current file list
            model.UpdateFileList(allFiles);
        }
        else if (logger.IsEnabled(LogLevel.Debug))
        {
            foreach (var dir in model.DirectoriesToScan)
            {
                logger.DirectoryContentsUnchanged(dir);
            }
        }
    }

    /// <summary>
    /// Scans one directory and reports what it found.
    /// </summary>
    /// <param name="directoryName">The directory to scan.</param>
    /// <param name="lastModifiedTime">
    /// The write time the previous scan reported; a file written no later than this is unchanged.
    /// </param>
    /// <param name="maxAgeTime">
    /// The write time a file must be older than to count as settled, which keeps a file still being
    /// written from being reported half-finished.
    /// </param>
    /// <param name="currentFileList">What the previous scan saw, which is what a deletion is measured against.</param>
    /// <param name="searchPattern">The pattern file names must match.</param>
    /// <param name="includeSubDirectories">Whether to descend into sub-directories.</param>
    private DirectoryScanResult GetUpdatedOrNewFiles(
        string directoryName,
        DateTimeOffset lastModifiedTime,
        DateTimeOffset maxAgeTime,
        IReadOnlyCollection<FileInfo> currentFileList,
        string searchPattern = "*",
        bool includeSubDirectories = false)
    {
        DirectoryInfo dir = new DirectoryInfo(directoryName);
        if (!dir.Exists)
        {
            logger.DirectoryDoesNotExist(directoryName);
            return new DirectoryScanResult([], [], []);
        }

        SearchOption searchOption = includeSubDirectories ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        FileInfo[] files = dir.GetFiles(searchPattern, searchOption);

        List<FileInfo> updatedFiles = files
            .Where(fileInfo => LastWriteTime(fileInfo) > lastModifiedTime && LastWriteTime(fileInfo) < maxAgeTime)
            .ToList();
        List<FileInfo> allFiles = files.ToList();
        List<FileInfo> deletedFiles = currentFileList.Except(allFiles, new FileInfoComparer()).ToList();

        return new DirectoryScanResult(allFiles, updatedFiles, deletedFiles);
    }

    /// <summary>
    /// The file's last write time as an instant. <c>FileInfo.LastWriteTimeUtc</c> is what the file
    /// system recorded; the local-time reading of it is the same instant said differently.
    /// </summary>
    private static DateTimeOffset LastWriteTime(FileInfo file) => new(file.LastWriteTimeUtc, TimeSpan.Zero);

    private static void AddToList(List<FileInfo> fileList, IReadOnlyList<FileInfo> updatedFileList)
    {
        lock (fileList)
        {
            foreach (var fileInfo in updatedFileList)
            {
                fileList.Add(fileInfo);
            }
        }
    }

    private sealed class FileInfoComparer : IEqualityComparer<FileInfo>
    {
        public bool Equals(FileInfo? x, FileInfo? y)
        {
            if (x is null || y is null)
            {
                return false;
            }

            return x.FullName == y.FullName;
        }

        public int GetHashCode(FileInfo obj)
        {
            return obj.FullName.GetHashCode();
        }
    }
}