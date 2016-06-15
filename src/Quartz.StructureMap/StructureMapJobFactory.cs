using Quartz.Spi;
using StructureMap;

namespace Quartz.StructureMap
{
    public class StructureMapJobFactory : IJobFactory
    {
        private readonly IContainer container;

        public StructureMapJobFactory(IContainer container)
        {
            this.container = container;
        }

        public IJob NewJob(TriggerFiredBundle bundle, IScheduler scheduler)
        {
            var job = (IJob)container.GetInstance(bundle.JobDetail.JobType);
            return new StructureMapJobDecorator(job);
        }

        public void ReturnJob(IJob job)
        {
            // do nothing
        }
    }
}
