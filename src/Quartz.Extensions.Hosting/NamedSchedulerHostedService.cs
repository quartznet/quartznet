using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

#if NETCOREAPP3_1_OR_GREATER
using Lifetime = Microsoft.Extensions.Hosting.IHostApplicationLifetime;
#else
using Lifetime = Microsoft.Extensions.Hosting.IApplicationLifetime;
#endif

namespace Quartz;

/// <summary>
/// Hosted service that manages all named (non-default) Quartz schedulers registered via
/// <c>AddQuartz(string name, ...)</c>. If no named schedulers are registered, this service is a no-op.
/// </summary>
internal sealed class NamedSchedulerHostedService : IHostedService
{
    private readonly Lifetime applicationLifetime;
    private readonly IServiceProvider serviceProvider;
    private readonly IOptionsMonitor<QuartzOptions> optionsMonitor;
    private readonly IOptions<QuartzHostedServiceOptions> hostedServiceOptions;
    private readonly List<IScheduler> schedulers = new();
    private Task? startupTask;

    public NamedSchedulerHostedService(
        Lifetime applicationLifetime,
        IServiceProvider serviceProvider,
        IOptionsMonitor<QuartzOptions> optionsMonitor,
        IOptions<QuartzHostedServiceOptions> hostedServiceOptions)
    {
        this.applicationLifetime = applicationLifetime;
        this.serviceProvider = serviceProvider;
        this.optionsMonitor = optionsMonitor;
        this.hostedServiceOptions = hostedServiceOptions;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var registry = serviceProvider.GetService<SchedulerNameRegistry>();
        if (registry == null || registry.Names.Count == 0)
        {
            return;
        }

        try
        {
            // Create all named schedulers (requires successful initialization for app startup)
            foreach (var name in registry.Names)
            {
                var quartzOptions = optionsMonitor.Get(name);
                var factory = new NamedSchedulerFactory(serviceProvider, name, quartzOptions);
                var scheduler = await factory.CreateAndInitializeScheduler(cancellationToken).ConfigureAwait(false);
                schedulers.Add(scheduler);
            }

            if (hostedServiceOptions.Value.AwaitApplicationStarted)
            {
                startupTask = AwaitStartupCompletionAndStartSchedulersAsync(cancellationToken);

                if (startupTask.IsCompleted)
                {
                    await startupTask.ConfigureAwait(false);
                }
            }
            else
            {
                startupTask = StartSchedulersAsync(cancellationToken);
                await startupTask.ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // if the operation was canceled, we should not start the schedulers
        }
    }

    private async Task AwaitStartupCompletionAndStartSchedulersAsync(CancellationToken startupCancellationToken)
    {
        using var combinedCancellationSource = CancellationTokenSource.CreateLinkedTokenSource(startupCancellationToken, applicationLifetime.ApplicationStarted);

        await Task.Delay(Timeout.InfiniteTimeSpan, combinedCancellationSource.Token)
            .ContinueWith(_ => { }, TaskContinuationOptions.OnlyOnCanceled)
            .ConfigureAwait(false);

        if (!startupCancellationToken.IsCancellationRequested)
        {
            await StartSchedulersAsync(applicationLifetime.ApplicationStopping).ConfigureAwait(false);
        }
    }

    private async Task StartSchedulersAsync(CancellationToken cancellationToken)
    {
        if (applicationLifetime.ApplicationStopping.IsCancellationRequested)
        {
            return;
        }

        foreach (var scheduler in schedulers)
        {
            if (hostedServiceOptions.Value.StartDelay.HasValue)
            {
                await scheduler.StartDelayed(hostedServiceOptions.Value.StartDelay.Value, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                await scheduler.Start(cancellationToken).ConfigureAwait(false);
            }
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (schedulers.Count == 0 || startupTask is null)
        {
            return;
        }

        try
        {
            await Task.WhenAny(startupTask, Task.Delay(Timeout.Infinite, cancellationToken)).ConfigureAwait(false);
        }
        finally
        {
            foreach (var scheduler in schedulers)
            {
                await scheduler.Shutdown(hostedServiceOptions.Value.WaitForJobsToComplete, cancellationToken).ConfigureAwait(false);
            }
        }
    }
}
