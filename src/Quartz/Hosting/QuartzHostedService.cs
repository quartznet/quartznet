using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

using Quartz.Configuration;
using Quartz.Util;

using Lifetime = Microsoft.Extensions.Hosting.IHostApplicationLifetime;

namespace Quartz;

/// <summary>
/// Runs the schedulers registered in the container for as long as the application runs.
/// </summary>
/// <remarks>
/// <para>
/// Every scheduler in the container is started, the default one and each named one, and each is
/// configured by <see cref="QuartzHostedServiceOptions"/> under its own name — so one scheduler can
/// wait for application startup while another starts immediately, and one whose
/// <see cref="QuartzHostedServiceOptions.AutoStart"/> is <see langword="false"/> is created and bound
/// but left for the application to start.
/// </para>
/// <para>
/// The schedulers are resolved when the host starts rather than when the service is registered, so
/// <c>AddQuartzHostedService</c> and <c>AddQuartz</c> can be called in either order. Registering the
/// hosted service first used to leave the default scheduler unstarted and say nothing about it.
/// </para>
/// <para>
/// Deriving from this and registering the derived type with <c>AddQuartzHostedService&lt;T&gt;</c> is a
/// supported extension point, but only through the four lifecycle hooks —
/// <see cref="StartingAsync"/>, <see cref="StartedAsync"/>, <see cref="StoppingAsync"/> and
/// <see cref="StoppedAsync"/> — which exist for nothing else.
/// <see cref="StartAsync"/> and <see cref="StopAsync"/> are not overridable: they maintain state a
/// subclass cannot see, and an override that did not call base left the schedulers bound to the
/// repository with nothing left to shut them down. What a subclass would have overridden them for is
/// reading the running schedulers, and that is <see cref="Schedulers"/>.
/// </para>
/// </remarks>
public class QuartzHostedService : IHostedLifecycleService
{
    private readonly Lifetime applicationLifetime;
    private readonly IServiceProvider serviceProvider;
    private readonly IOptionsMonitor<QuartzHostedServiceOptions> options;
    private List<HostedScheduler> schedulers = [];
    private readonly Lock stopGate = new();
    private Task? stopTask;
    internal Task? startupTask;

    /// <summary>
    /// Constructed by the container; an application registers this service with
    /// <c>AddQuartzHostedService</c> rather than building one.
    /// </summary>
    /// <param name="applicationLifetime">The host's lifetime, which <c>AwaitApplicationStarted</c> waits on.</param>
    /// <param name="serviceProvider">The container the schedulers are resolved from.</param>
    /// <param name="options">One <see cref="QuartzHostedServiceOptions" /> per scheduler name.</param>
    public QuartzHostedService(
        Lifetime applicationLifetime,
        IServiceProvider serviceProvider,
        IOptionsMonitor<QuartzHostedServiceOptions> options)
    {
        this.applicationLifetime = applicationLifetime;
        this.serviceProvider = serviceProvider;
        this.options = options;
    }

    /// <summary>
    /// The schedulers this service is running, from the moment they are resolved until they are shut
    /// down.
    /// </summary>
    /// <remarks>
    /// A snapshot rather than a live view, so a hook cannot be handed a list that empties underneath it.
    /// Empty before <see cref="StartAsync"/> has resolved them and after <see cref="StopAsync"/> has shut
    /// them down, which makes <see cref="StartedAsync"/> and <see cref="StoppingAsync"/> the two hooks it
    /// is worth reading from.
    /// </remarks>
    protected IReadOnlyList<IScheduler> Schedulers => Volatile.Read(ref schedulers).ConvertAll(static hosted => hosted.Scheduler);

    /// <summary>
    /// Runs before any scheduler has been resolved. Does nothing; override it to do something.
    /// </summary>
    /// <param name="cancellationToken">The host's start token.</param>
    public virtual Task StartingAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    /// <summary>
    /// Resolves every scheduler the container holds, binds it to the repository and starts it as its
    /// options say.
    /// </summary>
    /// <remarks>
    /// Not overridable: it maintains state a subclass cannot see, and an override that did not call
    /// base left the schedulers bound with nothing to shut them down. The four hooks are the extension
    /// point.
    /// </remarks>
    /// <param name="cancellationToken">The host's start token.</param>
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            // Require successful initialization for application startup to succeed
            await CreateSchedulers(cancellationToken).ConfigureAwait(false);

