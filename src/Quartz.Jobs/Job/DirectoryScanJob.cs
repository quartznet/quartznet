using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

using Quartz.Logging;
using Quartz.Spi;

namespace Quartz.Job
{
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

        private readonly ILog log;

        public DirectoryScanJob()
        {
            log = LogProvider.GetLogger(GetType());
        }

        /// <summary>
        /// This is the main entry point for job execution. The scheduler will call this method on the
        /// job once it is triggered.
        /// </summary>
        /// <param name="context">The <see cref="IJobExecutionContext"/> that
        /// the job will use during execution.</param>
        public Task Execute(IJobExecutionContext context)
        {
            DirectoryScanJobModel model = DirectoryScanJobModel.GetInstance(context);

            List<FileInfo> updatedFiles = new List<FileInfo>();
            Parallel.ForEach(model.DirectoriesToScan, d =>
            {
                var newOrUpdatedFiles = GetUpdatedOrNewFiles(d, model.LastModTime, model.MaxAgeDate);
                lock (updatedFiles)
                {
                    foreach (var fileInfo in newOrUpdatedFiles)
                    {
                        updatedFiles.Add(fileInfo);
                    }
                }
            });

            if (updatedFiles.Any())
            {
                foreach (var fileInfo in updatedFiles)
                {
                    log.Info($"Directory '{fileInfo.DirectoryName}' contents updated, notifying listener.");
                }

                // notify call back...
                model.DirectoryScanListener.FilesUpdatedOrAdded(updatedFiles);

                DateTime latestWriteTimeFromFiles = updatedFiles.Select(x => x.LastWriteTime).Max();
                model.UpdateLastModifiedDate(latestWriteTimeFromFiles);
            }
            else if (log.IsDebugEnabled())
            {
                foreach (var dir in model.DirectoriesToScan)
                {
                    log.Debug($"Directory '{dir}' contents unchanged.");
                }
            }
            return TaskUtil.CompletedTask;
        }

        protected List<FileInfo> GetUpdatedOrNewFiles(string dirName, DateTime lastModifiedDate, DateTime maxAgeDate)
        {
            DirectoryInfo dir = new DirectoryInfo(dirName);
            if (!dir.Exists)
            {
                log.Warn($"Directory '{dirName}' does not exist.");
                return new List<FileInfo>();
            }

            FileInfo[] files = dir.GetFiles();
            return files
                .Where(fileInfo => fileInfo.LastWriteTime > lastModifiedDate && fileInfo.LastWriteTime < maxAgeDate)
                .ToList();
        }
    }
}