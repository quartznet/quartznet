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
        SchedulerNameRegistry? registry = serviceProvider.GetService<SchedulerNameRegistry>();
        if (registry == null || registry.Names.Count == 0)
        {
            return;
        }

        try
        {
            // Create all named schedulers (requires successful initialization for app startup)
            foreach (string name in registry.Names)
            {
                QuartzOptions quartzOptions = optionsMonitor.Get(name);
                NamedSchedulerFactory factory = new NamedSchedulerFactory(serviceProvider, name, quartzOptions);
                IScheduler scheduler = await factory.CreateAndInitializeScheduler(cancellationToken).ConfigureAwait(false);
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
        catch (Exception)
        {
            // If creation fails partway through, shut down any schedulers that were already created
            await ShutdownAllSchedulersAsync(CancellationToken.None).ConfigureAwait(false);
            throw;
        }
    }

    private async Task AwaitStartupCompletionAndStartSchedulersAsync(CancellationToken startupCancellationToken)
    {
        using CancellationTokenSource combinedCancellationSource = CancellationTokenSource.CreateLinkedTokenSource(startupCancellationToken, applicationLifetime.ApplicationStarted);

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

        foreach (IScheduler scheduler in schedulers)
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
            await ShutdownAllSchedulersAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task ShutdownAllSchedulersAsync(CancellationToken cancellationToken)
    {
        List<Exception>? exceptions = null;
        foreach (IScheduler scheduler in schedulers)
        {
            try
            {
                await scheduler.Shutdown(hostedServiceOptions.Value.WaitForJobsToComplete, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                exceptions ??= new List<Exception>();
                exceptions.Add(ex);
            }
        }

        schedulers.Clear();

        if (exceptions is { Count: > 0 })
        {
            throw new AggregateException("One or more named scheduler shutdowns failed.", exceptions);
        }
    }
}
