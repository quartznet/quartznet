using System.Collections.Concurrent;
using System.Reflection;

using Microsoft.Extensions.Logging;

using Quartz.Diagnostics;

namespace Quartz.Jobs;

/// <summary>
/// Internal model to hold settings used by <see cref="DirectoryScanJob"/>
/// </summary>
internal sealed class DirectoryScanJobModel
{
    private static readonly ConcurrentDictionary<string, Type?> listenerTypeCache = new();
    private static readonly ILogger<DirectoryScanJobModel> logger = LogProvider.CreateLogger<DirectoryScanJobModel>();

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
            CurrentFileList = mergedJobDataMap.TryGetValue(DirectoryScanJob.CurrentFileList, out object? value)
                ? (List<FileInfo>) value!
                : [],
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
    /// <param name="fileList"></param>
    internal void UpdateFileList(List<FileInfo> fileList)
    {
        JobDetailJobDataMap[DirectoryScanJob.CurrentFileList] = fileList;
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


    private static IDirectoryScanListener GetListener(string listenerName, SchedulerContext schedCtxt, IServiceProvider? serviceProvider)
    {
        // First, try to resolve from DI if service provider is available
        if (serviceProvider is not null)
        {
            // Try to get listener by type name from DI (with caching to avoid repeated reflection)
            var listenerType = listenerTypeCache.GetOrAdd(listenerName, name =>
            {
                return AppDomain.CurrentDomain.GetAssemblies()
                    .SelectMany(assembly =>
                    {
                        try
                        {
                            return assembly.GetTypes();
                        }
                        catch (ReflectionTypeLoadException ex)
                        {
                            // Some types in the assembly couldn't be loaded, but we can still use the ones that did load
                            logger.SomeTypesNotLoaded(assembly.FullName, ex);
                            return ex.Types.Where(t => t != null)!;
                        }
                        catch (Exception ex) when (ex is FileNotFoundException or BadImageFormatException or NotSupportedException)
                        {
                            // Assembly can't be loaded - skip it
                            logger.AssemblyNotLoaded(assembly.FullName, ex);
                            return Array.Empty<Type>();
                        }
                    })
                    .FirstOrDefault(type =>
                        typeof(IDirectoryScanListener).IsAssignableFrom(type) &&
                        type.Name == name);
            });

            if (listenerType is not null)
            {
                var listener = serviceProvider.GetService(listenerType);
                if (listener is not null)
                {
                    return (IDirectoryScanListener) listener;
                }
            }
        }

        // Fall back to SchedulerContext (legacy behavior)
        if (!schedCtxt.TryGetValue(listenerName, out var listenerFromContext))
        {
            throw new JobExecutionException($"IDirectoryScanListener named '{listenerName}' not found in SchedulerContext");

        }

        return (IDirectoryScanListener) listenerFromContext!;
    }
}