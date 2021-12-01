using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Quartz
{
    internal class QuartzHostedService : IHostedService
    {
        private readonly IApplicationLifetime applicationLifetime;
        private readonly ISchedulerFactory schedulerFactory;
        private readonly IOptions<QuartzHostedServiceOptions> options;
        private IScheduler scheduler = null!;
        private bool schedulerWasStarted;

        public QuartzHostedService(
            IApplicationLifetime applicationLifetime,
            ISchedulerFactory schedulerFactory,
            IOptions<QuartzHostedServiceOptions> options)
        {
            this.applicationLifetime = applicationLifetime;
            this.schedulerFactory = schedulerFactory;
            this.options = options;
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            // Require successful initialization for application startup to succeed
            scheduler = await schedulerFactory.GetScheduler(cancellationToken);

            if (options.Value.AwaitApplicationStarted) // Sensible mode: proceed with startup, and have jobs start after application startup
            {
                // Follow the pattern from BackgroundService.StartAsync: https://github.com/dotnet/runtime/blob/main/src/libraries/Microsoft.Extensions.Hosting.Abstractions/src/BackgroundService.cs

                var deferredSchedulerStartTask = AwaitStartupCompletionAndStartSchedulerAsync(cancellationToken);

                // If the task completed synchronously, await it in order to bubble potential cancellation/failure to the caller
                // Otherwise, return, allowing application startup to complete
                if (deferredSchedulerStartTask.IsCompleted)
                {
                    await deferredSchedulerStartTask.ConfigureAwait(false);
                }
            }
            else // Legacy mode: start jobs inline
            {
                await StartSchedulerAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        private async Task AwaitStartupCompletionAndStartSchedulerAsync(CancellationToken startupCancellationToken)
        {
            using var combinedCancellationSource = CancellationTokenSource.CreateLinkedTokenSource(startupCancellationToken, applicationLifetime.ApplicationStarted);

            await Task.Delay(Timeout.InfiniteTimeSpan, combinedCancellationSource.Token) // Wait "indefinitely", until startup completes or is aborted
                .ContinueWith(_ => { }, TaskContinuationOptions.OnlyOnCanceled) // Without an OperationCanceledException on cancellation
                .ConfigureAwait(false);

            if (!startupCancellationToken.IsCancellationRequested)
            {
                await StartSchedulerAsync(applicationLifetime.ApplicationStopping).ConfigureAwait(false); // Startup has finished, but ApplicationStopping may still interrupt starting of the scheduler
            }
        }

        /// <summary>
        /// Starts the <see cref="IScheduler"/>, either immediately or after the delay configured in the <see cref="options"/>.
        /// </summary>
        private async Task StartSchedulerAsync(CancellationToken cancellationToken)
        {
            schedulerWasStarted = true;

            // Avoid potential race conditions between ourselves and StopAsync, in case it has already made its attempt to stop the scheduler
            if (applicationLifetime.ApplicationStopping.IsCancellationRequested)
            {
                return;
            }

            if (options.Value.StartDelay.HasValue)
            {
                await scheduler.StartDelayed(options.Value.StartDelay.Value, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                await scheduler.Start(cancellationToken).ConfigureAwait(false);
            }
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            if (schedulerWasStarted)
            {
                await scheduler.Shutdown(options.Value.WaitForJobsToComplete, cancellationToken).ConfigureAwait(false);
            }
        }
    }
}
