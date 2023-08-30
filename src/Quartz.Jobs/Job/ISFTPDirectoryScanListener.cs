using Renci.SshNet.Sftp;

namespace Quartz.Job;

///<summary>
/// Interface for objects wishing to receive a 'call-back' from a <see cref="SFTPDirectoryScanJob"/>
/// </summary>
/// <remarks>
/// <para>Instances should be stored in the <see cref="SchedulerContext"/> such that the
/// <see cref="SFTPDirectoryScanJob"/> can find it.</para>
/// </remarks>
/// <author>Monty</author>
public interface ISFTPDirectoryScanListener
{
    /// <param name="updatedFiles">
    /// An array of <see cref="SftpFile"/> objects that were updated/added since the last scan of the directory
    /// </param>
    void FilesUpdatedOrAdded(IReadOnlyCollection<SftpFile> updatedFiles);

    /// <param name="deletedFiles">
    /// An array of <see cref="SftpFile"/> objects that were deleted since the last scan of the directory
    /// </param>
    void FilesDeleted(IReadOnlyCollection<SftpFile> deletedFiles);
}