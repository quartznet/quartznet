using System.Threading.Tasks;

namespace Quartz.Simpl
{
    /// <summary>
    /// An implementation of the TaskSchedulerThreadPool using the default task scheduler
    /// </summary>
    public class DefaultThreadPool : TaskSchedulingThreadPool
    {
        /// <summary>
        /// Returns TaskScheduler.Default
        /// </summary>
        /// <returns>TaskScheduler.Default</returns>
        protected override TaskScheduler GetDefaultScheduler()
        {
            return TaskScheduler.Default;
        }
    }
}
