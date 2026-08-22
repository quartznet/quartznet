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
/// Configures a job from <c>Quartz.Jobs</c> with named options rather than with its job data keys.
/// </summary>
/// <remarks>
/// <para>
/// Each of these writes the same <see cref="JobDataMap" /> entries the job has always read, so the
/// stored job is identical to one configured key by key and reads back the same on any version. What
/// changes is the writing: the key cannot be misspelled, the value cannot be of the wrong type, and
/// a setting the job supports cannot go missing from the documentation — the options type lists them.
/// </para>
/// <para>
/// They are generic in the configurator so that the call keeps its receiver's type: a
/// <see cref="JobBuilder{TJob}" /> chain still ends in <c>Build()</c>, and the
/// <see cref="IJobConfigurator{TJob}" /> handed to <c>AddJob</c> still chains its own members.
/// </para>
/// </remarks>
public static class JobConfiguratorExtensions
{
    /// <summary>
    /// Configures a <see cref="DirectoryScanJob" /> with the directories to scan, the pattern to
    /// match, whether to recurse, and how long a file must have settled.
    /// </summary>
    /// <example>
    /// <code>
    /// IJobDetail job = JobBuilder.Create&lt;DirectoryScanJob&gt;()
    ///     .WithIdentity("inboxScan")
    ///     .UsingDirectoryScanOptions(new DirectoryScanOptions
    ///     {
    ///         Directories = ["/var/spool/inbox"],
    ///         SearchPattern = "*.csv",
    ///         IncludeSubDirectories = true,
    ///         ScanListenerName = nameof(InboxListener),
    ///     })
    ///     .Build();
    /// </code>
    /// </example>
    public static TConfigurator UsingDirectoryScanOptions<TConfigurator>(this TConfigurator configurator, DirectoryScanOptions options)
        where TConfigurator : class, IJobConfigurator<DirectoryScanJob>
    {
        ArgumentNullException.ThrowIfNull(configurator);
        ArgumentNullException.ThrowIfNull(options);

        configurator.UsingJobData(options.ToJobData());
        return configurator;
    }

    /// <summary>
    /// Configures a <see cref="FileScanJob" /> with the file to watch, the listener to tell, and how
    /// long the file must have settled.
    /// </summary>
    public static TConfigurator UsingFileScanOptions<TConfigurator>(this TConfigurator configurator, FileScanOptions options)
        where TConfigurator : class, IJobConfigurator<FileScanJob>
    {
        ArgumentNullException.ThrowIfNull(configurator);
        ArgumentNullException.ThrowIfNull(options);

        configurator.UsingJobData(options.ToJobData());
        return configurator;
    }

    /// <summary>
    /// Configures a <see cref="NativeJob" /> with the command to run and how to run it.
    /// </summary>
    /// <example>
    /// <code>
    /// IJobDetail job = JobBuilder.Create&lt;NativeJob&gt;()
    ///     .WithIdentity("nightlyReport")
    ///     .UsingNativeJobOptions(new NativeJobOptions
    ///     {
    ///         Command = "report.exe",
    ///         Parameters = "--nightly",
    ///         ConsumeStreams = true,
    ///     })
    ///     .Build();
    /// </code>
    /// </example>
    public static TConfigurator UsingNativeJobOptions<TConfigurator>(this TConfigurator configurator, NativeJobOptions options)
        where TConfigurator : class, IJobConfigurator<NativeJob>
    {
        ArgumentNullException.ThrowIfNull(configurator);
        ArgumentNullException.ThrowIfNull(options);

        configurator.UsingJobData(options.ToJobData());
        return configurator;
    }

    /// <summary>
    /// Configures a <see cref="SendMailJob" /> with the message to send and the server to send it
    /// through.
    /// </summary>
    /// <remarks>
    /// <see cref="SendMailOptions" /> has no credentials on purpose; register an
    /// <see cref="System.Net.ICredentialsByHost" /> with the container instead of putting a password
    /// in job data.
    /// </remarks>
    public static TConfigurator UsingSendMailOptions<TConfigurator>(this TConfigurator configurator, SendMailOptions options)
        where TConfigurator : class, IJobConfigurator<SendMailJob>
    {
        ArgumentNullException.ThrowIfNull(configurator);
        ArgumentNullException.ThrowIfNull(options);

        configurator.UsingJobData(options.ToJobData());
        return configurator;
    }
}
