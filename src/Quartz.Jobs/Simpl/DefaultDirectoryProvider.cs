using Quartz.Job;
using Quartz.Spi;

namespace Quartz.Simpl;

/// <summary>
/// Default directory provider that inspects and parses the merged JobDataMap <see cref="JobDataMap"/>
/// for the entries <see cref="BaseDirectoryScanJob.DirectoryName"/> and <see cref="BaseDirectoryScanJob.DirectoryNames"/>
/// to supply the directory paths
/// </summary>
internal sealed class DefaultDirectoryProvider : IDirectoryProvider
{
    public IReadOnlyList<string> GetDirectoriesToScan(JobDataMap mergedJobDataMap)
    {
        List<string> directoriesToScan = new List<string>();
        var dirName = mergedJobDataMap.GetString(BaseDirectoryScanJob.DirectoryName);
        var dirNames = mergedJobDataMap.GetString(BaseDirectoryScanJob.DirectoryNames);

        if (dirName == null && dirNames == null)
        {
            throw new JobExecutionException($"The parameter '{BaseDirectoryScanJob.DirectoryName}' or '{BaseDirectoryScanJob.DirectoryNames}' " +
                                            "is required and was not found in merged JobDataMap");
        }

        /*
            If the user supplied both DirectoryScanJob.DirectoryName and DirectoryScanJob.DirectoryNames,
            then just use both. The directory names will be 'distincted' by the caller.
        */
        if (dirName != null)
        {
            directoriesToScan.Add(dirName);
        }
        if (dirNames != null)
        {
            directoriesToScan.AddRange(
                dirNames.Split(new[] { ";" }, StringSplitOptions.RemoveEmptyEntries));
        }

        return directoriesToScan;
    }
}