            // Sensible mode: proceed with startup, and have jobs start after application startup.
            // Follow the pattern from BackgroundService.StartAsync: https://github.com/dotnet/runtime/blob/main/src/libraries/Microsoft.Extensions.Hosting.Abstractions/src/BackgroundService.cs
            // A scheduler the application starts itself is not waited for: it is nothing this service
            // will start once startup completes, so it must not be what keeps a startup task alive.
            if (schedulers.Exists(static scheduler => scheduler.Options is { AutoStart: true, AwaitApplicationStarted: true }))
            {
                startupTask = AwaitStartupCompletionAndStartSchedulers(cancellationToken);

                // If the task completed synchronously, await it in order to bubble potential cancellation/failure to the caller
                // Otherwise, return, allowing application startup to complete
                if (startupTask.IsCompleted)
                {
                    await startupTask.ConfigureAwait(false);
                }
            }
            else // Legacy mode: start jobs inline
            {
                startupTask = StartSchedulers(waitForApplicationStarted: false, cancellationToken);
                await startupTask.ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // if the operation was canceled, we should not start the scheduler
        }
        catch (Exception)
        {
            // A scheduler created before the failure is already bound to the repository, so it has to be
            // shut down rather than left behind by a host that will never call StopAsync.
            await ShutdownSchedulers(CancellationToken.None).ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>
    /// Runs once every scheduler has started. Does nothing; override it to do something —
    /// <see cref="Schedulers" /> is what it is for.
    /// </summary>
    /// <param name="cancellationToken">The host's start token.</param>
    public virtual Task StartedAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    /// <summary>
    /// Resolves every scheduler the container holds: the default one, and one per named registration.
    /// </summary>
    /// <remarks>
    /// A container with no scheduler at all is a mistake worth reporting — the hosted service was asked
    /// for, so something was meant to run.
    /// </remarks>
    private async ValueTask CreateSchedulers(CancellationToken cancellationToken)
    {
        var factory = serviceProvider.GetService<ISchedulerFactory>();
        if (factory is not null)
        {
            var scheduler = await factory.GetScheduler(cancellationToken).ConfigureAwait(false);
            schedulers.Add(new HostedScheduler(scheduler, options.Get(Options.DefaultName)));
        }

        var registry = serviceProvider.GetService<SchedulerNameRegistry>();
        foreach (var name in registry?.Names ?? [])
        {
            // Each named scheduler's factory is registered under the scheduler's name as the service key.
            var named = serviceProvider.GetRequiredKeyedService<ISchedulerFactory>(name);
            var scheduler = await named.GetScheduler(cancellationToken).ConfigureAwait(false);
            schedulers.Add(new HostedScheduler(scheduler, options.Get(name)));
        }

        if (schedulers.Count == 0)
        {
            Throw.SchedulerConfigException(
                "AddQuartzHostedService() was called but no scheduler is registered in the container, so "
                + "there is nothing to start. Call AddQuartz(...) to register one.");
        }
    }

    private async Task AwaitStartupCompletionAndStartSchedulers(CancellationToken startupCancellationToken)
    {
        // The schedulers that were told not to wait start before anything is awaited.
        await StartSchedulers(waitForApplicationStarted: false, startupCancellationToken).ConfigureAwait(false);

        using var combinedCancellationSource = CancellationTokenSource.CreateLinkedTokenSource(startupCancellationToken, applicationLifetime.ApplicationStarted);

        await Task.Delay(Timeout.InfiniteTimeSpan, combinedCancellationSource.Token) // Wait "indefinitely", until startup completes or is aborted
            .ContinueWith(_ => { }, CancellationToken.None, TaskContinuationOptions.OnlyOnCanceled, TaskScheduler.Default) // Without an OperationCanceledException on cancellation
            .ConfigureAwait(false);

        if (!startupCancellationToken.IsCancellationRequested)
        {
            // Startup has finished, but ApplicationStopping may still interrupt starting of the scheduler
            await StartSchedulers(waitForApplicationStarted: true, applicationLifetime.ApplicationStopping).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Starts the schedulers of one persuasion, either immediately or after the delay each was configured
    /// with.
    /// </summary>
    private async Task StartSchedulers(bool waitForApplicationStarted, CancellationToken cancellationToken)
    {
        // Avoid potential race conditions between ourselves and StopAsync, in case it has already made its attempt to stop the scheduler
        if (applicationLifetime.ApplicationStopping.IsCancellationRequested)
        {
            return;
        }

        foreach (var hosted in schedulers)
        {
            // Tested before the two settings that say *when* to start, because AutoStart says whether
            // to at all: the scheduler was created and bound by CreateSchedulers, and that is all this
            // service does for it. The application presses start.
            if (!hosted.Options.AutoStart)
            {
                continue;
            }

            if (hosted.Options.AwaitApplicationStarted != waitForApplicationStarted)
            {
                continue;
            }

            if (hosted.Options.StartDelay.HasValue)
            {
                await hosted.Scheduler.StartDelayed(hosted.Options.StartDelay.Value, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                await hosted.Scheduler.Start(cancellationToken).ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// Runs before the schedulers are shut down, while <see cref="Schedulers" /> still lists them.
    /// Does nothing; override it to do something.
    /// </summary>
    /// <param name="cancellationToken">The host's shutdown token.</param>
    public virtual Task StoppingAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    /// <summary>
    /// Waits for whatever start-up is still running and shuts every scheduler down, unbinding it from
    /// the repository.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Not overridable, for the reason <see cref="StartAsync" /> is not.
    /// </para>
    /// <para>
    /// Safe to call more than once, and safe to call concurrently, because the generic host does both:
    /// <c>StopAsync</c> while <c>RunAsync</c> is pending raises <c>ApplicationStopping</c>,
    /// <c>WaitForShutdownAsync</c> wakes on it and stops the host again, and the two stops reach every
    /// hosted service at once. Every call after the first joins the stop already under way and observes
    /// its outcome — the same completion, and the same failure if a shutdown threw.
    /// </para>
    /// </remarks>
    /// <param name="cancellationToken">The host's shutdown token.</param>
    public Task StopAsync(CancellationToken cancellationToken)
    {
        Task stop;
        lock (stopGate)
        {
            // The second caller's token is deliberately ignored: there is one stop, it runs under the
            // token of whichever call started it, and a later call waits for that rather than starting
            // a second shutdown of schedulers the first one is already tearing down.
            stop = stopTask ??= StopCore(cancellationToken);
        }

        return stop;
    }

    /// <summary>
    /// The stop itself, run exactly once however many callers ask for it.
    /// </summary>
    private async Task StopCore(CancellationToken cancellationToken)
    {
        // Stopped without having been started
        if (Volatile.Read(ref schedulers).Count == 0)
        {
            return;
        }

        try
        {
            // Wait until any ongoing startup logic has finished or the graceful shutdown period is over
            if (startupTask is not null)
            {
                await Task.WhenAny(startupTask, Task.Delay(Timeout.Infinite, cancellationToken)).ConfigureAwait(false);
            }
        }
        finally
        {
            // we always need to call shutdown to ensure that we unbind the scheduler from global repository
            await ShutdownSchedulers(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Runs once every scheduler has been shut down. Does nothing; override it to do something.
    /// </summary>
    /// <param name="cancellationToken">The host's shutdown token.</param>
    public virtual Task StoppedAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    /// <summary>
    /// Shuts every scheduler down, reporting all the failures rather than the first one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The shutdowns run concurrently, because the host's shutdown budget is one deadline for all of
    /// them rather than one each: shutting down in turn made the host's stop time the sum of the waits
    /// for running jobs, which is what overran <c>HostOptions.ShutdownTimeout</c> with more than
    /// one scheduler registered. Each scheduler owns its own thread pool, job store and scheduler
    /// thread, so there is nothing for them to serialize behind.
    /// </para>
    /// <para>
    /// The list is taken rather than walked in place: emptying it afterwards meant a caller was
    /// enumerating a list somebody else was clearing, which is the
    /// <see cref="InvalidOperationException" /> #3701 reported out of <c>RunAsync</c>.
    /// </para>
    /// </remarks>
    private async ValueTask ShutdownSchedulers(CancellationToken cancellationToken)
    {
        List<HostedScheduler> toShutDown = Interlocked.Exchange(ref schedulers, []);

        // Every shutdown is started before any of them is awaited, so that the waits for running jobs
        // overlap. A scheduler that throws before it yields is captured rather than left to abandon the
        // schedulers after it in the list.
        List<Task> shutdowns = new List<Task>(toShutDown.Count);
        foreach (HostedScheduler hosted in toShutDown)
        {
            try
            {
                shutdowns.Add(hosted.Scheduler.Shutdown(hosted.Options.WaitForJobsToComplete, cancellationToken).AsTask());
            }
            catch (Exception e)
            {
                shutdowns.Add(Task.FromException(e));
            }
        }

        List<Exception>? exceptions = null;
        foreach (Task shutdown in shutdowns)
        {
            try
            {
                await shutdown.ConfigureAwait(false);
            }
            catch (Exception e)
            {
                exceptions ??= [];
                exceptions.Add(e);
            }
        }

        if (exceptions is { Count: > 0 })
        {
            throw new AggregateException("One or more scheduler shutdowns failed.", exceptions);
        }
    }

    /// <summary>
    /// A scheduler this service runs, together with the options registered under its name.
    /// </summary>
    private readonly record struct HostedScheduler(IScheduler Scheduler, QuartzHostedServiceOptions Options);
}
