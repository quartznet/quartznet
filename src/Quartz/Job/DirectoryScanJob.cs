using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Quartz.Logging;

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

        private const string LastModifiedTime = "LAST_MODIFIED_TIME";

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
        public void Execute(IJobExecutionContext context)
        {
            DirectoryScanModel model = DirectoryScanModel.GetInstance(context);

            List<FileInfo> updatedFiles = new List<FileInfo>();
            Parallel.ForEach(model.DirectoriesToScan, d =>
            {
                updatedFiles.AddRange(GetUpdatedOrNewFiles(d, model.LastModTime, model.MaxAgeDate));
            });

            if (updatedFiles.Any())
            {
                // notify call back...
                updatedFiles.Select(x => x.DirectoryName)
                    .Distinct().ToList()
                    .ForEach(dir =>
                    {
                        log.Info("Directory '" + dir + "' contents updated, notifying listener.");
                    });
                model.DirectoryScanListener.FilesUpdatedOrAdded(updatedFiles);

                DateTime latestWriteTimeFromFiles = updatedFiles.Select(x => x.LastWriteTime).Max();
                DateTime newLastModifiedDate = latestWriteTimeFromFiles > model.LastModTime
                    ? latestWriteTimeFromFiles
                    : model.LastModTime;
                model.UpdateLastModifiedDate(newLastModifiedDate);
            }
            else if (log.IsDebugEnabled())
            {
                model.DirectoriesToScan.ToList().ForEach(dir =>
                {
                    log.Debug("Directory '" + dir + "' contents unchanged.");
                });
            }
        }

        protected IEnumerable<FileInfo> GetUpdatedOrNewFiles(string dirName, DateTime lastModifiedDate, DateTime maxAgeDate)
        {
            DirectoryInfo dir = new DirectoryInfo(dirName);
            if (!dir.Exists)
            {
                log.Warn("Directory '" + dirName + "' does not exist.");
                return null;
            }

            FileInfo[] files = dir.GetFiles();
            return files.Where(fileInfo => fileInfo.LastWriteTime > lastModifiedDate && fileInfo.LastWriteTime < maxAgeDate);
        }


        /// <summary>
        /// Internal model to hold settings used by <see cref="DirectoryScanJob"/>
        /// </summary>
        private class DirectoryScanModel
        {
            public IReadOnlyCollection<string> DirectoriesToScan { get; private set; }
            public IDirectoryScanListener DirectoryScanListener { get; private set; }
            private TimeSpan MinUpdateAge { get; set; }
            public DateTime LastModTime { get; private set; }
            public DateTime MaxAgeDate => DateTime.Now - this.MinUpdateAge;
            private JobDataMap JobDetailJobDataMap { get; set; }

            private DirectoryScanModel()
            {
            }

            public static DirectoryScanModel GetInstance(IJobExecutionContext context)
            {
                JobDataMap mergedJobDataMap = context.MergedJobDataMap;
                SchedulerContext schedCtxt;
                try
                {
                    schedCtxt = context.Scheduler.Context;
                }
                catch (SchedulerException e)
                {
                    throw new JobExecutionException("Error obtaining scheduler context.", e, false);
                }

                return new DirectoryScanModel
                {
                    DirectoriesToScan = GetDirectoriesToScan(schedCtxt, mergedJobDataMap),
                    DirectoryScanListener = GetListener(mergedJobDataMap, schedCtxt),
                    LastModTime = mergedJobDataMap.ContainsKey(LastModifiedTime)
                        ? mergedJobDataMap.GetDateTime(LastModifiedTime)
                        : DateTime.MinValue,
                    MinUpdateAge = mergedJobDataMap.ContainsKey(MinimumUpdateAge)
                        ? TimeSpan.FromMilliseconds(mergedJobDataMap.GetLong(MinimumUpdateAge))
                        : TimeSpan.FromSeconds(5), // default of 5 seconds
                    JobDetailJobDataMap = context.JobDetail.JobDataMap
                };
            }

            public void UpdateLastModifiedDate(DateTime lastModifiedDate)
            {
                // It is the JobDataMap on the JobDetail which is actually stateful
                this.JobDetailJobDataMap.Put(LastModifiedTime, lastModifiedDate);
            }

            private static List<string> GetDirectoriesToScan(SchedulerContext schedCtxt, JobDataMap mergedJobDataMap)
            {
                List<string> directoriesToScan = new List<string>();
                string dirName = mergedJobDataMap.GetString(DirectoryName);
                string dirNames = mergedJobDataMap.GetString(DirectoryNames);
                string dirProviderName = mergedJobDataMap.GetString(DirectoryProviderName);

                if (dirName == null && dirNames == null && dirProviderName == null)
                {
                    throw new JobExecutionException($"The parameter '{DirectoryName}', '{DirectoryNames}', or '{DirectoryProviderName} " +
                                                    "is required and was not found in merged JobDataMap");
                }

                if (dirName != null)
                {
                    directoriesToScan.Add(dirName);
                }
                else if (dirNames != null)
                {
                    directoriesToScan.AddRange(
                        dirNames.Split(new[] {";"}, StringSplitOptions.RemoveEmptyEntries)
                            .Distinct()); // just in case their are duplicates
                }
                else
                {
                    object temp;
                    schedCtxt.TryGetValue(dirProviderName, out temp);
                    IDirectoryProvider provider = (IDirectoryProvider)temp;
                    if (provider == null)
                    {
                        throw new JobExecutionException("IDirectoryProvider named '" +
                                                    dirProviderName + "' not found in SchedulerContext");
                    }
                    directoriesToScan.AddRange(provider.GetDirectoriesToScan());
                }

                return directoriesToScan;
            }

            private static IDirectoryScanListener GetListener(JobDataMap mergedJobDataMap, SchedulerContext schedCtxt)
            {
                string listenerName = mergedJobDataMap.GetString(DirectoryScanListenerName);

                if (listenerName == null)
                {
                    throw new JobExecutionException("Required parameter '" +
                                                    DirectoryScanListenerName + "' not found in merged JobDataMap");
                }

                object temp;
                schedCtxt.TryGetValue(listenerName, out temp);
                IDirectoryScanListener listener = (IDirectoryScanListener)temp;

                if (listener == null)
                {
                    throw new JobExecutionException("IDirectoryScanListener named '" +
                                                    listenerName + "' not found in SchedulerContext");
                }

                return listener;
            }
        }
    }
}