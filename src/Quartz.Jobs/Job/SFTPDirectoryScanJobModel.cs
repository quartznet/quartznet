using Quartz.Simpl;
using Quartz.Spi;
using Renci.SshNet;
using Renci.SshNet.Sftp;

namespace Quartz.Job;

/// <summary>
/// Internal model to hold settings used by <see cref="SFTPDirectoryScanJob"/>
/// </summary>
internal sealed class SFTPDirectoryScanJobModel
{
    /// <summary>
    /// We only want this type of object to be instantiated by inspecting the data 
    /// of a IJobExecutionContext <see cref="IJobExecutionContext"/>. Use the 
    /// GetInstance() <see cref="GetInstance"/> method to create an instance of this
    /// object type
    /// </summary>
    private SFTPDirectoryScanJobModel(SftpClient sftpClient)
    {
        SFTPSource = sftpClient;
    }

    internal List<string> DirectoriesToScan { get; private set; } = null!;
    internal List<SftpFile> CurrentFileList { get; private set; } = null!;
    internal ISFTPDirectoryScanListener SFTPDirectoryScanListener { get; private set; } = null!;
    internal DateTime LastModTime { get; private set; }
    internal DateTime MaxAgeDate => DateTime.Now - MinUpdateAge;
    private TimeSpan MinUpdateAge { get; set; }
    private JobDataMap JobDetailJobDataMap { get; set; } = null!;
    public string SearchPattern { get; internal set; } = null!;
    public string HostServer { get; internal set; } = null!;
    public string User { get; internal set; } = null!;
    public string Password { get; internal set; } = null!;

    public SftpClient SFTPSource { get; internal set; }



    /// <summary>
    /// Creates an instance of SFTPDirectoryScanJobModel by inspecting the provided IJobExecutionContext <see cref="IJobExecutionContext"/>
    /// </summary>
    /// <param name="context">Content of the job execution <see cref="IJobExecutionContext"/></param>
    /// <returns>Instance of SFTPDirectoryScanJobModel based on the IJobExecutionContext <see cref="IJobExecutionContext"/> passed in</returns>
    internal static SFTPDirectoryScanJobModel GetInstance(IJobExecutionContext context)
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

        if (!mergedJobDataMap.ContainsKey(SFTPDirectoryScanJob.HostServer)
            || !mergedJobDataMap.ContainsKey(SFTPDirectoryScanJob.User)
            || !mergedJobDataMap.ContainsKey(SFTPDirectoryScanJob.Password))
        {
            throw new JobExecutionException($"Missing define {SFTPDirectoryScanJob.HostServer} or {SFTPDirectoryScanJob.User} or {SFTPDirectoryScanJob.Password}");
        }
        else
        {
            var sf = new SftpClient(mergedJobDataMap.GetString(SFTPDirectoryScanJob.HostServer),
            mergedJobDataMap.GetString(SFTPDirectoryScanJob.User), mergedJobDataMap.GetString(SFTPDirectoryScanJob.Password));

            var model = new SFTPDirectoryScanJobModel(sf)
            {
                SFTPDirectoryScanListener = GetListener(mergedJobDataMap, schedCtxt),
                LastModTime = mergedJobDataMap.ContainsKey(BaseDirectoryScanJob.LastModifiedTime)
                ? mergedJobDataMap.GetDateTime(BaseDirectoryScanJob.LastModifiedTime)
                : DateTime.MinValue,
                MinUpdateAge = mergedJobDataMap.ContainsKey(BaseDirectoryScanJob.MinimumUpdateAge)
                ? TimeSpan.FromMilliseconds(mergedJobDataMap.GetLong(BaseDirectoryScanJob.MinimumUpdateAge))
                : TimeSpan.FromSeconds(5), // default of 5 seconds
                JobDetailJobDataMap = context.JobDetail.JobDataMap,
                DirectoriesToScan = GetDirectoriesToScan(schedCtxt, mergedJobDataMap)
                .Distinct().ToList(),
                CurrentFileList = mergedJobDataMap.ContainsKey(BaseDirectoryScanJob.CurrentFileList) ?
                (List<SftpFile>) mergedJobDataMap[BaseDirectoryScanJob.CurrentFileList]
                : new List<SftpFile>(),
                SearchPattern = mergedJobDataMap.ContainsKey(SFTPDirectoryScanJob.SearchPattern) ?
                mergedJobDataMap.GetString(SFTPDirectoryScanJob.SearchPattern)! : ".*"
            };
            return model;
        }
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
        JobDetailJobDataMap.Put(SFTPDirectoryScanJob.LastModifiedTime, newLastModifiedDate);
    }

    /// <summary>
    /// Updates the file list for comparison in next iteration
    /// </summary>
    /// <param name="fileList"></param>
    internal void UpdateFileList(List<SftpFile> fileList)
    {
        JobDetailJobDataMap.Put(SFTPDirectoryScanJob.CurrentFileList, fileList);
    }


    private static List<string> GetDirectoriesToScan(SchedulerContext schedCtxt, JobDataMap mergedJobDataMap)
    {
        IDirectoryProvider directoryProvider = new DefaultDirectoryProvider();
        var explicitDirProviderName = mergedJobDataMap.GetString(SFTPDirectoryScanJob.DirectoryProviderName);

        if (explicitDirProviderName != null)
        {
            if (!schedCtxt.TryGetValue(explicitDirProviderName, out var temp))
            {
                throw new JobExecutionException($"IDirectoryProvider named '{explicitDirProviderName}' not found in SchedulerContext");
            }
            directoryProvider = (IDirectoryProvider) temp;
        }

        return directoryProvider.GetDirectoriesToScan(mergedJobDataMap).ToList();
    }


    private static ISFTPDirectoryScanListener GetListener(JobDataMap mergedJobDataMap, SchedulerContext schedCtxt)
    {
        var listenerName = mergedJobDataMap.GetString(SFTPDirectoryScanJob.DirectoryScanListenerName);

        if (listenerName == null)
        {
            throw new JobExecutionException("Required parameter '" +
                                            SFTPDirectoryScanJob.DirectoryScanListenerName + "' not found in merged JobDataMap");
        }

        if (!schedCtxt.TryGetValue(listenerName, out var listener))
        {
            throw new JobExecutionException($"ISFTPDirectoryScanListener named '{listenerName}' not found in SchedulerContext");

        }

        return (ISFTPDirectoryScanListener) listener;
    }
}