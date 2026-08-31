#nullable enable

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

using Quartz.Configuration;
using Quartz.Core;
using Quartz.Diagnostics;
using Quartz.Extensibility;
using Quartz.Impl;

namespace Quartz.Tests.Unit.Diagnostics;

/// <summary>
/// Where the scheduler's own parts get their loggers from. The rule is one sentence — the container's
/// <see cref="ILoggerFactory" />, bridged to <see cref="LogProvider" /> when there is nothing behind it
/// — and these say what each half of it means.
/// </summary>
public sealed class InjectedLoggingTest
{
    [Test]
    public void TheSchedulersResourcesCarryTheContainersLoggerFactory()
    {
        ServiceCollection services = new();
        services.AddQuartz();

        using ServiceProvider provider = services.BuildServiceProvider();

        provider.GetRequiredService<QuartzSchedulerResources>().LoggerFactory
            .Should().BeSameAs(provider.GetRequiredService<ILoggerFactory>(),
                "a hosted application configures logging on its container and expects the scheduler to "
                + "use it, without having to know that a process-wide static exists");
    }

    /// <remarks>
    /// Asked of the resolver rather than of a scheduler built without a logger factory, because there is
    /// no such scheduler to build: <c>AddQuartz</c> calls <c>AddLogging</c>, and taking the factory back
    /// out leaves <c>ILogger&lt;T&gt;</c> unresolvable, so the graph fails before any component could
    /// fall back to anything. This is the rule the resources are filled in from.
    /// </remarks>
    [Test]
    public void AContainerWithNoLoggerFactoryFallsBackToTheAmbientOne()
    {
        ServiceCollection services = new();
        using ServiceProvider provider = services.BuildServiceProvider();

        provider.GetSchedulerLoggerFactory()
            .Should().BeOfType<LogProviderLoggerFactory>(
                "falling silent is the one answer a component that used to log has no business giving");
    }

    [Test]
    [NonParallelizable]
    public async Task AStandaloneSchedulerLogsThroughTheAmbientFactory()
    {
        RecordingLoggerProvider recorder = new();
        using ILoggerFactory ambient = new LoggerFactory();
        ambient.AddProvider(recorder);

        LogProvider.SetLogProvider(ambient);
        try
        {
            await using StandaloneSchedulerFactory factory = QuartzSchedulerBuilder
                .Create(q => q.ConfigureScheduler(options => options.InstanceName = "standalone"))
                .Build();

            await factory.GetScheduler();
        }
        finally
        {
            LogProvider.SetLogProvider(NullLoggerFactory.Instance);
        }

        recorder.Messages.Should().Contain("Quartz Scheduler created",
            "a console application says where its logging goes by setting the ambient factory, and the "
            + "container the builder made for it has no providers of its own to write to");
    }

    [Test]
    [NonParallelizable]
    public async Task AStandaloneSchedulerLogsThroughItsOwnContainerWhenLoggingIsRegisteredThere()
    {
        RecordingLoggerProvider registered = new();
        RecordingLoggerProvider ambient = new();
        using ILoggerFactory ambientFactory = new LoggerFactory();
        ambientFactory.AddProvider(ambient);

        LogProvider.SetLogProvider(ambientFactory);
        try
        {
            await using StandaloneSchedulerFactory factory = QuartzSchedulerBuilder
                .Create(q =>
                {
                    q.ConfigureScheduler(options => options.InstanceName = "standalone");
                    q.Services.AddLogging(logging => logging.AddProvider(registered));
                })
                .Build();

            await factory.GetScheduler();
        }
        finally
        {
            LogProvider.SetLogProvider(NullLoggerFactory.Instance);
        }

        registered.Messages.Should().Contain("Quartz Scheduler created",
            "a caller who registered a logging provider has said where logging goes");
        ambient.Messages.Should().NotContain("Quartz Scheduler created",
            "the bridge exists because the container had nowhere to write, so a container that does "
            + "must not have its lines duplicated into the ambient factory as well");
    }

