using System.Threading.Tasks;

namespace Quartz.Simpl
{
    public class DefaultThreadPool : TaskSchedulingThreadPool
    {
        protected override TaskScheduler GetDefaultScheduler()
        {
            return TaskScheduler.Default;
        }
    }
}
