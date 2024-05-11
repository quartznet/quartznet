#region License
/*
 * All content copyright Marko Lahma, unless otherwise indicated. All rights reserved.
 *
 * Licensed under the Apache License, Version 2.0 (the "License"); you may not
 * use this file except in compliance with the License. You may obtain a copy
 * of the License at
 *
 *   http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS, WITHOUT
 * WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the
 * License for the specific language governing permissions and limitations
 * under the License.
 *
 */
#endregion

using Microsoft.Extensions.Logging;

using Quartz.Diagnostics;

namespace Quartz.Job;

/// <summary>
/// Inspects a file and compares whether its "last modified date" has changed
/// since the last time it was inspected.  If the file has been updated, the
/// job invokes a "call-back" method on an identified
/// <see cref="IFileScanListener" /> that can be found in the
/// <see cref="SchedulerContext" />.
/// </summary>
/// <author>James House</author>
/// <author>Marko Lahma (.NET)</author>
/// <seealso cref="IFileScanListener" />
[DisallowConcurrentExecution]
[PersistJobDataAfterExecution]
public class FileScanJob : IJob
{
    /// <summary>
    /// JobDataMap key with which to specify the name of the file to monitor.
    /// </summary>
    public const string FileName = "FILE_NAME";

    /// <summary>
    /// JobDataMap key with which to specify the <see cref="IFileScanListener" />
    /// to be notified when the file contents change.
    /// </summary>
    public const string FileScanListenerName = "FILE_SCAN_LISTENER_NAME";

    /// <summary>
    /// <see cref="JobDataMap" /> key with which to specify a long
    /// value that represents the minimum number of milliseconds that must have
    /// past since the file's last modified time in order to consider the file
    /// new/altered.  This is necessary because another process may still be
    /// in the middle of writing to the file when the scan occurs, and the
    /// file may therefore not yet be ready for processing.
    ///
    /// <para>If this parameter is not specified, a default value of
    /// 5000 (five seconds) will be used.</para>
    /// </summary>
    public const string MinimumUpdateAge = "MINIMUM_UPDATE_AGE";

    private const string LastModifiedTime = "LAST_MODIFIED_TIME";

    /// <summary>
    /// Gets the log.
    /// </summary>
    /// <value>The log.</value>
    private ILogger<FileScanJob> logger { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="FileScanJob"/> class.
    /// </summary>
    public FileScanJob()
    {
        logger = LogProvider.CreateLogger<FileScanJob>();
    }

    /// <summary>
    /// Called by the <see cref="IScheduler" /> when a <see cref="ITrigger" />
    /// fires that is associated with the <see cref="IJob" />.
    /// <para>
    /// The implementation may wish to set a  result object on the
    /// JobExecutionContext before this method exits.  The result itself
    /// is meaningless to Quartz, but may be informative to
    /// <see cref="IJobListener" />s or
    /// <see cref="ITriggerListener" />s that are watching the job's
    /// execution.
    /// </para>
    /// </summary>
    /// <param name="context">The execution context.</param>
    /// <seealso cref="IJob">
    /// </seealso>
    public virtual async ValueTask Execute(IJobExecutionContext context)
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

        var fileName = mergedJobDataMap.GetString(FileName);
        var listenerName = mergedJobDataMap.GetString(FileScanListenerName);

        if (fileName is null)
        {
            throw new JobExecutionException($"Required parameter '{FileName}' not found in JobDataMap");
        }
        if (listenerName is null)
        {
            throw new JobExecutionException($"Required parameter '{FileScanListenerName}' not found in JobDataMap");
        }

        IFileScanListener? listener = (IFileScanListener?) schedCtxt[listenerName];

        if (listener is null)
        {
            throw new JobExecutionException($"FileScanListener named '{listenerName}' not found in SchedulerContext");
        }

        DateTime lastDate = DateTime.MinValue;
        if (mergedJobDataMap.ContainsKey(LastModifiedTime))
        {
            lastDate = mergedJobDataMap.GetDateTime(LastModifiedTime);
        }

        long minAge = 5000;
        if (mergedJobDataMap.ContainsKey(MinimumUpdateAge))
        {
            minAge = mergedJobDataMap.GetLong(MinimumUpdateAge);
        }

        DateTime maxAgeDate = DateTime.Now.AddMilliseconds(minAge);

        DateTime newDate = GetLastModifiedDate(fileName);

        if (newDate == DateTime.MinValue)
        {
            logger.LogWarning("File '{FileName}' does not exist.", fileName);
            return;
        }

        if (lastDate != DateTime.MinValue && newDate != lastDate && newDate < maxAgeDate)
        {
            // notify call back...
            logger.LogInformation("File '{FileName}' updated, notifying listener.", fileName);
            await listener.FileUpdated(fileName).ConfigureAwait(false);
        }
        else
        {
            logger.LogDebug("File '{FileName}' unchanged.", fileName);
        }

        context.JobDetail.JobDataMap.Put(LastModifiedTime, newDate);
    }

    /// <summary>
    /// Gets the last modified date.
    /// </summary>
    /// <param name="fileName">Name of the file.</param>
    /// <returns></returns>
    protected virtual DateTime GetLastModifiedDate(string fileName)
    {
        FileInfo file = new FileInfo(fileName);

        bool tmpBool;
        if (File.Exists(file.FullName))
        {
            tmpBool = true;
        }
        else
        {
            tmpBool = Directory.Exists(file.FullName);
        }
        if (!tmpBool)
        {
            return DateTime.MinValue;
        }
        return file.LastWriteTime;
    }
}