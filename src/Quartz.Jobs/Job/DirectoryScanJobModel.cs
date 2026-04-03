using System.Collections.Concurrent;
using System.Reflection;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Quartz.Diagnostics;
using Quartz.Simpl;
using Quartz.Spi;

namespace Quartz.Job;

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
    /// <param name="serviceProvider">Optional service provider for resolving dependencies via DI</param>
    /// <returns>Instance of DirectoryScanJobModel based on the IJobExecutionContext <see cref="IJobExecutionContext"/> passed in</returns>
    internal static DirectoryScanJobModel GetInstance(IJobExecutionContext context, IServiceProvider? serviceProvider = null)
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
            DirectoryScanListener = GetListener(mergedJobDataMap, schedCtxt, serviceProvider),
            LastModTime = mergedJobDataMap.ContainsKey(DirectoryScanJob.LastModifiedTime)
                ? mergedJobDataMap.GetDateTime(DirectoryScanJob.LastModifiedTime)
                : DateTime.MinValue,
            MinUpdateAge = mergedJobDataMap.ContainsKey(DirectoryScanJob.MinimumUpdateAge)
                ? TimeSpan.FromMilliseconds(mergedJobDataMap.GetLong(DirectoryScanJob.MinimumUpdateAge))
                : TimeSpan.FromSeconds(5), // default of 5 seconds
            JobDetailJobDataMap = context.JobDetail.JobDataMap,
            DirectoriesToScan = GetDirectoriesToScan(schedCtxt, mergedJobDataMap)
                .Distinct().ToList(),
            CurrentFileList = mergedJobDataMap.TryGetValue(DirectoryScanJob.CurrentFileList, out object? value)
                ? (List<FileInfo>) value
                : [],
            SearchPattern = mergedJobDataMap.TryGetString(DirectoryScanJob.SearchPattern, out string? pattern)
                ? pattern ?? "*"
                : "*",
            IncludeSubDirectories = mergedJobDataMap.TryGetBoolean(DirectoryScanJob.IncludeSubDirectories, out bool includeSubDirectories) && includeSubDirectories,
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
        JobDetailJobDataMap[DirectoryScanJob.LastModifiedTime] = newLastModifiedDate;
    }

    /// <summary>
    /// Updates the file list for comparison in next iteration
    /// </summary>
    /// <param name="fileList"></param>
    internal void UpdateFileList(List<FileInfo> fileList)
    {
        JobDetailJobDataMap[DirectoryScanJob.CurrentFileList] = fileList;
    }


    private static List<string> GetDirectoriesToScan(SchedulerContext schedCtxt, JobDataMap mergedJobDataMap)
    {
        IDirectoryProvider directoryProvider = new DefaultDirectoryProvider();
        var explicitDirProviderName = mergedJobDataMap.GetString(DirectoryScanJob.DirectoryProviderName);

        if (explicitDirProviderName is not null)
        {
            if (!schedCtxt.TryGetValue(explicitDirProviderName, out var temp))
            {
                throw new JobExecutionException($"IDirectoryProvider named '{explicitDirProviderName}' not found in SchedulerContext");
            }
            directoryProvider = (IDirectoryProvider) temp;
        }

        return directoryProvider.GetDirectoriesToScan(mergedJobDataMap).ToList();
    }


    private static IDirectoryScanListener GetListener(JobDataMap mergedJobDataMap, SchedulerContext schedCtxt, IServiceProvider? serviceProvider)
    {
        var listenerName = mergedJobDataMap.GetString(DirectoryScanJob.DirectoryScanListenerName);

        if (listenerName is null)
        {
            throw new JobExecutionException("Required parameter '" +
                                            DirectoryScanJob.DirectoryScanListenerName + "' not found in merged JobDataMap");
        }

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
                            logger.LogDebug(ex, "Could not load some types from assembly {AssemblyName} while scanning for IDirectoryScanListener", assembly.FullName);
                            return ex.Types.Where(t => t != null)!;
                        }
                        catch (Exception ex) when (ex is FileNotFoundException or BadImageFormatException or NotSupportedException)
                        {
                            // Assembly can't be loaded - skip it
                            logger.LogDebug(ex, "Could not load assembly {AssemblyName} while scanning for IDirectoryScanListener", assembly.FullName);
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

        return (IDirectoryScanListener) listenerFromContext;
    }
}