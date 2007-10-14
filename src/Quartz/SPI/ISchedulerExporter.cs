using Quartz.Simpl;

namespace Quartz.Spi
{
    /// <summary>
    /// Service interface for scheduler exporters.
    /// </summary>
    /// <author>Marko Lahma</author>
    public interface ISchedulerExporter
    {
        /// <summary>
        /// Binds (exports) scheduler to external context.
        /// </summary>
        /// <param name="scheduler"></param>
        void Bind(IRemotableQuartzScheduler scheduler);

        /// <summary>
        /// Unbinds scheduler from external context.
        /// </summary>
        /// <param name="scheduler"></param>
        void UnBind(IRemotableQuartzScheduler scheduler);
    }
}
