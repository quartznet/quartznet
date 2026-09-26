#region License
/*
 * All content copyright Marko Lahma, unless otherwise indicated. All rights reserved.
 *
 * Licensed under the Apache License, Version 2.0 (the "License"); you may not
 * use this file except in compliance with the License. You may obtain a copy
 * of the License at
 *
 *   http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS, WITHOUT
 * WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the
 * License for the specific language governing permissions and limitations
 * under the License.
 *
 */
#endregion

#nullable enable

using FakeItEasy;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Quartz.Diagnostics;

namespace Quartz.Tests.Unit.Diagnostics;

/// <summary>
/// The per-firing scope <c>AddJobLogScope</c> opens: what a job's own log line carries with it, and what
/// it carries without the call.
/// </summary>
/// <remarks>
/// Driven through a real scheduler built by the container, with a logger provider that records the
/// scopes open when each line was written — a scope never reaches the message, so that is the only way
/// to see one. Nothing sets the ambient <see cref="LogProvider" />: the scope has to be pushed onto the
/// container's factory, which is the one the job logs through.
/// </remarks>
public sealed class JobLogScopeTest
{
    private static readonly TimeSpan observationDeadline = TimeSpan.FromSeconds(30);

    [Test]
    [NonParallelizable]
    public async Task EveryLineAJobWritesNamesItsFiring()
    {
        Recording recording = await RunOneFiring("scoped", q => q.AddJobLogScope());

        IReadOnlyList<KeyValuePair<string, object?>> scopes = recording.JobLine.Scopes;

        scopes.Should().Contain(new KeyValuePair<string, object?>(ActivityTags.JobName, "report"));
        scopes.Should().Contain(new KeyValuePair<string, object?>(ActivityTags.JobGroup, "reports"));
        scopes.Should().Contain(new KeyValuePair<string, object?>(ActivityTags.TriggerName, "nightly"));
        scopes.Should().Contain(new KeyValuePair<string, object?>(ActivityTags.TriggerGroup, "schedules"));
        scopes.Should().Contain(new KeyValuePair<string, object?>(ActivityTags.FireInstanceId, recording.FireInstanceId),
            "the fire instance is what tells two runs of one job apart, and what an interrupt names");
    }

    [Test]
    [NonParallelizable]
    public async Task WithoutTheCallAFiringOpensNoScopeOfItsOwn()
    {
        Recording recording = await RunOneFiring("unscoped", _ => { });

        recording.JobLine.Scopes.Should().NotContain(x => x.Key == ActivityTags.FireInstanceId,
            "the per-firing scope is opt-in: it costs an execution context copy on every firing");
        recording.JobLine.Scopes.Should().Contain(x => x.Key == ActivityTags.SchedulerName,
            "the scheduler's own scope is opened once per loop and reaches the job either way");
    }

    /// <summary>
    /// A second call, or the same call made again for every scheduler, would put the same five values on
    /// every line twice.
    /// </summary>
    [Test]
    [NonParallelizable]
    public async Task CallingItMoreThanOnceOpensOneScope()
    {
        Recording recording = await RunOneFiring(
            "twice",
            q => q.AddJobLogScope().AddJobLogScope(),
            services => services.ConfigureAllQuartzSchedulers(q => q.AddJobLogScope()));

        recording.JobLine.Scopes.Where(x => x.Key == ActivityTags.FireInstanceId).Should().ContainSingle(
            "the registration is guarded, so however many times it is asked for, one scope is opened");
    }

    /// <summary>
    /// The guard is per scheduler: a scheduler registered after another that asked for the scope still
    /// gets its own.
    /// </summary>
    [Test]
    [NonParallelizable]
    public async Task EachSchedulerAsksForItsOwn()
    {
        Recording recording = await RunOneFiring(
            "second",
            q => q.AddJobLogScope(),
            services => services.AddQuartz("first", q => q.AddJobLogScope()));

        recording.JobLine.Scopes.Should().Contain(new KeyValuePair<string, object?>(ActivityTags.FireInstanceId, recording.FireInstanceId),
            "each scheduler has a pipeline of its own, so another scheduler's scope says nothing about this one's");
    }

    /// <summary>
    /// Registered first, the scope is outside every other middleware, so what they log names the firing too.
    /// </summary>
    [Test]
    [NonParallelizable]
    public async Task AMiddlewareRegisteredAfterItLogsInsideTheScope()
    {
        Recording recording = await RunOneFiring(
            "ordered",
            q => q
                .AddJobLogScope()
                .AddJobMiddleware(provider => new LoggingMiddleware(provider.GetRequiredService<ILogger<LoggingMiddleware>>())));

        CapturedEntry middlewareLine = recording.Entries.Should().ContainSingle(x => x.Message == LoggingMiddleware.Message).Subject;

        middlewareLine.Scopes.Should().Contain(new KeyValuePair<string, object?>(ActivityTags.FireInstanceId, recording.FireInstanceId),
            "middleware runs in registration order, outermost first");
    }

