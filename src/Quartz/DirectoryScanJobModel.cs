using System;
using System.Collections.Generic;
using System.Linq;
using Quartz.Job;

namespace Quartz
{
    /// <summary>
    /// Internal model to hold settings used by <see cref="DirectoryScanJob"/>
    /// </summary>
    internal class DirectoryScanJobModel
    {
        internal IReadOnlyCollection<string> DirectoriesToScan { get; private set; }
        internal IDirectoryScanListener DirectoryScanListener { get; private set; }
        internal DateTime LastModTime { get; private set; }
        internal DateTime MaxAgeDate => DateTime.Now - this.MinUpdateAge;
        private TimeSpan MinUpdateAge { get; set; }
        private JobDataMap JobDetailJobDataMap { get; set; }


        private DirectoryScanJobModel()
        {
        }

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
            };

            return model;
        }


        /// <summary>
        /// Updates the last modified date to the date provided, unless the currently set one is later
        /// </summary>
        /// <param name="lastWriteTimeFromFiles">Latest LastWriteTime of the files scanned</param>
        internal void UpdateLastModifiedDate(DateTime lastWriteTimeFromFiles)
        {
            DateTime newLastModifiedDate = lastWriteTimeFromFiles > this.LastModTime
                ? lastWriteTimeFromFiles
                : this.LastModTime;

            // It is the JobDataMap on the JobDetail which is actually stateful
            this.JobDetailJobDataMap.Put(DirectoryScanJob.LastModifiedTime, newLastModifiedDate);
        }


        private static List<string> GetDirectoriesToScan(SchedulerContext schedCtxt, JobDataMap mergedJobDataMap)
        {
            IDirectoryProvider directoryProvider = new DefaultDirectoryProvider();
            string explicitDirProviderName = mergedJobDataMap.GetString(DirectoryScanJob.DirectoryProviderName);

            if (explicitDirProviderName != null)
            {
                object temp;
                schedCtxt.TryGetValue(explicitDirProviderName, out temp);
                IDirectoryProvider explicitProvider = (IDirectoryProvider)temp;
                if (explicitProvider == null)
                {
                    throw new JobExecutionException("IDirectoryProvider named '" +
                                                    explicitDirProviderName + "' not found in SchedulerContext");
                }
                directoryProvider = explicitProvider;
            }

            return directoryProvider.GetDirectoriesToScan(mergedJobDataMap).ToList();
        }


        private static IDirectoryScanListener GetListener(JobDataMap mergedJobDataMap, SchedulerContext schedCtxt)
        {
            string listenerName = mergedJobDataMap.GetString(DirectoryScanJob.DirectoryScanListenerName);

            if (listenerName == null)
            {
                throw new JobExecutionException("Required parameter '" +
                                                DirectoryScanJob.DirectoryScanListenerName + "' not found in merged JobDataMap");
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