using Quartz.Util;

namespace Quartz.Simpl
{
    public class DedicatedThreadPool : TaskSchedulingThreadPool
    {
        public override void Initialize()
        {
            Scheduler = new QueuedTaskScheduler(MaxConcurency);
            base.Initialize();
        }
    }
}
