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
/// The command <see cref="NativeJob" /> runs, and how it runs it.
/// </summary>
/// <remarks>
/// The job is configured through its <see cref="JobDataMap" />, and those keys are the persisted
/// contract — this type is the named way to write and read them.
/// <see cref="JobConfiguratorExtensions.UsingNativeJobOptions{TConfigurator}" /> writes them,
/// <see cref="FromJobData" /> reads them back, and job data written by hand or by an earlier version
/// reads the same either way.
/// </remarks>
public sealed record NativeJobOptions
{
    /// <summary>
    /// The executable to run.
    /// </summary>
    public required string Command { get; init; }

    /// <summary>
    /// The parameters to pass to the command, or <see langword="null" /> for none.
    /// </summary>
    public string? Parameters { get; init; }

    /// <summary>
    /// Whether the job waits for the process to exit before it completes, which is also what makes
    /// the exit code available as <see cref="IJobExecutionContext.Result" />. Defaults to
    /// <see langword="true" />.
    /// </summary>
    public bool WaitForProcess { get; init; } = true;

    /// <summary>
    /// Whether the spawned process's <c>stdout</c> and <c>stderr</c> are read and logged. A process
    /// that writes more output than its pipe holds blocks until someone reads it, so leave this on
    /// for anything chatty. Defaults to <see langword="false" />.
    /// </summary>
    public bool ConsumeStreams { get; init; }

    /// <summary>
    /// The working directory to start the process in, or <see langword="null" /> to inherit the
    /// scheduler's.
    /// </summary>
    public string? WorkingDirectory { get; init; }

    /// <summary>
    /// Reads the options out of a job's data, taking the default for every key it does not carry.
    /// </summary>
    /// <param name="data">
    /// The job data to read, normally <see cref="IJobExecutionContext.MergedJobDataMap" />.
    /// </param>
    /// <exception cref="JobExecutionException">
    /// <see cref="NativeJob.PropertyCommand" /> is absent; there is nothing to run.
    /// </exception>
    public static NativeJobOptions FromJobData(JobDataMap data)
    {
        ArgumentNullException.ThrowIfNull(data);

        string command = data.GetString(NativeJob.PropertyCommand) ?? throw new JobExecutionException("command missing");

        return new NativeJobOptions
        {
            Command = command,
            Parameters = data.GetString(NativeJob.PropertyParameters),
            WaitForProcess = !data.TryGetBoolean(NativeJob.PropertyWaitForProcess, out bool wait) || wait,
            ConsumeStreams = data.TryGetBoolean(NativeJob.PropertyConsumeStreams, out bool consumeStreams) && consumeStreams,
            WorkingDirectory = data.GetString(NativeJob.PropertyWorkingDirectory),
        };
    }

    /// <summary>
    /// Writes the options as the job data keys <see cref="NativeJob" /> reads.
    /// </summary>
    public JobDataMap ToJobData()
    {
        JobDataMap data = new JobDataMap
        {
            [NativeJob.PropertyCommand] = Command,
            [NativeJob.PropertyWaitForProcess] = WaitForProcess,
            [NativeJob.PropertyConsumeStreams] = ConsumeStreams,
        };

        if (Parameters is not null)
        {
            data[NativeJob.PropertyParameters] = Parameters;
        }

        if (WorkingDirectory is not null)
        {
            data[NativeJob.PropertyWorkingDirectory] = WorkingDirectory;
        }

        return data;
    }
}
