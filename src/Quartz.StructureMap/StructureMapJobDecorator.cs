using StructureMap.Pipeline;

namespace Quartz.StructureMap
{
    public class StructureMapJobDecorator : IJob
    {
        private readonly IJob job;

        public StructureMapJobDecorator(IJob job)
        {
            this.job = job;
        }

        public void Execute(IJobExecutionContext context)
        {
            try
            {
                job.Execute(context);
            }
            finally
            {
                ThreadLocalStorageLifecycle.RefreshCache();
            }
        }

       
    }
}
