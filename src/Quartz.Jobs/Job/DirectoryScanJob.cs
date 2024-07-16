using Microsoft.Extensions.Logging;

using Quartz.Diagnostics;
using Quartz.Spi;

using LogLevel = Microsoft.Extensions.Logging.LogLevel;

namespace Quartz.Job;

///<summary>
/// Inspects a directory and compares whether any files' "last modified dates"
/// have changed since the last time it was inspected.  If one or more files
/// have been updated (or created), the job invokes a "call-back" method on an
/// identified <see cref="IDirectoryScanListener"/> that can be found in the
/// <see cref="SchedulerContext"/>.
/// </summary>
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
    public const string MinimumUpdateAge = "MINIMUM_UPDATE_AGE";

    internal const string LastModifiedTime = "LAST_MODIFIED_TIME";

    /// <summary>
    /// The search string to match against the names of files.
    /// Can contain combination of valid literal path and wildcard (* and ?) characters
    /// </summary>
    internal const string SearchPattern = "SEARCH_PATTERN";

    ///<see cref="JobDataMap"/> Key to specify whether to scan sub directories for file changes.
    internal const string IncludeSubDirectories = "INCLUDE_SUB_DIRECTORIES";

    ///<see cref="JobDataMap"/> key to store the current file list of the scanned directories.
    ///This is required to find out deleted files during next iteration.
    internal const string CurrentFileList = "CURRENT_FILE_LIST";

    private readonly ILogger<DirectoryScanJob> logger;

    public DirectoryScanJob()
    {
        logger = LogProvider.CreateLogger<DirectoryScanJob>();
    }

    /// <summary>
    /// This is the main entry point for job execution. The scheduler will call this method on the
    /// job once it is triggered.
    /// </summary>
    /// <param name="context">The <see cref="IJobExecutionContext"/> that
    /// the job will use during execution.</param>
    public ValueTask Execute(IJobExecutionContext context)
    {
        DirectoryScanJobModel model = DirectoryScanJobModel.GetInstance(context);

        List<FileInfo> allFiles = new List<FileInfo>();
        List<FileInfo> updatedFiles = new List<FileInfo>();
        List<FileInfo> deletedFiles = new List<FileInfo>();
        Parallel.ForEach(model.DirectoriesToScan, d =>
        {
            List<FileInfo> dirAllFiles;
            List<FileInfo> dirNewOrUpdatedFiles;
            List<FileInfo> dirDeletedFiles;

            GetUpdatedOrNewFiles(d, model.LastModTime, model.MaxAgeDate, model.CurrentFileList,
                out dirAllFiles, out dirNewOrUpdatedFiles, out dirDeletedFiles, model.SearchPattern, model.IncludeSubDirectories);

            AddToList(updatedFiles, dirNewOrUpdatedFiles);
            AddToList(deletedFiles, dirDeletedFiles);
            AddToList(allFiles, dirAllFiles);
        });

        if (updatedFiles.Count > 0 || deletedFiles.Count > 0)
        {
            foreach (var fileInfo in updatedFiles)
            {
                logger.LogInformation("Directory {DirectoryName} contents updated, notifying listener.", fileInfo.DirectoryName);
            }

            // notify call back...
            if (updatedFiles.Count > 0)
            {
                model.DirectoryScanListener.FilesUpdatedOrAdded(updatedFiles);
                DateTime latestWriteTimeFromFiles = updatedFiles.Select(x => x.LastWriteTime).Max();
                model.UpdateLastModifiedDate(latestWriteTimeFromFiles);
            }
            if (deletedFiles.Count > 0)
            {
                model.DirectoryScanListener.FilesDeleted(deletedFiles);
            }

            //Update current file list
            model.UpdateFileList(allFiles);
        }
        else if (logger.IsEnabled(LogLevel.Debug))
        {
            foreach (var dir in model.DirectoriesToScan)
            {
                logger.LogDebug("Directory '{Directory}' contents unchanged.", dir);
            }
        }
        return default;
    }

    protected void GetUpdatedOrNewFiles(string dirName, DateTime lastModifiedDate, DateTime maxAgeDate, List<FileInfo> currentFileList,
        out List<FileInfo> allFiles, out List<FileInfo> updatedFiles, out List<FileInfo> deletedFiles, string searchPattern = "*", bool includeSubDirectories = false)
    {
        updatedFiles = new List<FileInfo>();
        deletedFiles = new List<FileInfo>();
        allFiles = new List<FileInfo>();

        DirectoryInfo dir = new DirectoryInfo(dirName);
        if (!dir.Exists)
        {
            logger.LogWarning("Directory '{DirectoryName}' does not exist.", dirName);
            return;
        }

        SearchOption searchOption = includeSubDirectories ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        FileInfo[] files = dir.GetFiles(searchPattern, searchOption);
        updatedFiles = files
            .Where(fileInfo => fileInfo.LastWriteTime > lastModifiedDate && fileInfo.LastWriteTime < maxAgeDate)
            .ToList();
        allFiles = files.ToList();
        deletedFiles = currentFileList.Except(allFiles, new FileInfoComparer()).ToList();
    }

    private static void AddToList(List<FileInfo> fileList, List<FileInfo> updatedFileList)
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