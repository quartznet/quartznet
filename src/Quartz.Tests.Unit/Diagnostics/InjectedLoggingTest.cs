#nullable enable

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

using Quartz.Configuration;
using Quartz.Core;
using Quartz.Diagnostics;

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
            await using StandaloneSchedulerFactory factory = QuartzSchedulerBuilder.Create()
                .ConfigureScheduler(options => options.InstanceName = "standalone")
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
            QuartzSchedulerBuilder builder = QuartzSchedulerBuilder.Create()
                .ConfigureScheduler(options => options.InstanceName = "standalone");
            builder.Services.AddLogging(logging => logging.AddProvider(registered));

            await using StandaloneSchedulerFactory factory = builder.Build();

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

    /// <summary>
    /// Records the rendered message of every line, whatever the category.
    /// </summary>
    private sealed class RecordingLoggerProvider : ILoggerProvider
    {
        private readonly Lock gate = new();
        private readonly List<string> messages = [];

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

        public ILogger CreateLogger(string categoryName) => new RecordingLogger(this);

        public void Dispose()
        {
        }

        private void Record(string message)
        {
            lock (gate)
            {
                messages.Add(message);
            }
        }

        private sealed class RecordingLogger(RecordingLoggerProvider provider) : ILogger
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
                provider.Record(formatter(state, exception));
            }
        }
    }
}
