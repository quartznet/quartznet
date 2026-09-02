using System.Globalization;

using Microsoft.Extensions.DependencyInjection;

namespace Quartz.Jobs;

/// <summary>
/// Internal model to hold settings used by <see cref="DirectoryScanJob"/>
/// </summary>
internal sealed class DirectoryScanJobModel
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
    internal DateTimeOffset LastModifiedTime { get; private set; }
    internal DateTimeOffset MaxAgeTime => TimeProvider.GetUtcNow() - Options.MinimumUpdateAge;
    private TimeProvider TimeProvider { get; set; } = null!;
    private DirectoryScanOptions Options { get; set; } = null!;
    private JobDataMap JobDetailJobDataMap { get; set; } = null!;
    internal string SearchPattern => Options.SearchPattern;
    internal bool IncludeSubDirectories => Options.IncludeSubDirectories;

    /// <summary>
    /// Creates an instance of DirectoryScanJobModel by inspecting the provided IJobExecutionContext <see cref="IJobExecutionContext"/>
    /// </summary>
    /// <param name="context">Content of the job execution <see cref="IJobExecutionContext"/></param>
    /// <param name="serviceProvider">Optional service provider for resolving dependencies via DI</param>
    /// <param name="timeProvider">The job's clock, which decides how recent "too recent to be settled" is</param>
    /// <returns>Instance of DirectoryScanJobModel based on the IJobExecutionContext <see cref="IJobExecutionContext"/> passed in</returns>
    internal static DirectoryScanJobModel GetInstance(IJobExecutionContext context, IServiceProvider? serviceProvider, TimeProvider timeProvider)
    {
        JobDataMap mergedJobDataMap = context.MergedJobDataMap;
        SchedulerContext schedCtxt;
        try
        {
            schedCtxt = context.Scheduler.Context;
        }
        catch (SchedulerException e)
        {
            throw new JobExecutionException("Error obtaining scheduler context.", e);
        }

        DirectoryScanOptions options = DirectoryScanOptions.FromJobData(mergedJobDataMap);

        var model = new DirectoryScanJobModel
        {
            TimeProvider = timeProvider,
            Options = options,
            DirectoryScanListener = GetListener(options.ScanListenerName, schedCtxt, serviceProvider),
            LastModifiedTime = mergedJobDataMap.ReadTimestamp(DirectoryScanJob.LastModifiedTime) ?? DateTimeOffset.MinValue,
            JobDetailJobDataMap = context.JobDetail.JobDataMap,
            DirectoriesToScan = GetDirectoriesToScan(schedCtxt, mergedJobDataMap, options.DirectoryProviderName)
                .Distinct().ToList(),
            CurrentFileList = ReadFileList(mergedJobDataMap),
        };

        return model;
    }


    /// <summary>
    /// Updates the last modified time to the one provided, unless the currently set one is later
    /// </summary>
    /// <param name="lastWriteTimeFromFiles">Latest write time of the files scanned</param>
    internal void UpdateLastModifiedTime(DateTimeOffset lastWriteTimeFromFiles)
    {
        DateTimeOffset newLastModifiedTime = lastWriteTimeFromFiles > LastModifiedTime
            ? lastWriteTimeFromFiles
            : LastModifiedTime;

        // It is the JobDataMap on the JobDetail which is actually stateful
        JobDetailJobDataMap[DirectoryScanJob.LastModifiedTime] = newLastModifiedTime;
    }

    /// <summary>
    /// Updates the file list for comparison in next iteration
    /// </summary>
    /// <remarks>
    /// Stored as a <see cref="Dictionary{TKey,TValue}" /> of full path to last-write ticks, because this
    /// job is <c>[PersistJobDataAfterExecution]</c> and a job data map is written to
    /// <c>QRTZ_JOB_DETAILS</c> by whichever serializer is configured. A <c>List&lt;FileInfo&gt;</c> is
    /// not a value either serializer can read back — it is refused outright — so the job's first firing
    /// against a persistent store failed to persist, and reading the job's data map over the HTTP API or
    /// the dashboard refused it too. A string-to-string dictionary is one of the shapes both admit, and a
    /// path is what the comparison actually uses.
    /// </remarks>
    /// <param name="fileList">What this scan saw.</param>
    internal void UpdateFileList(List<FileInfo> fileList)
    {
        Dictionary<string, string> stored = new(fileList.Count, StringComparer.Ordinal);
        foreach (FileInfo file in fileList)
        {
            stored[file.FullName] = file.LastWriteTimeUtc.Ticks.ToString(CultureInfo.InvariantCulture);
        }

        JobDetailJobDataMap[DirectoryScanJob.CurrentFileList] = stored;
    }

    /// <summary>
    /// What the previous scan saw, as the paths it recorded.
    /// </summary>
    /// <remarks>
    /// A <c>List&lt;FileInfo&gt;</c> is still read, because a scheduler that has been running since
    /// before this changed holds one in its in-memory map; nothing persistent can, since storing one
    /// always failed.
    /// </remarks>
    private static List<FileInfo> ReadFileList(JobDataMap mergedJobDataMap)
    {
        if (!mergedJobDataMap.TryGetValue(DirectoryScanJob.CurrentFileList, out object? value) || value is null)
        {
            return [];
        }

        if (value is Dictionary<string, string> stored)
        {
            List<FileInfo> files = new(stored.Count);
            foreach (string path in stored.Keys)
            {
                files.Add(new FileInfo(path));
            }

            return files;
        }

        return value as List<FileInfo> ?? [];
    }


    private static List<string> GetDirectoriesToScan(SchedulerContext schedCtxt, JobDataMap mergedJobDataMap, string? explicitDirProviderName)
    {
        IDirectoryProvider directoryProvider = new DefaultDirectoryProvider();

        if (explicitDirProviderName is not null)
        {
            if (!schedCtxt.TryGetValue(explicitDirProviderName, out var temp))
            {
                throw new JobExecutionException($"IDirectoryProvider named '{explicitDirProviderName}' not found in SchedulerContext");
            }
            directoryProvider = (IDirectoryProvider) temp!;
        }

        return directoryProvider.GetDirectoriesToScan(mergedJobDataMap).ToList();
    }


    /// <summary>
    /// The listener the job data names: a keyed registration, a registered
    /// <see cref="IDirectoryScanListener" /> whose type carries the name, or an entry in the
    /// <see cref="SchedulerContext" /> — which is how <see cref="FileScanJob" /> has always found its own.
    /// </summary>
    /// <remarks>
    /// The name is caller-controlled job data. It used to be looked up by sweeping <em>every loaded
    /// assembly</em> with <c>GetTypes()</c> on the first miss and remembering the answer — including the
    /// misses — in a process-lifetime dictionary keyed on that name, so a job data map could grow an
    /// unbounded cache and pay for a full reflection sweep per distinct value. It also matched on the
    /// simple type name from any assembly, which is not an identity. All three lookups here are bounded
    /// by what the application registered.
    /// </remarks>
    private static IDirectoryScanListener GetListener(string listenerName, SchedulerContext schedCtxt, IServiceProvider? serviceProvider)
    {
        if (serviceProvider is not null)
        {
            // A keyed registration is the precise form: AddKeyedSingleton<IDirectoryScanListener>(name).
            if (serviceProvider is IKeyedServiceProvider
                && serviceProvider.GetKeyedService<IDirectoryScanListener>(listenerName) is { } keyed)
            {
                return keyed;
            }

            // Otherwise the registered listeners, matched by their type's name, which is what
            // ScanListenerName = nameof(InboxListener) says.
            foreach (IDirectoryScanListener candidate in serviceProvider.GetServices<IDirectoryScanListener>())
            {
                if (string.Equals(candidate.GetType().Name, listenerName, StringComparison.Ordinal))
                {
                    return candidate;
                }
            }
        }

        // Fall back to SchedulerContext (legacy behavior)
        if (!schedCtxt.TryGetValue(listenerName, out var listenerFromContext))
        {
            throw new JobExecutionException(
                $"IDirectoryScanListener named '{listenerName}' was not found. Register it in the container as an "
                + $"IDirectoryScanListener - keyed on '{listenerName}', or with a type named '{listenerName}' - or "
                + $"put the instance in the SchedulerContext under that key.");
        }

        return (IDirectoryScanListener) listenerFromContext!;
    }
}