using System.Collections.Generic;

using Quartz.Job;

namespace Quartz.Spi
{
    ///<summary>Interface for objects that wish to provide a list of directory paths to be 
    /// monitored to <see cref="DirectoryScanJob"/></summary>
    ///<remarks><para>Instances should be stored in the <see cref="SchedulerContext"/> such that the
    ///<see cref="DirectoryScanJob"/> can find it.</para></remarks>
    ///<author>Chris Knight (.NET)</author>
    public interface IDirectoryProvider
    {
        /// <summary>
        /// Called by <see cref="DirectoryScanJob"/> to provide a list of directory paths
        /// to montitor - absolute paths are recommended.
        /// </summary>
        /// <returns></returns>
        IReadOnlyList<string> GetDirectoriesToScan(JobDataMap mergedJobDataMap);
    }
}