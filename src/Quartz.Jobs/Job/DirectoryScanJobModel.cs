using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Quartz.Simpl;
using Quartz.Spi;

namespace Quartz.Job
{
    /// <summary>
    /// Internal model to hold settings used by <see cref="DirectoryScanJob"/>
    /// </summary>
    internal class DirectoryScanJobModel
    {
        /// <summary>
        /// We only want this type of object to be instantiated by inspecting the data 
        /// of a IJobExecutionContext <see cref="IJobExecutionContext"/>. Use the 
        /// GetInstance() <see cref="GetInstance"/> method to create an instance of this
        /// object type
        /// </summary>
        private DirectoryScanJobModel()
        {
        }

        internal List<string> DirectoriesToScan { get; private set; } = null!;
        internal List<FileInfo> CurrentFileList { get; private set; } = null!;
        internal IDirectoryScanListener DirectoryScanListener { get; private set; } = null!;
        internal DateTime LastModTime { get; private set; }
        internal DateTime MaxAgeDate => DateTime.Now - MinUpdateAge;
        private TimeSpan MinUpdateAge { get; set; }
        private JobDataMap JobDetailJobDataMap { get; set; } = null!;
        public string SearchPattern { get; internal set; } = null!;
        public bool IncludeSubDirectories { get; internal set; }

        /// <summary>
        /// Creates an instance of DirectoryScanJobModel by inspecting the provided IJobExecutionContext <see cref="IJobExecutionContext"/>
        /// </summary>
        /// <param name="context">Content of the job execution <see cref="IJobExecutionContext"/></param>
        /// <returns>Instance of DirectoryScanJobModel based on the IJobExecutionContext <see cref="IJobExecutionContext"/> passed in</returns>
        internal static DirectoryScanJobModel GetInstance(IJobExecutionContext context)
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

            var model = new DirectoryScanJobModel
            {
                DirectoryScanListener = GetListener(mergedJobDataMap, schedCtxt),
                LastModTime = mergedJobDataMap.ContainsKey(DirectoryScanJob.LastModifiedTime)
                    ? mergedJobDataMap.GetDateTime(DirectoryScanJob.LastModifiedTime)
                    : DateTime.MinValue,
                MinUpdateAge = mergedJobDataMap.ContainsKey(DirectoryScanJob.MinimumUpdateAge)
                    ? TimeSpan.FromMilliseconds(mergedJobDataMap.GetLong(DirectoryScanJob.MinimumUpdateAge))
                    : TimeSpan.FromSeconds(5), // default of 5 seconds
                JobDetailJobDataMap = context.JobDetail.JobDataMap,
                DirectoriesToScan = GetDirectoriesToScan(schedCtxt, mergedJobDataMap)
                    .Distinct().ToList(),
                CurrentFileList = mergedJobDataMap.ContainsKey(DirectoryScanJob.CurrentFileList) ?
                    (List<FileInfo>)mergedJobDataMap.Get(DirectoryScanJob.CurrentFileList)
                    : new List<FileInfo>(),
                SearchPattern = mergedJobDataMap.ContainsKey(DirectoryScanJob.SearchPattern) ?
                    mergedJobDataMap.GetString(DirectoryScanJob.SearchPattern)! : "*",
                IncludeSubDirectories = mergedJobDataMap.ContainsKey(DirectoryScanJob.IncludeSubDirectories) 
                                        && mergedJobDataMap.GetBooleanValue(DirectoryScanJob.IncludeSubDirectories)
            };

            return model;
        }


        /// <summary>
        /// Updates the last modified date to the date provided, unless the currently set one is later
        /// </summary>
        /// <param name="lastWriteTimeFromFiles">Latest LastWriteTime of the files scanned</param>
        internal void UpdateLastModifiedDate(DateTime lastWriteTimeFromFiles)
        {
            DateTime newLastModifiedDate = lastWriteTimeFromFiles > LastModTime
                ? lastWriteTimeFromFiles
                : LastModTime;

            // It is the JobDataMap on the JobDetail which is actually stateful
            JobDetailJobDataMap.Put(DirectoryScanJob.LastModifiedTime, newLastModifiedDate);
        }

        /// <summary>
        /// Updates the file list for comparison in next iteration
        /// </summary>
        /// <param name="fileList"></param>
        internal void UpdateFileList(List<FileInfo> fileList)
        {
            JobDetailJobDataMap.Put(DirectoryScanJob.CurrentFileList, fileList);
        }


        private static List<string> GetDirectoriesToScan(SchedulerContext schedCtxt, JobDataMap mergedJobDataMap)
        {
            IDirectoryProvider directoryProvider = new DefaultDirectoryProvider();
            var explicitDirProviderName = mergedJobDataMap.GetString(DirectoryScanJob.DirectoryProviderName);

            if (explicitDirProviderName != null)
            {
                schedCtxt.TryGetValue(explicitDirProviderName, out var temp);
                IDirectoryProvider explicitProvider = (IDirectoryProvider)temp;
                directoryProvider = explicitProvider ?? throw new JobExecutionException("IDirectoryProvider named '" +
                                                    explicitDirProviderName + "' not found in SchedulerContext");
            }

            return directoryProvider.GetDirectoriesToScan(mergedJobDataMap).ToList();
        }


        private static IDirectoryScanListener GetListener(JobDataMap mergedJobDataMap, SchedulerContext schedCtxt)
        {
            var listenerName = mergedJobDataMap.GetString(DirectoryScanJob.DirectoryScanListenerName);

            if (listenerName == null)
            {
                throw new JobExecutionException("Required parameter '" +
                                                DirectoryScanJob.DirectoryScanListenerName + "' not found in merged JobDataMap");
            }

            schedCtxt.TryGetValue(listenerName, out var temp);
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