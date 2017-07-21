using System.Threading.Tasks;

using Quartz.Util;

namespace Quartz.Simpl
{
    /// <summary>
    /// An implementation of the TaskSchedulingThreadPool which uses a custom task scheduler
    /// with a dedicated pool of threads reserved only for its own scheduling purposes
    /// </summary>
    public class DedicatedThreadPool : TaskSchedulingThreadPool
    {
        /// <summary>
        /// Returns a QueuedTaskScheduler
        /// </summary>
        /// <returns>QueuedTaskScheduler with threadCount == MaxConcurrency</returns>
        protected override TaskScheduler GetDefaultScheduler()
        {
            return new QueuedTaskScheduler(MaxConcurency);
        }
    }
}
