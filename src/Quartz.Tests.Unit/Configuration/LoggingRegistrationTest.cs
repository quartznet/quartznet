#nullable enable

using System.Collections.Concurrent;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

using Quartz.Diagnostics;
using Quartz.Extensibility;

namespace Quartz.Tests.Unit.Configuration;

/// <summary>
/// Where a Quartz log line ends up, for every order the two calls that decide it can be written in.
/// </summary>
/// <remarks>
/// <para>
/// Quartz registers <see cref="ILogger{T}" /> and deliberately no <see cref="ILoggerFactory" />
/// (#3730). The reason is the whole of this file: every registration in this area is a <c>TryAdd</c>,
/// so the first one wins, and a factory registered by <c>AddQuartz</c> would beat the
/// <c>services.AddLogging(b =&gt; b.AddConsole())</c> an application writes on the next line — the
/// providers would be registered, never consumed, and nothing would say so. An application that
/// configured logging must reach Quartz's lines whichever side of <c>AddQuartz</c> it configured them
/// on, and one that configured none must still get a scheduler.
/// </para>
/// <para>
/// Seven arrangements, which is every way the two decisions combine that behaves differently: no
/// logging at all, either order in one container, a host that configured it before user code ran, the
/// standalone builder's ambient bridge, an application that registered a factory instance rather than
/// calling <c>AddLogging</c>, and a scheduler added to a container after it was built.
/// </para>
/// </remarks>
public sealed class LoggingRegistrationTest
{
    private const string SchedulerCategory = "Quartz.Core.QuartzScheduler";

    private readonly List<IScheduler> started = [];

    [TearDown]
    public async Task ShutDownWhateverStarted()
    {
        foreach (IScheduler scheduler in started)
        {
            try
            {
                await scheduler.Shutdown();
            }
            catch (SchedulerException)
            {
                // Already shut down by the test itself.
            }
        }

        started.Clear();
    }

    /// <summary>
    /// (i) Nothing but <c>AddQuartz</c>.
    /// </summary>
    [Test]
    public async Task AContainerToldNothingAboutLoggingStillRunsAScheduler()
    {
        SignallingJob.Fired = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        ServiceCollection services = new();
        services.AddQuartz(quartz => quartz.ScheduleJob<SignallingJob>(
            trigger => trigger.WithIdentity("now").StartNow(),
            job => job.WithIdentity("signalling")));

        await using ServiceProvider provider = services.BuildServiceProvider();

        provider.GetService<ILoggerFactory>().Should().BeNull(
            "Quartz registers no logger factory, which is the whole reason an application's later "
            + "AddLogging is still the one that decides where lines go");

        IScheduler scheduler = await Start(provider);

        await SignallingJob.Fired.Task.WaitAsync(TimeSpan.FromSeconds(30));

        (await scheduler.GetStatus()).Should().Be(SchedulerStatus.Running,
            "a container that was never told where logging goes is still a container a scheduler builds "
            + "and runs out of - nothing about logging may be a precondition for scheduling");
    }

    /// <summary>
    /// (i, continued) A component of your own, built by Quartz in that same container, still gets
    /// everything its constructor asks for — an <see cref="ILoggerFactory" /> included.
    /// </summary>
    /// <remarks>
    /// It has always been satisfiable, because <c>AddQuartz</c> used to call <c>AddLogging()</c>. Now
    /// the factory comes from the provider Quartz activates through rather than from a registration, so
    /// this holds the two together: the ambient bridge arrives, and the keyed parameter beside it still
    /// resolves the way the container would have resolved it.
    /// </remarks>
    [Test]
    public void AComponentBuiltInThatContainerStillGetsAnILoggerFactory()
    {
        ServiceCollection services = new();
        services.AddKeyedSingleton("audit", new AuditSink());
        services.AddQuartz(quartz => quartz.UseInstanceIdGenerator<DemandingInstanceIdGenerator>());

        using ServiceProvider provider = services.BuildServiceProvider();

        DemandingInstanceIdGenerator generator = provider.GetRequiredService<IInstanceIdGenerator>()
            .Should().BeOfType<DemandingInstanceIdGenerator>().Subject;

        generator.LoggerFactory.Should().BeOfType<LogProviderLoggerFactory>(
            "'you configured no logging, so your component cannot be built' is not an answer, and the "
            + "ambient bridge is where the rest of Quartz already writes when nothing was configured");
        generator.Sink.Should().NotBeNull(
            "and the provider Quartz activates through has to stay an ordinary one for everything else, "
            + "keyed constructor parameters included");
    }

