using System.Threading;
using System.Threading.Tasks;

using Quartz.Listener;

namespace Quartz
{
    internal class QuartzHostedServiceListener : SchedulerListenerSupport, IQuartzHostedServiceListener
    {
        public bool Running { get; private set; }
        public int ErrorCount { get; private set; }

        public override Task SchedulerError(string msg, SchedulerException cause, CancellationToken cancellationToken = default)
        {
            ErrorCount++;
            return Task.CompletedTask;
        }

        public override Task SchedulerStarted(CancellationToken cancellationToken = default)
        {
            Running = true;
            return Task.CompletedTask;
        }

        public override Task SchedulerShutdown(CancellationToken cancellationToken = default)
        {
            Running = false;
            return Task.CompletedTask;
        }
    }
}
