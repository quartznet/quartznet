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

namespace Quartz.Jobs;

/// <summary>
/// What <see cref="FileScanJob" /> watches and when it considers the file settled.
/// </summary>
/// <remarks>
/// The job is configured through its <see cref="JobDataMap" />, and those keys are the persisted
/// contract — this type is the named way to write and read them.
/// <see cref="JobConfiguratorExtensions.UsingFileScanOptions{TConfigurator}" /> writes them,
/// <see cref="FromJobData" /> reads them back, and job data written by hand or by an earlier version
/// reads the same either way.
/// </remarks>
public sealed record FileScanOptions
{
    /// <summary>
    /// The file to monitor; an absolute path is recommended.
    /// </summary>
    public required string FileName { get; init; }

    /// <summary>
    /// The <see cref="SchedulerContext" /> key an <see cref="IFileScanListener" /> is stored under.
    /// </summary>
    public required string ScanListenerName { get; init; }

    /// <summary>
    /// How long the file must have been left alone to count as altered rather than as one another
    /// process is still writing. Defaults to five seconds.
    /// </summary>
    /// <remarks>
    /// Persisted as a whole number of milliseconds under <see cref="FileScanJob.MinimumUpdateAge" />,
    /// which is what earlier versions wrote.
    /// </remarks>
    public TimeSpan MinimumUpdateAge { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Reads the options out of a job's data, taking the default for every key it does not carry.
    /// </summary>
    /// <param name="data">
    /// The job data to read, normally <see cref="IJobExecutionContext.MergedJobDataMap" />.
    /// </param>
    /// <exception cref="JobExecutionException">
    /// <see cref="FileScanJob.FileName" /> or <see cref="FileScanJob.FileScanListenerName" /> is
    /// absent; the job has nothing to watch, or nothing to tell.
    /// </exception>
    public static FileScanOptions FromJobData(JobDataMap data)
    {
        ArgumentNullException.ThrowIfNull(data);

        string? fileName = data.GetString(FileScanJob.FileName);
        if (fileName is null)
        {
            throw new JobExecutionException($"Required parameter '{FileScanJob.FileName}' not found in JobDataMap");
        }

        string? listenerName = data.GetString(FileScanJob.FileScanListenerName);
        if (listenerName is null)
        {
            throw new JobExecutionException($"Required parameter '{FileScanJob.FileScanListenerName}' not found in JobDataMap");
        }

        return new FileScanOptions
        {
            FileName = fileName,
            ScanListenerName = listenerName,
            MinimumUpdateAge = JobDataMapDurations.ReadMilliseconds(data, FileScanJob.MinimumUpdateAge) ?? TimeSpan.FromSeconds(5),
        };
    }

    /// <summary>
    /// Writes the options as the job data keys <see cref="FileScanJob" /> reads.
    /// </summary>
    public JobDataMap ToJobData()
    {
        return new JobDataMap
        {
            [FileScanJob.FileName] = FileName,
            [FileScanJob.FileScanListenerName] = ScanListenerName,
            [FileScanJob.MinimumUpdateAge] = (long) MinimumUpdateAge.TotalMilliseconds,
        };
    }
}