    /// <summary>
    /// (ii) <c>AddQuartz</c> first, then the application's logging. The case the ruling protects: a
    /// factory registered by Quartz would have won this one, and the provider below would never be read.
    /// </summary>
    [Test]
    public async Task LoggingConfiguredAfterAddQuartzStillReceivesQuartzsLines()
    {
        RecordingLoggerProvider recorder = new();

        ServiceCollection services = new();
        services.AddQuartz();
        services.AddLogging(logging => logging.AddProvider(recorder));

        await using ServiceProvider provider = services.BuildServiceProvider();
        await Start(provider);

        recorder.Categories.Should().Contain(SchedulerCategory,
            "AddLogging registers its factory with TryAdd, so anything Quartz registered for that "
            + "service would have silently taken the application's providers out of the picture");
    }

    /// <summary>
    /// (iii) The other order, which is the one every sample writes.
    /// </summary>
    [Test]
    public async Task LoggingConfiguredBeforeAddQuartzReceivesQuartzsLines()
    {
        RecordingLoggerProvider recorder = new();

        ServiceCollection services = new();
        services.AddLogging(logging => logging.AddProvider(recorder));
        services.AddQuartz();

        await using ServiceProvider provider = services.BuildServiceProvider();
        await Start(provider);

        recorder.Categories.Should().Contain(SchedulerCategory,
            "the two orders are the same arrangement written twice, and a difference between them "
            + "would be a difference nobody could see until production");
    }

    /// <summary>
    /// (iv) A host, which configures logging for itself before any application code runs.
    /// </summary>
    [Test]
    public async Task AHostsOwnLoggingReceivesQuartzsLines()
    {
        RecordingLoggerProvider recorder = new();

        HostApplicationBuilder builder = Host.CreateApplicationBuilder();
        builder.Logging.AddProvider(recorder);
        builder.Services.AddQuartz();

        using IHost host = builder.Build();
        await Start(host.Services);

        recorder.Categories.Should().Contain(SchedulerCategory,
            "a hosted application never calls AddLogging itself, and the host's Logger<T> registration "
            + "is what wins the open generic here rather than Quartz's");
    }

    /// <summary>
    /// (v) The standalone builder, whose container has no providers of its own and forwards to
    /// <see cref="LogProvider" />.
    /// </summary>
    [Test]
    [NonParallelizable]
    public async Task AStandaloneSchedulerStillLogsThroughTheAmbientFactory()
    {
        RecordingLoggerProvider recorder = new();
        using ILoggerFactory ambient = new LoggerFactory();
        ambient.AddProvider(recorder);

        LogProvider.SetLogProvider(ambient);
        try
        {
            await using StandaloneSchedulerFactory factory = QuartzSchedulerBuilder
                .Create(quartz => quartz.ConfigureScheduler(options => options.InstanceName = "ambient-bridge"))
                .Build();

            started.Add(await factory.GetScheduler());

            recorder.Categories.Should().Contain(SchedulerCategory,
                "the bridge is how a console application says where its logging goes, and dropping the "
                + "logging package must not have taken it with it");
        }
        finally
        {
            LogProvider.SetLogProvider(NullLoggerFactory.Instance);
        }
    }

    /// <summary>
    /// (v, continued) A provider registered on the standalone builder without a factory to read it.
    /// </summary>
    /// <remarks>
    /// It used to work, because <c>AddQuartz</c> called <c>AddLogging()</c> and so there was always a
    /// factory to consume the provider. There is not any more, and the two answers left are to write the
    /// caller's lines somewhere they did not ask for or to say so — so it says so.
    /// </remarks>
    [Test]
    public void AStandaloneBuilderRefusesAProviderWithNoFactoryToReadIt()
    {
        Action build = () => QuartzSchedulerBuilder
            .Create(quartz => quartz.Services.AddSingleton<ILoggerProvider>(new RecordingLoggerProvider()))
            .Build();

        build.Should().Throw<SchedulerConfigException>()
            .WithMessage("*AddLogging*",
                "the message has to name the one-line fix, because the mistake is invisible otherwise: "
                + "the scheduler runs, and the lines the caller asked for go to the ambient bridge");
    }

