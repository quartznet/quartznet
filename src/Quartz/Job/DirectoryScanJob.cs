using System;
using System.Collections.Generic;
using System.IO;

using Common.Logging;

namespace Quartz.Job
{
/**
 * Inspects a directory and compares whether any files' "last modified dates" 
 * have changed since the last time it was inspected.  If one or more files 
 * have been updated (or created), the job invokes a "call-back" method on an 
 * identified <code>DirectoryScanListener</code> that can be found in the 
 * <code>SchedulerContext</code>.
 * 
 * @author pl47ypus
 * @author jhouse
 * @see org.quartz.jobs.DirectoryScanListener
 * @see org.quartz.SchedulerContext
 */

    [DisallowConcurrentExecution]
    [PersistJobDataAfterExecution]
    public class DirectoryScanJob : IJob
    {
        /**
     * <code>JobDataMap</code> key with which to specify the directory to be 
     * monitored - an absolute path is recommended. 
     */
        public const string DIRECTORY_NAME = "DIRECTORY_NAME";

        /**
     * <code>JobDataMap</code> key with which to specify the 
     * {@link org.quartz.jobs.DirectoryScanListener} to be 
     * notified when the directory contents change.  
     */
        public const string DIRECTORY_SCAN_LISTENER_NAME = "DIRECTORY_SCAN_LISTENER_NAME";

        /**
     * <code>JobDataMap</code> key with which to specify a <code>long</code>
     * value that represents the minimum number of milliseconds that must have
     * past since the file's last modified time in order to consider the file
     * new/altered.  This is necessary because another process may still be
     * in the middle of writing to the file when the scan occurs, and the
     * file may therefore not yet be ready for processing.
     * 
     * <p>If this parameter is not specified, a default value of 
     * <code>5000</code> (five seconds) will be used.</p>
     */
        public const string MINIMUM_UPDATE_AGE = "MINIMUM_UPDATE_AGE";

        private const string LAST_MODIFIED_TIME = "LAST_MODIFIED_TIME";

        private readonly ILog log;

        public DirectoryScanJob()
        {
            log = LogManager.GetLogger(GetType());
        }

        /** 
     * @see org.quartz.Job#execute(org.quartz.JobExecutionContext)
     */

        public void Execute(IJobExecutionContext context)
        {
            JobDataMap mergedJobDataMap = context.MergedJobDataMap;
            SchedulerContext schedCtxt = null;
            try
            {
                schedCtxt = context.Scheduler.Context;
            }
            catch (SchedulerException e)
            {
                throw new JobExecutionException("Error obtaining scheduler context.", e, false);
            }

            string dirName = mergedJobDataMap.GetString(DIRECTORY_NAME);
            string listenerName = mergedJobDataMap.GetString(DIRECTORY_SCAN_LISTENER_NAME);

            if (dirName == null)
            {
                throw new JobExecutionException("Required parameter '" +
                                                DIRECTORY_NAME + "' not found in merged JobDataMap");
            }
            if (listenerName == null)
            {
                throw new JobExecutionException("Required parameter '" +
                                                DIRECTORY_SCAN_LISTENER_NAME + "' not found in merged JobDataMap");
            }

            object temp;
            schedCtxt.TryGetValue(listenerName, out temp);
            IDirectoryScanListener listener = (IDirectoryScanListener) temp;

            if (listener == null)
            {
                throw new JobExecutionException("DirectoryScanListener named '" +
                                                listenerName + "' not found in SchedulerContext");
            }

            DateTime lastDate = DateTime.MinValue;
            if (mergedJobDataMap.ContainsKey(LAST_MODIFIED_TIME))
            {
                lastDate = mergedJobDataMap.GetDateTime(LAST_MODIFIED_TIME);
            }

            long minAge = 5000;
            if (mergedJobDataMap.ContainsKey(MINIMUM_UPDATE_AGE))
            {
                minAge = mergedJobDataMap.GetLong(MINIMUM_UPDATE_AGE);
            }
            DateTime maxAgeDate = DateTime.Now.AddMilliseconds(minAge);

            FileInfo[] updatedFiles = GetUpdatedOrNewFiles(dirName, lastDate, maxAgeDate);

            if (updatedFiles == null)
            {
                log.Warn("Directory '" + dirName + "' does not exist.");
                return;
            }

            DateTime latestMod = DateTime.MinValue;
            foreach (FileInfo updFile in updatedFiles)
            {
                DateTime lm = updFile.LastWriteTime;
                latestMod = (lm > latestMod) ? lm : latestMod;
            }

            if (updatedFiles.Length > 0)
            {
                // notify call back...
                log.Info("Directory '" + dirName + "' contents updated, notifying listener.");
                listener.FilesUpdatedOrAdded(updatedFiles);
            }
            else if (log.IsDebugEnabled)
            {
                log.Debug("Directory '" + dirName + "' contents unchanged.");
            }

            // It is the JobDataMap on the JobDetail which is actually stateful
            context.JobDetail.JobDataMap.Put(LAST_MODIFIED_TIME, latestMod);
        }

        protected FileInfo[] GetUpdatedOrNewFiles(string dirName, DateTime lastDate, DateTime maxAgeDate)
        {
            DirectoryInfo dir = new DirectoryInfo(dirName);
            if (!dir.Exists)
            {
                return null;
            }

            FileInfo[] files = dir.GetFiles();
            List<FileInfo> acceptedFiles = new List<FileInfo>();

            foreach (FileInfo fileInfo in files)
            {
                if (fileInfo.LastWriteTime > lastDate && fileInfo.LastWriteTime < maxAgeDate)
                {
                    acceptedFiles.Add(fileInfo);
                }
            }

            return acceptedFiles.ToArray();
        }
    }
}