namespace Quartz.Jobs;

/// <summary>
/// Default directory provider that inspects and parses the merged JobDataMap <see cref="JobDataMap"/>
/// for the entries <see cref="DirectoryScanJob.DirectoryName"/> and <see cref="DirectoryScanJob.DirectoryNames"/>
/// to supply the directory paths
/// </summary>
internal sealed class DefaultDirectoryProvider : IDirectoryProvider
{
    public List<string> GetDirectoriesToScan(JobDataMap mergedJobDataMap)
    {
        /*
            If the user supplied both DirectoryScanJob.DirectoryName and DirectoryScanJob.DirectoryNames,
            then just use both. The directory names will be 'distincted' by the caller.
        */
        List<string> directoriesToScan = DirectoryScanOptions.ReadDirectories(mergedJobDataMap);

        if (directoriesToScan.Count == 0)
        {
            throw new JobExecutionException($"The parameter '{DirectoryScanJob.DirectoryName}' or '{DirectoryScanJob.DirectoryNames}' " +
                                            "is required and was not found in merged JobDataMap");
        }

        return directoriesToScan;
    }
}