    [Test]
    public void TheScopeCarriesTheFiringUnderTheAttributeNamesTheSpanUses()
    {
        JobLogScope scope = new(Firing("report", "reports", "nightly", "schedules", "fire-1"));

        scope.Should().Equal(
            [
                new KeyValuePair<string, object?>(ActivityTags.JobName, "report"),
                new KeyValuePair<string, object?>(ActivityTags.JobGroup, "reports"),
                new KeyValuePair<string, object?>(ActivityTags.TriggerName, "nightly"),
                new KeyValuePair<string, object?>(ActivityTags.TriggerGroup, "schedules"),
                new KeyValuePair<string, object?>(ActivityTags.FireInstanceId, "fire-1"),
            ],
            "the enumerator and the indexer are two ways into the same five pairs");

        scope.Count.Should().Be(5);
        scope[4].Should().Be(new KeyValuePair<string, object?>(ActivityTags.FireInstanceId, "fire-1"));
        ((System.Collections.IEnumerable) scope).Cast<KeyValuePair<string, object?>>().Should().Equal(scope,
            "a provider that reads the scope as a plain IEnumerable sees the same pairs");

        Func<KeyValuePair<string, object?>> beyond = () => scope[5];
        beyond.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Test]
    public void TheTextFormIsBuiltOnceWhenAProviderFirstAsks()
    {
        JobLogScope scope = new(Firing("report", "reports", "nightly", "schedules", "fire-1"));

        string text = scope.ToString();

        text.Should().Be("quartz.job.name:report quartz.job.group:reports quartz.trigger.name:nightly quartz.trigger.group:schedules quartz.fire.instance.id:fire-1");
        scope.ToString().Should().BeSameAs(text,
            "a provider that renders a scope as text asks for it once per line, so it is formatted once and kept");
    }

    [Test]
    public void TheBuilderIsRequired()
    {
        Action act = () => QuartzBuilderExtensions.AddJobLogScope(null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("builder");
    }

    private static IJobExecutionContext Firing(string jobName, string jobGroup, string triggerName, string triggerGroup, string fireInstanceId)
    {
        IJobDetail job = JobBuilder.Create<LoggingJob>().WithIdentity(jobName, jobGroup).Build();
        ITrigger trigger = TriggerBuilder.Create().WithIdentity(triggerName, triggerGroup).ForJob(job).Build();

        IJobExecutionContext context = A.Fake<IJobExecutionContext>();
        A.CallTo(() => context.JobDetail).Returns(job);
        A.CallTo(() => context.Trigger).Returns(trigger);
        A.CallTo(() => context.FireInstanceId).Returns(fireInstanceId);
        return context;
    }

    private static async Task<Recording> RunOneFiring(
        string schedulerName,
        Action<IQuartzBuilder> configure,
        Action<IServiceCollection>? before = null)
    {
        ScopeCapturingLoggerProvider recorder = new();
        TaskCompletionSource<string> fired = new(TaskCreationOptions.RunContinuationsAsynchronously);

        ServiceCollection services = new();
        services.AddLogging(builder => builder.AddProvider(recorder));
        services.AddSingleton(fired);
        before?.Invoke(services);

        services.AddQuartz(schedulerName, q =>
        {
            configure(q);
            q.ScheduleJob<LoggingJob>(
                trigger => trigger.WithIdentity("nightly", "schedules").StartNow(),
                job => job.WithIdentity("report", "reports"));
        });

        await using ServiceProvider provider = services.BuildServiceProvider();

        IScheduler scheduler = await provider.GetRequiredKeyedService<ISchedulerFactory>(schedulerName).GetScheduler();
        await scheduler.Start();
        string fireInstanceId;
        try
        {
            fireInstanceId = await fired.Task.WaitAsync(observationDeadline);
        }
        finally
        {
            await scheduler.Shutdown(waitForJobsToComplete: true);
        }

        IReadOnlyList<CapturedEntry> entries = recorder.Entries;
        CapturedEntry jobLine = entries.Should().ContainSingle(x => x.Message == LoggingJob.Message,
            "the job logs exactly once, with a logger the container gave it").Subject;

        return new Recording(entries, jobLine, fireInstanceId);
    }

    private sealed record Recording(IReadOnlyList<CapturedEntry> Entries, CapturedEntry JobLine, string FireInstanceId);

    public sealed class LoggingJob : IJob
    {
        internal const string Message = "the job wrote this";

        private readonly ILogger<LoggingJob> logger;
        private readonly TaskCompletionSource<string> fired;

        public LoggingJob(ILogger<LoggingJob> logger, TaskCompletionSource<string> fired)
        {
            this.logger = logger;
            this.fired = fired;
        }

        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
        {
            logger.LogInformation(Message);
            fired.TrySetResult(context.FireInstanceId);
            return default;
        }
    }

    private sealed class LoggingMiddleware(ILogger<LoggingMiddleware> logger) : IJobExecutionMiddleware
    {
        internal const string Message = "the middleware wrote this";

        public ValueTask Invoke(IJobExecutionContext context, JobExecutionDelegate next, CancellationToken cancellationToken = default)
        {
            logger.LogInformation(Message);
            return next(context, cancellationToken);
        }
    }

    private sealed record CapturedEntry(string Message, IReadOnlyList<KeyValuePair<string, object?>> Scopes);

    /// <summary>
    /// Records every line with the scopes that were open when it was written.
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
