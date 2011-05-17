using Quartz.Spi;

namespace Quartz.Impl
{
    /// <summary>
    /// Schedules work on a newly spawned thread. This is the default Quartz behavior.
    /// </summary>
    /// <author>matt.accola</author>
    public class DefaultThreadExecutor : IThreadExecutor
    {
        public void Execute(QuartzThread thread)
        {
            thread.Start();
        }

        public void Initialize()
        {
        }
    }
}