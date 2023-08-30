using Quartz.Spi;

namespace Quartz.Job;
/// <summary>
/// Base class for DirectroyScanJob
/// </summary>
/// <author>Monty</author>
public class BaseDirectoryScanJob
{
    ///<see cref="JobDataMap"/> key with which to specify the directory to be
    /// monitored - an absolute path is recommended.
    public const string DirectoryName = "DIRECTORY_NAME";

    ///<see cref="JobDataMap"/> key with which to specify the directories to be
    /// monitored. Directory paths should be separated by a semi-colon (;) - absolute paths are recommended.
    public const string DirectoryNames = "DIRECTORY_NAMES";

    /// <see cref="JobDataMap"/> key with which to specify the
    /// <see cref="IDirectoryProvider"/> to be used to provide
    /// the directory paths to be monitored - absolute paths are recommended.
    public const string DirectoryProviderName = "DIRECTORY_PROVIDER_NAME";

    /// <see cref="JobDataMap"/> key with which to specify the
    /// <see cref="ISFTPDirectoryScanListener"/> to be
    /// notified when the directory contents change.
    public const string DirectoryScanListenerName = "DIRECTORY_SCAN_LISTENER_NAME";

    /// <see cref="JobDataMap"/> key with which to specify a <see cref="long"/>
    /// value that represents the minimum number of milliseconds that must have
    /// passed since the file's last modified time in order to consider the file
    /// new/altered.  This is necessary because another process may still be
    /// in the middle of writing to the file when the scan occurs, and the
    ///  file may therefore not yet be ready for processing.
    /// <para>If this parameter is not specified, a default value of 5000 (five seconds) will be used.</para>
    public const string MinimumUpdateAge = "MINIMUM_UPDATE_AGE";

    internal const string LastModifiedTime = "LAST_MODIFIED_TIME";


    ///<see cref="JobDataMap"/> key to store the current file list of the scanned directories.
    ///This is required to find out deleted files during next iteration.
    internal const string CurrentFileList = "CURRENT_FILE_LIST";

    internal static void AddToList<T>(List<T> fileList, List<T> updatedFileList)
    {
        lock (fileList)
        {
            foreach (var fileInfo in updatedFileList)
            {
                fileList.Add(fileInfo);
            }
        }
    }


}
