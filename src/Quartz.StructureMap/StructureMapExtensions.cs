using StructureMap;

namespace Quartz.StructureMap
{
    public static class StructureMapExtensions
    {
        public static IScheduler UseStructureMap(this IScheduler scheduler, IContainer container)
        {
            scheduler.JobFactory = new StructureMapJobFactory(container);
            return scheduler;
        }
    }
}
