using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

using Quartz.Logging;
using Quartz.Spi;
using Renci.SshNet;
using Renci.SshNet.Sftp;
using LogLevel = Microsoft.Extensions.Logging.LogLevel;

namespace Quartz.Job;

///<summary>
/// Inspects a directory and compares whether any files' "last modified dates"
/// have changed since the last time it was inspected.  If one or more files
/// have been updated (or created), the job invokes a "call-back" method on an
/// identified <see cref="ISFTPDirectoryScanListener"/> that can be found in the
/// <see cref="SchedulerContext"/>.
/// </summary>
/// <author>Monty</author>
[DisallowConcurrentExecution]
[PersistJobDataAfterExecution]
public class SFTPDirectoryScanJob : BaseDirectoryScanJob, IJob
{
    /// <summary>
    /// SSH.NET don't allow filter by the clasic *.*, *.zip, etc (i don't know how it's called)
    /// So this search patern it will be Regex, and AFTER to get the full list of elements from the server.
    /// </summary>
    internal const string SearchPattern = "SEARCH_PATTERN";

    ///<see cref="JobDataMap"/> IP or URL of the host.
    internal const string HostServer = "HOST";

    ///<see cref="JobDataMap"/> User to connect to the host.
    internal const string User = "USER";

    ///<see cref="JobDataMap"/> Password to connect to the host.
    internal const string Password = "PASSWORD";

    private readonly ILogger<SFTPDirectoryScanJob> logger;

    public SFTPDirectoryScanJob()
    {
        logger = LogProvider.CreateLogger<SFTPDirectoryScanJob>();
    }

    /// <summary>
    /// This is the main entry point for job execution. The scheduler will call this method on the
    /// job once it is triggered.
    /// </summary>
    /// <param name="context">The <see cref="IJobExecutionContext"/> that
    /// the job will use during execution.</param>
    public ValueTask Execute(IJobExecutionContext context)
    {
        SFTPDirectoryScanJobModel model = SFTPDirectoryScanJobModel.GetInstance(context);

        List<SftpFile> allFiles = new List<SftpFile>();
        List<SftpFile> updatedFiles = new List<SftpFile>();
        List<SftpFile> deletedFiles = new List<SftpFile>();
        using (model.SFTPSource)
        {
            model.SFTPSource?.Connect();
            Parallel.ForEach(model.DirectoriesToScan, d =>
            {
                List<SftpFile> dirAllFiles;
                List<SftpFile> dirNewOrUpdatedFiles;
                List<SftpFile> dirDeletedFiles;

                GetUpdatedOrNewFiles(d, model.LastModTime, model.MaxAgeDate, model.CurrentFileList,
                    out dirAllFiles, out dirNewOrUpdatedFiles, out dirDeletedFiles, model.SFTPSource!, model.SearchPattern);

                AddToList(updatedFiles, dirNewOrUpdatedFiles);
                AddToList(deletedFiles, dirDeletedFiles);
                AddToList(allFiles, dirAllFiles);
            });
        }

        if (updatedFiles.Any() || deletedFiles.Any())
        {
            foreach (var fileInfo in updatedFiles)
            {
                logger.LogInformation("Directory {DirectoryName} contents updated, notifying listener.", Path.GetDirectoryName(fileInfo.FullName));
            }

            // notify call back...
            if (updatedFiles.Any())
            {
                model.SFTPDirectoryScanListener.FilesUpdatedOrAdded(updatedFiles);
                DateTime latestWriteTimeFromFiles = updatedFiles.Select(x => x.LastWriteTime).Max();
                model.UpdateLastModifiedDate(latestWriteTimeFromFiles);
            }
            if (deletedFiles.Any())
            {
                model.SFTPDirectoryScanListener.FilesDeleted(deletedFiles);
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

   

    protected void GetUpdatedOrNewFiles(string dirName, DateTime lastModifiedDate, DateTime maxAgeDate, List<SftpFile> currentFileList,
        out List<SftpFile> allFiles, out List<SftpFile> updatedFiles, out List<SftpFile> deletedFiles, SftpClient sftpSource, string searchPattern)
    {
        updatedFiles = new List<SftpFile>();
        deletedFiles = new List<SftpFile>();
        allFiles = new List<SftpFile>();
        
        List<SftpFile> files = GetFileList(dirName, sftpSource, searchPattern);
        updatedFiles = files
            .Where(fileInfo => fileInfo.LastWriteTime > lastModifiedDate && fileInfo.LastWriteTime < maxAgeDate)
            .ToList();
        allFiles = files;
        deletedFiles = currentFileList.Except(allFiles, new FileInfoComparer()).ToList();
    }

    private List<SftpFile> GetFileList(string path, SftpClient sftpSource, string searchPattern)
    {

        if (!sftpSource.Exists(path))
        {
            logger.LogWarning("Directory '{DirectoryName}' does not exist.", path);
            return new List<SftpFile>();
        }
        return sftpSource.ListDirectory(path).Where(x => x.IsRegularFile && Regex.IsMatch(x.Name, searchPattern, RegexOptions.None, TimeSpan.FromSeconds(1))).ToList();
    }


    private sealed class FileInfoComparer : IEqualityComparer<SftpFile>
    {
        public bool Equals(SftpFile? x, SftpFile? y)
        {
            if (x is null || y is null)
            {
                return false;
            }

            return x.FullName.Equals(y.FullName);
        }

        public int GetHashCode(SftpFile obj)
        {
            return obj.FullName.GetHashCode();
        }
    }
}