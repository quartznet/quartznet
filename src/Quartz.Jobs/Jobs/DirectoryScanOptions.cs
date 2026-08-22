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
/// What <see cref="DirectoryScanJob" /> scans and when it considers a file settled.
/// </summary>
/// <remarks>
/// <para>
/// The job is configured through its <see cref="JobDataMap" />, and those keys are the persisted
/// contract — this type is the named way to write and read them.
/// <see cref="JobConfiguratorExtensions.UsingDirectoryScanOptions{TConfigurator}" /> writes them,
/// <see cref="FromJobData" /> reads them back, and job data written by hand or by an earlier version
/// reads the same either way.
/// </para>
/// <para>
/// The directories to scan come either from <see cref="Directories" /> or from an
/// <see cref="IDirectoryProvider" /> named by <see cref="DirectoryProviderName" />. Naming neither
/// leaves the job nothing to scan, which it reports when it first fires.
/// </para>
/// </remarks>
public sealed record DirectoryScanOptions
{
    /// <summary>
    /// The directories to monitor; absolute paths are recommended. They are stored semicolon-separated
    /// under <see cref="DirectoryScanJob.DirectoryNames" />, so a path may not itself contain a
    /// semicolon.
    /// </summary>
    public IReadOnlyList<string> Directories { get; init; } = [];

    /// <summary>
    /// The <see cref="SchedulerContext" /> key of an <see cref="IDirectoryProvider" /> that supplies
    /// the directories instead of <see cref="Directories" />, or <see langword="null" /> to use the
    /// paths in <see cref="Directories" />.
    /// </summary>
    public string? DirectoryProviderName { get; init; }

    /// <summary>
    /// The name of the <see cref="IDirectoryScanListener" /> to notify: either a type registered in
    /// the container, or a <see cref="SchedulerContext" /> key an instance is stored under.
    /// </summary>
    public required string ScanListenerName { get; init; }

    /// <summary>
    /// The pattern file names must match, which may combine literal path characters with the
    /// <c>*</c> and <c>?</c> wildcards. Defaults to <c>*</c>, every file.
    /// </summary>
    public string SearchPattern { get; init; } = "*";

    /// <summary>
    /// Whether to descend into sub-directories. Defaults to <see langword="false" />.
    /// </summary>
    public bool IncludeSubDirectories { get; init; }

    /// <summary>
    /// How long a file must have been left alone to count as new or altered rather than as one
    /// another process is still writing. Defaults to five seconds.
    /// </summary>
    /// <remarks>
    /// Persisted as a whole number of milliseconds under
    /// <see cref="DirectoryScanJob.MinimumUpdateAge" />, which is what earlier versions wrote.
    /// </remarks>
    public TimeSpan MinimumUpdateAge { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Two sets of options are equal when they say the same thing, which for
    /// <see cref="Directories" /> means the same paths in the same order.
    /// </summary>
    /// <remarks>
    /// Spelled out because the compiler's version would compare the list by reference, and two
    /// options records read out of the same job data would then differ.
    /// </remarks>
    public bool Equals(DirectoryScanOptions? other)
    {
        return other is not null
               && Directories.SequenceEqual(other.Directories, StringComparer.Ordinal)
               && DirectoryProviderName == other.DirectoryProviderName
               && ScanListenerName == other.ScanListenerName
               && SearchPattern == other.SearchPattern
               && IncludeSubDirectories == other.IncludeSubDirectories
               && MinimumUpdateAge == other.MinimumUpdateAge;
    }

    /// <inheritdoc />
    public override int GetHashCode()
    {
        HashCode hash = new HashCode();
        foreach (string directory in Directories)
        {
            hash.Add(directory, StringComparer.Ordinal);
        }

        hash.Add(DirectoryProviderName);
        hash.Add(ScanListenerName);
        hash.Add(SearchPattern);
        hash.Add(IncludeSubDirectories);
        hash.Add(MinimumUpdateAge);
        return hash.ToHashCode();
    }

    /// <summary>
    /// Reads the options out of a job's data, taking the default for every key it does not carry.
    /// </summary>
    /// <param name="data">
    /// The job data to read, normally <see cref="IJobExecutionContext.MergedJobDataMap" />.
    /// </param>
    /// <exception cref="JobExecutionException">
    /// <see cref="DirectoryScanJob.DirectoryScanListenerName" /> is absent; there is nothing to
    /// notify, so nothing the job can usefully do.
    /// </exception>
    public static DirectoryScanOptions FromJobData(JobDataMap data)
    {
        ArgumentNullException.ThrowIfNull(data);

        string? listenerName = data.GetString(DirectoryScanJob.DirectoryScanListenerName);
        if (listenerName is null)
        {
            throw new JobExecutionException("Required parameter '" +
                                            DirectoryScanJob.DirectoryScanListenerName + "' not found in merged JobDataMap");
        }

        return new DirectoryScanOptions
        {
            Directories = ReadDirectories(data),
            DirectoryProviderName = data.GetString(DirectoryScanJob.DirectoryProviderName),
            ScanListenerName = listenerName,
            SearchPattern = data.TryGetString(DirectoryScanJob.SearchPattern, out string? pattern) && !string.IsNullOrEmpty(pattern)
                ? pattern
                : "*",
            IncludeSubDirectories = data.TryGetBoolean(DirectoryScanJob.IncludeSubDirectories, out bool includeSubDirectories) && includeSubDirectories,
            MinimumUpdateAge = JobDataMapDurations.ReadMilliseconds(data, DirectoryScanJob.MinimumUpdateAge) ?? TimeSpan.FromSeconds(5),
        };
    }

    /// <summary>
    /// Writes the options as the job data keys <see cref="DirectoryScanJob" /> reads.
    /// </summary>
    public JobDataMap ToJobData()
    {
        JobDataMap data = new JobDataMap();

        if (Directories.Count > 0)
        {
            foreach (string directory in Directories)
            {
                if (directory.Contains(';', StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"Directory '{directory}' contains a semicolon, which is what separates the paths stored under '{DirectoryScanJob.DirectoryNames}'.");
                }
            }

            data[DirectoryScanJob.DirectoryNames] = string.Join(';', Directories);
        }

        if (DirectoryProviderName is not null)
        {
            data[DirectoryScanJob.DirectoryProviderName] = DirectoryProviderName;
        }

        data[DirectoryScanJob.DirectoryScanListenerName] = ScanListenerName;
        data[DirectoryScanJob.SearchPattern] = SearchPattern;
        data[DirectoryScanJob.IncludeSubDirectories] = IncludeSubDirectories;
        data[DirectoryScanJob.MinimumUpdateAge] = (long) MinimumUpdateAge.TotalMilliseconds;

        return data;
    }

    /// <summary>
    /// The directory paths spelled out in job data, which is what
    /// <see cref="DefaultDirectoryProvider" /> serves when no other provider is named.
    /// </summary>
    internal static List<string> ReadDirectories(JobDataMap data)
    {
        List<string> directories = new List<string>();

        string? directoryName = data.GetString(DirectoryScanJob.DirectoryName);
        if (directoryName is not null)
        {
            directories.Add(directoryName);
        }

        string? directoryNames = data.GetString(DirectoryScanJob.DirectoryNames);
        if (directoryNames is not null)
        {
            directories.AddRange(directoryNames.Split(';', StringSplitOptions.RemoveEmptyEntries));
        }

        return directories;
    }
}
