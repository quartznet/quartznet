using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Quartz
{
    internal class QuartzHostedService : BackgroundService
    {
        private readonly ISchedulerFactory schedulerFactory;
        private readonly IOptions<QuartzHostedServiceOptions> options;

        public QuartzHostedService(
            ISchedulerFactory schedulerFactory,
            IOptions<QuartzHostedServiceOptions> options)
        {
            this.schedulerFactory = schedulerFactory;
            this.options = options;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var scheduler = await schedulerFactory.GetScheduler(stoppingToken);

            if (options.Value.StartDelay.HasValue)
            {
                await scheduler.StartDelayed(options.Value.StartDelay.Value, stoppingToken);
            }
            else
            {
                await scheduler.Start(stoppingToken);
            }

            await WhenStopping(stoppingToken);

            await scheduler.Shutdown(options.Value.WaitForJobsToComplete, CancellationToken.None);
        }

        async Task WhenStopping(CancellationToken stoppingToken)
        {
            var tcs = new TaskCompletionSource<object?>();
            using (stoppingToken.Register(() => tcs.SetResult(null)))
            {
                await tcs.Task;
            }
        }
    }
}
