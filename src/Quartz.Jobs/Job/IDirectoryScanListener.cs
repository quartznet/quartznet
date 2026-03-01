namespace Quartz.Job;

///<summary>
/// Interface for objects wishing to receive a 'call-back' from a <see cref="DirectoryScanJob"/>
/// </summary>
/// <remarks>
/// <para>
/// Implementations can be provided to <see cref="DirectoryScanJob"/> in two ways:
/// </para>
/// <list type="number">
/// <item>
/// <description>
/// Via dependency injection (recommended): Register the implementation in the DI container.
/// The job will resolve it by type name specified via <see cref="DirectoryScanJob.DirectoryScanListenerName"/>.
/// </description>
/// </item>
/// <item>
/// <description>
/// Via SchedulerContext (legacy): Store the instance in the <see cref="SchedulerContext"/>
/// with a key matching the value specified in <see cref="DirectoryScanJob.DirectoryScanListenerName"/>.
/// </description>
/// </item>
/// </list>
/// </remarks>
/// <author>Marko Lahma (.NET)</author>
public interface IDirectoryScanListener
{
    /// <param name="updatedFiles">
    /// An array of <see cref="FileInfo"/> objects that were updated/added since the last scan of the directory
    /// </param>
    void FilesUpdatedOrAdded(IReadOnlyCollection<FileInfo> updatedFiles);

    /// <param name="deletedFiles">
    /// An array of <see cref="FileInfo"/> objects that were deleted since the last scan of the directory
    /// </param>
    void FilesDeleted(IReadOnlyCollection<FileInfo> deletedFiles);
}