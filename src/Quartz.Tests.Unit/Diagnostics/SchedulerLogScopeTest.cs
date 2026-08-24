#nullable enable

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

using Quartz.Core;
using Quartz.Diagnostics;
using Quartz.Extensibility;
using Quartz.Impl;
using Quartz.Tests.Unit.Core;

namespace Quartz.Tests.Unit.Diagnostics;

/// <summary>
/// An <see cref="ILogger" /> category is a type name, so two schedulers in one process write log lines
/// that are identical in everything a log query can filter on, except where a message template happens
/// to carry the scheduler's name. A logging scope carries it on every line instead.
/// </summary>
/// <remarks>
/// The scope is opened once, for the lifetime of the scheduler thread's loop, and not per firing: a
/// <c>BeginScope</c> per firing costs an <see cref="AsyncLocal{T}" /> write, which copies the execution
/// context, and measured at 240 bytes and 14% of a no-op firing. A job inherits the loop's scope through
/// the execution context the thread pool's dispatch captures, which costs nothing per firing — and
/// reaches the job only when the loop's own logging goes somewhere, which means when the application has
/// called <see cref="LogProvider.SetLogProvider" />. That is pinned below, so the day the loop is handed
/// an injected logger instead, this is what says the reach came with it.
/// </remarks>
public sealed class SchedulerLogScopeTest
{
    private static readonly TimeSpan observationDeadline = TimeSpan.FromSeconds(30);

    [Test]
    [NonParallelizable]
    public async Task AJobInheritsTheSchedulersScopeFromTheThreadThatDispatchedIt()
    {
        ScopeCapturingLoggerProvider recorder = new();
        TaskCompletionSource fired = new(TaskCreationOptions.RunContinuationsAsynchronously);

        ServiceCollection services = new();
        services.AddLogging(builder => builder.AddProvider(recorder));
        services.AddSingleton(fired);

        services.AddQuartz("acme", q => q.ScheduleJob<LoggingJob>(
            trigger => trigger.WithIdentity("now").StartNow(),
            job => job.WithIdentity("logging")));

        await using ServiceProvider provider = services.BuildServiceProvider();

        // The scheduler thread logs through the ambient factory, so this is what makes its scope real
        // rather than NullLogger's no-op — and the scope is what the dispatch then carries to the job.
        LogProvider.SetLogProvider(provider.GetRequiredService<ILoggerFactory>());
        try
        {
            IScheduler scheduler = await provider.GetRequiredKeyedService<ISchedulerFactory>("acme").GetScheduler();
            await scheduler.Start();
            try
            {
                await fired.Task.WaitAsync(observationDeadline);
            }
            finally
            {
                await scheduler.Shutdown(waitForJobsToComplete: true);
            }
        }
        finally
        {
            LogProvider.SetLogProvider(NullLoggerFactory.Instance);
        }

        CapturedEntry entry = recorder.Entries
            .Should().ContainSingle(x => x.Message == LoggingJob.Message,
                "the job logs exactly once, with a logger the container gave it")
            .Subject;

        entry.Scopes.Should().Contain(new KeyValuePair<string, object?>(ActivityTags.SchedulerName, "acme"),
            "a job's log line says nothing about which tenant it ran for unless the firing puts it there");
        entry.Scopes.Should().Contain(x => x.Key == ActivityTags.SchedulerId,
            "the attribute names are the ones the spans and the measurements use, so one query spans all three");
    }