    [Test]
    [NonParallelizable]
    public async Task EveryPartOfARunningSchedulerLogsThroughTheContainer()
    {
        RecordingLoggerProvider recorder = new();
        SignallingJob.Fired = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        ServiceCollection services = new();
        services.AddLogging(logging => logging.SetMinimumLevel(LogLevel.Trace).AddProvider(recorder));
        services.AddQuartz(quartz =>
        {
            // The job factory chain rather than the container-resolving one, because that is the arm
            // whose logging is worth seeing: it says which job it is about to build.
            quartz.UseJobFactory<PropertySettingJobFactory>();
            quartz.ScheduleJob<SignallingJob>(
                trigger => trigger.WithIdentity("now").StartNow(),
                job => job.WithIdentity("signalling"));
        });

        await using ServiceProvider provider = services.BuildServiceProvider();

        IScheduler scheduler = await provider.GetRequiredService<ISchedulerFactory>().GetScheduler();
        await scheduler.Start();
        try
        {
            await SignallingJob.Fired.Task.WaitAsync(TimeSpan.FromSeconds(30));
        }
        finally
        {
            await scheduler.Shutdown(waitForJobsToComplete: true);
        }

        recorder.Categories.Should().Contain(
            ["Quartz.Core.QuartzScheduler", "Quartz.Core.QuartzSchedulerThread", "Quartz.Core.SchedulerSignalerImpl", "Quartz.Impl.TaskSchedulingThreadPool", "Quartz.Impl.SimpleJobFactory"],
            "the scheduler, its loop, its signaler, its thread pool and its job factory are all built "
            + "by the container, so all five log to what the container was configured with");
    }

    /// <remarks>
    /// <see cref="ActivatorUtilities" /> picks the longest constructor it can satisfy, so a component
    /// whose only constructor took nothing has to grow one that takes a logger before the container can
    /// give it one — and a component that grew the wrong shape stops being constructible at all rather
    /// than quietly logging nowhere.
    /// </remarks>
    [Test]
    public void TheComponentsAContainerBuildsAllHaveAConstructorItCanCall()
    {
        ServiceCollection services = new();
        services.AddQuartz(quartz =>
        {
            quartz.UseThreadPool<ZeroSizeThreadPool>();
            quartz.UseInstanceIdGenerator<HostNameInstanceIdGenerator>();
            quartz.UseTypeLoader<SimpleTypeLoader>();
        });

        using ServiceProvider provider = services.BuildServiceProvider();

        provider.GetRequiredService<IThreadPool>().Should().BeOfType<ZeroSizeThreadPool>();
        provider.GetRequiredService<IInstanceIdGenerator>().Should().BeOfType<HostNameInstanceIdGenerator>();
        provider.GetRequiredService<ITypeLoader>().Should().BeOfType<SimpleTypeLoader>();
        provider.GetRequiredService<IJobFactory>().Should().BeOfType<MicrosoftDependencyInjectionJobFactory>(
            "the job factory derives from PropertySettingJobFactory, so it has to keep passing the "
            + "logger factory down two levels of base constructor");
    }

    /// <summary>
    /// Signals through a static because <see cref="PropertySettingJobFactory" /> builds a job from its
    /// parameterless constructor and has nowhere to inject one from.
    /// </summary>
    public sealed class SignallingJob : IJob
    {
        internal static TaskCompletionSource Fired = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
        {
            Fired.TrySetResult();
            return default;
        }
    }

    /// <summary>
    /// Records the rendered message of every line, whatever the category.
    /// </summary>
    private sealed class RecordingLoggerProvider : ILoggerProvider
    {
        private readonly Lock gate = new();
        private readonly List<string> messages = [];
        private readonly HashSet<string> categories = new(StringComparer.Ordinal);

        public IReadOnlyList<string> Messages
        {
            get
            {
                lock (gate)
                {
                    return [.. messages];
                }
            }
        }

        /// <summary>
        /// Which categories wrote anything, which is how "this type logs through the container" is
        /// observable without pinning the words a type happens to log.
        /// </summary>
        public IReadOnlyCollection<string> Categories
        {
            get
            {
                lock (gate)
                {
                    return [.. categories];
                }
            }
        }

        public ILogger CreateLogger(string categoryName) => new RecordingLogger(this, categoryName);

        public void Dispose()
        {
        }

        private void Record(string category, string message)
        {
            lock (gate)
            {
                messages.Add(message);
                categories.Add(category);
            }
        }

        private sealed class RecordingLogger(RecordingLoggerProvider provider, string category) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                provider.Record(category, formatter(state, exception));
            }
        }
    }
}
