using System.Collections.Generic;
using System.IO;

namespace Quartz.Job
{
    ///<summary>Interface for objects wishing to receive a 'call-back' from a <see cref="DirectoryScanJob"/></summary>
    ///<remarks><para>Instances should be stored in the <see cref="SchedulerContext"/> such that the
    ///<see cref="DirectoryScanJob"/> can find it.</para></remarks>
    ///<author>James House</author>
    /// <author>Marko Lahma (.NET)</author>
    public interface IDirectoryScanListener
    {
        ///<param name="updatedFiles">An array of <see cref="FileInfo"/> objects that were updated/added 
        ///since the last scan of the directory</param>
        void FilesUpdatedOrAdded(IEnumerable<FileInfo> updatedFiles);
    }
}