    [Test]
    [NonParallelizable]
    public async Task TheSchedulerThreadsOwnLogLinesNameTheSchedulerToo()
    {
        ScopeCapturingLoggerProvider recorder = new();
        using ILoggerFactory factory = new LoggerFactory();
        factory.AddProvider(recorder);

        // The scheduler thread logs through the ambient factory rather than an injected logger, so this
        // is what makes its lines observable at all — the same reason an application that wants to see
        // them calls it.
        LogProvider.SetLogProvider(factory);

        FaultInjectingJobStore store = new();
        await store.Initialize(TestJobStores.Identity());

        QuartzSchedulerResources resources = new()
        {
            Name = "acme",
            InstanceId = "acme-1",
            IdleWaitTime = TimeSpan.FromSeconds(1),
            MaxBatchSize = 1,
            JobStore = store,
            ThreadPool = new DefaultThreadPool { MaxConcurrency = 1 },
            JobRunShellFactory = new StdJobRunShellFactory(NullLogger<JobRunShell>.Instance),
        };

        await resources.ThreadPool.Initialize();

        QuartzScheduler scheduler = new(resources);
        QuartzSchedulerThread thread = new(scheduler, resources);

        try
        {
            // Anything that is not a persistence problem is logged by the loop rather than reported to
            // scheduler listeners, which is the arm that writes a line of its own.
            store.OnAcquireNextTriggers = static (_, _, _) => throw new InvalidOperationException("the database is gone");

            thread.Start();
            thread.TogglePause(pause: false);

            await store.Acquisitions.Reaches(1).WaitAsync(observationDeadline);

            CapturedEntry entry = await Eventually(
                () => recorder.Entries.FirstOrDefault(x => x.Message.Contains("the database is gone", StringComparison.Ordinal)),
                "the loop reports the first failure of a run");

            entry.Scopes.Should().Contain(new KeyValuePair<string, object?>(ActivityTags.SchedulerName, "acme"),
                "an acquisition failure that does not say which scheduler could not reach its store is "
                + "the same line from either tenant");
            entry.Scopes.Should().Contain(new KeyValuePair<string, object?>(ActivityTags.SchedulerId, "acme-1"));
        }
        finally
        {
            await thread.Halt(wait: true);
            await thread.Shutdown();
            await store.Shutdown();
            LogProvider.SetLogProvider(NullLoggerFactory.Instance);
        }
    }

    [Test]
    public void TheScopeCarriesTheSchedulerUnderTheAttributeNamesTheTracesUse()
    {
        SchedulerLogScope scope = new("acme", "acme-1");

        scope.Should().Equal(
            [new KeyValuePair<string, object?>(ActivityTags.SchedulerName, "acme"),
             new KeyValuePair<string, object?>(ActivityTags.SchedulerId, "acme-1")]);

        scope.ToString().Should().Be("quartz.scheduler.name:acme quartz.scheduler.id:acme-1",
            "a provider that renders a scope as text asks for this once per line, so it is built once");
    }

    /// <summary>
    /// Waits for a log line to arrive. A line is written by the loop rather than returned to the test,
    /// so there is nothing to await for it directly; the deadline is a way of failing instead of hanging.
    /// </summary>
    private static async Task<CapturedEntry> Eventually(Func<CapturedEntry?> read, string because)
    {
        DateTime deadline = DateTime.UtcNow + observationDeadline;
        while (DateTime.UtcNow < deadline)
        {
            CapturedEntry? entry = read();
            if (entry is not null)
            {
                return entry;
            }

            await Task.Delay(20);
        }

        read().Should().NotBeNull(because);
        return read()!;
    }

    public sealed class LoggingJob : IJob
    {
        internal const string Message = "the job wrote this";

        private readonly ILogger<LoggingJob> logger;
        private readonly TaskCompletionSource fired;

        public LoggingJob(ILogger<LoggingJob> logger, TaskCompletionSource fired)
        {
            this.logger = logger;
            this.fired = fired;
        }

        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
        {
            logger.LogInformation(Message);
            fired.TrySetResult();
            return default;
        }
    }

    private sealed record CapturedEntry(string Message, IReadOnlyList<KeyValuePair<string, object?>> Scopes);

    /// <summary>
    /// Records every line with the scopes that were open when it was written, which is the only way to
    /// see a scope: it never reaches the message.
    /// </summary>
    private sealed class ScopeCapturingLoggerProvider : ILoggerProvider, ISupportExternalScope
    {
        private readonly Lock gate = new();
        private readonly List<CapturedEntry> entries = [];
        private IExternalScopeProvider? scopeProvider;

        public IReadOnlyList<CapturedEntry> Entries
        {
            get
            {
                lock (gate)
                {
                    return [.. entries];
                }
            }
        }

        public void SetScopeProvider(IExternalScopeProvider scopeProvider) => this.scopeProvider = scopeProvider;

        public ILogger CreateLogger(string categoryName) => new CapturingLogger(this);

        public void Dispose()
        {
        }

        private void Record(string message)
        {
            List<KeyValuePair<string, object?>> scopes = [];
            scopeProvider?.ForEachScope(
                static (scope, state) =>
                {
                    if (scope is IReadOnlyList<KeyValuePair<string, object?>> pairs)
                    {
                        for (int i = 0; i < pairs.Count; i++)
                        {
                            state.Add(pairs[i]);
                        }
                    }
                },
                scopes);

            lock (gate)
            {
                entries.Add(new CapturedEntry(message, scopes));
            }
        }

        private sealed class CapturingLogger(ScopeCapturingLoggerProvider provider) : ILogger
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
