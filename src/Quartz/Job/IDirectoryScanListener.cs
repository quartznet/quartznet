using System.IO;

namespace Quartz.Job
{
    /**
     * Interface for objects wishing to receive a 'call-back' from a 
     * <code>DirectoryScanJob</code>.
     * 
     * <p>Instances should be stored in the {@link org.quartz.SchedulerContext} 
     * such that the <code>DirectoryScanJob</code> can find it.</p>
     * 
     * @author jhouse
     * @see org.quartz.jobs.DirectoryScanJob
     * @see org.quartz.SchedulerContext
     */
    public interface IDirectoryScanListener
    {

        /**
         * @param updatedFiles The set of files that were updated/added since the
         * last scan of the directory
         */
        void FilesUpdatedOrAdded(FileInfo[] updatedFiles);
    }
}