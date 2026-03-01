using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

using Quartz.Logging;
using Quartz.Simpl;
using Quartz.Spi;

namespace Quartz.Job;

/// <summary>
/// Internal model to hold settings used by <see cref="DirectoryScanJob"/>
/// </summary>
internal sealed class DirectoryScanJobModel
{
    private static readonly ConcurrentDictionary<string, Type> listenerTypeCache = new();
    private static readonly ILog log = LogProvider.GetLogger(typeof(DirectoryScanJobModel));

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
            CurrentFileList = mergedJobDataMap.ContainsKey(DirectoryScanJob.CurrentFileList) ?
                (List<FileInfo>)mergedJobDataMap.Get(DirectoryScanJob.CurrentFileList)
                : new List<FileInfo>(),
            SearchPattern = mergedJobDataMap.ContainsKey(DirectoryScanJob.SearchPattern) ?
                mergedJobDataMap.GetString(DirectoryScanJob.SearchPattern)! : "*",
            IncludeSubDirectories = mergedJobDataMap.ContainsKey(DirectoryScanJob.IncludeSubDirectories) 
                                    && mergedJobDataMap.GetBooleanValue(DirectoryScanJob.IncludeSubDirectories)
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
        JobDetailJobDataMap.Put(DirectoryScanJob.LastModifiedTime, newLastModifiedDate);
    }

    /// <summary>
    /// Updates the file list for comparison in next iteration
    /// </summary>
    /// <param name="fileList"></param>
    internal void UpdateFileList(List<FileInfo> fileList)
    {
        JobDetailJobDataMap.Put(DirectoryScanJob.CurrentFileList, fileList);
    }


    private static List<string> GetDirectoriesToScan(SchedulerContext schedCtxt, JobDataMap mergedJobDataMap)
    {
        IDirectoryProvider directoryProvider = new DefaultDirectoryProvider();
        var explicitDirProviderName = mergedJobDataMap.GetString(DirectoryScanJob.DirectoryProviderName);

        if (explicitDirProviderName != null)
        {
            schedCtxt.TryGetValue(explicitDirProviderName, out var temp);
            IDirectoryProvider explicitProvider = (IDirectoryProvider)temp;
            directoryProvider = explicitProvider ?? throw new JobExecutionException("IDirectoryProvider named '" +
                                                                                    explicitDirProviderName + "' not found in SchedulerContext");
        }

        return directoryProvider.GetDirectoriesToScan(mergedJobDataMap).ToList();
    }


    private static IDirectoryScanListener GetListener(JobDataMap mergedJobDataMap, SchedulerContext schedCtxt, IServiceProvider? serviceProvider)
    {
        var listenerName = mergedJobDataMap.GetString(DirectoryScanJob.DirectoryScanListenerName);

        if (listenerName == null)
        {
            throw new JobExecutionException("Required parameter '" +
                                            DirectoryScanJob.DirectoryScanListenerName + "' not found in merged JobDataMap");
        }

        // First, try to resolve from DI if service provider is available.
        if (serviceProvider != null)
        {
            Type? listenerType = null;
            if (!listenerTypeCache.TryGetValue(listenerName, out Type cachedType))
            {
                listenerType = ResolveListenerType(listenerName);
                if (listenerType != null)
                {
                    listenerTypeCache.TryAdd(listenerName, listenerType);
                }
            }
            else
            {
                listenerType = cachedType;
            }

            if (listenerType != null)
            {
                object? listenerFromDi = serviceProvider.GetService(listenerType);
                if (listenerFromDi is IDirectoryScanListener directoryScanListenerFromDi)
                {
                    return directoryScanListenerFromDi;
                }
            }
        }

        // Fall back to SchedulerContext (legacy behavior).
        schedCtxt.TryGetValue(listenerName, out object? listenerFromContext);
        if (listenerFromContext is not IDirectoryScanListener directoryScanListener)
        {
            throw new JobExecutionException("IDirectoryScanListener named '" +
                                            listenerName + "' not found in SchedulerContext");
        }

        return directoryScanListener;
    }

    private static Type? ResolveListenerType(string listenerName)
    {
        foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            IEnumerable<Type> candidateTypes;

            try
            {
                candidateTypes = assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                if (log.IsDebugEnabled())
                {
                    log.Debug($"Could not load some types from assembly {assembly.FullName} while scanning for IDirectoryScanListener: {ex.Message}");
                }

                candidateTypes = ex.Types.Where(type => type != null).Cast<Type>();
            }
            catch (Exception ex) when (ex is FileNotFoundException || ex is BadImageFormatException || ex is NotSupportedException)
            {
                if (log.IsDebugEnabled())
                {
                    log.Debug($"Could not load assembly {assembly.FullName} while scanning for IDirectoryScanListener: {ex.Message}");
                }

                continue;
            }

            Type? listenerType = candidateTypes.FirstOrDefault(type =>
                typeof(IDirectoryScanListener).IsAssignableFrom(type) &&
                type.Name == listenerName);

            if (listenerType != null)
            {
                return listenerType;
            }
        }

        return null;
    }
}