    /// <summary>
    /// (vi) An application that registered a factory of its own rather than calling <c>AddLogging</c>,
    /// which is what a container assembled by hand around an existing factory looks like.
    /// </summary>
    [Test]
    public async Task AnApplicationsOwnLoggerFactoryIsTheOneQuartzUses()
    {
        RecordingLoggerProvider recorder = new();
        using ILoggerFactory application = LoggerFactory.Create(logging => logging.AddProvider(recorder));

        ServiceCollection services = new();
        services.AddSingleton(application);
        services.AddQuartz();

        await using ServiceProvider provider = services.BuildServiceProvider();
        await Start(provider);

        recorder.Categories.Should().Contain(SchedulerCategory,
            "there is one question - what ILoggerFactory does this container hold - and Quartz asks it "
            + "rather than caring how the answer got there");
    }

    /// <summary>
    /// (vii) A scheduler added to a container that has already been built, which registers its own
    /// object graph in a container of its own and is handed the application's factory as an instance.
    /// </summary>
    [Test]
    public async Task ASchedulerAddedAtRuntimeLogsThroughTheApplicationsFactory()
    {
        RecordingLoggerProvider recorder = new();

        ServiceCollection services = new();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddLogging(logging => logging.AddProvider(recorder));
        services.AddQuartz(quartz => quartz.ConfigureScheduler(options => options.InstanceName = "landlord"));

        await using ServiceProvider provider = services.BuildServiceProvider();

        IScheduler tenant = await provider.GetRequiredService<ISchedulerRuntime>().Add("tenant", _ => { });
        started.Add(tenant);

        recorder.Messages.Should().Contain(message => message.Contains("Scheduler tenant_$_", StringComparison.Ordinal),
            "a tenant's container registers Quartz's shared services all over again, and the one thing "
            + "it must not register a second copy of is where log lines go");
    }

    private async Task<IScheduler> Start(IServiceProvider provider)
    {
        IScheduler scheduler = await provider.GetRequiredService<ISchedulerFactory>().GetScheduler();
        started.Add(scheduler);

        await scheduler.Start();
        return scheduler;
    }

    /// <summary>
    /// Something for a keyed constructor parameter to be, so that the component below asks the
    /// activating provider for two different kinds of thing at once.
    /// </summary>
    private sealed class AuditSink;

    /// <summary>
    /// A component of the shape an application writes: it takes a logger factory, and something else
    /// the container holds under a key.
    /// </summary>
    private sealed class DemandingInstanceIdGenerator : IInstanceIdGenerator
    {
        public DemandingInstanceIdGenerator(
            ILoggerFactory loggerFactory,
            [FromKeyedServices("audit")] AuditSink sink)
        {
            LoggerFactory = loggerFactory;
            Sink = sink;
        }

        public ILoggerFactory LoggerFactory { get; }

        public AuditSink Sink { get; }

        public ValueTask<string> GenerateInstanceId(CancellationToken cancellationToken = default) => new("demanding");
    }

    /// <summary>
    /// Signals through a static because the job factory builds it from its parameterless constructor.
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
    /// Records the category and rendered message of every line, which is how "this reached the
    /// application's providers" is observable without pinning the words a type happens to log.
    /// </summary>
    private sealed class RecordingLoggerProvider : ILoggerProvider
    {
        private readonly ConcurrentQueue<(string Category, string Message)> entries = new();

        public IReadOnlyCollection<string> Categories => [.. entries.Select(x => x.Category)];

        public IReadOnlyCollection<string> Messages => [.. entries.Select(x => x.Message)];

        public ILogger CreateLogger(string categoryName) => new RecordingLogger(this, categoryName);

        public void Dispose()
        {
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
                provider.entries.Enqueue((category, formatter(state, exception)));
            }
        }
    }
}
