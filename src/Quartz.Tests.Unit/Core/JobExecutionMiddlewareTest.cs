#nullable enable

using Microsoft.Extensions.DependencyInjection;

using Quartz.Core;

namespace Quartz.Tests.Unit.Core;

/// <summary>
/// The execution seam: what a middleware wraps, what it may decide, and what it deliberately cannot.
/// </summary>
/// <remarks>
/// <para>
/// Every scheduler here is built the way an application builds one, because the registration is half of
/// the feature — a middleware that runs but is wired to the wrong scheduler, or in the wrong order, is
/// not something anybody can reason about.
/// </para>
/// <para>
/// The pipeline is composed once when the scheduler is built, so these tests keep what happened in a
/// recorder resolved from the container rather than in fields on the middleware. That is the same
/// constraint an application is under, and writing the tests inside it keeps them honest about it.
/// </para>
/// </remarks>
public sealed class JobExecutionMiddlewareTest
{
    [Test]
    public async Task MiddlewareRunsInRegistrationOrder_FirstRegisteredOutermost()
    {
        Recorder recorder = new(expectedFirings: 1);

        await RunScheduler(recorder, quartz =>
        {
            quartz.AddJobMiddleware<OuterMiddleware>();
            quartz.AddJobMiddleware<InnerMiddleware>();
        });

        recorder.Steps.Should().Equal(["outer:before", "inner:before", "job", "inner:after", "outer:after"],
            "middleware runs in registration order, outermost first — the first registered sees the "
            + "firing before the second and its result after it, which is the only ordering a log scope "
            + "or a transaction can be planned around");
    }

    [Test]
    public async Task AllThreeRegistrationOverloadsPutTheMiddlewareInThePipeline()
    {
        Recorder recorder = new(expectedFirings: 1);

        await RunScheduler(recorder, quartz =>
        {
            quartz.AddJobMiddleware<OuterMiddleware>();
            quartz.AddJobMiddleware(provider => new NamedMiddleware("factory", provider.GetRequiredService<Recorder>()));
            quartz.AddJobMiddleware(new NamedMiddleware("instance", recorder));
        });

        recorder.Steps.Should().Equal(
            ["outer:before", "factory:before", "instance:before", "job", "instance:after", "factory:after", "outer:after"],
            "the type, the factory and the instance overloads are three ways to say the same thing, and "
            + "each takes its place in the chain by when it was registered rather than by which overload "
            + "was used");
    }

    [Test]
    public async Task TheStandaloneBuilderRegistersMiddlewareToo()
    {
        Recorder recorder = new(expectedFirings: 1);

        IScheduler scheduler = await QuartzSchedulerBuilder
            .Create(q =>
            {
                q.Services.AddSingleton(recorder);
                q.ConfigureScheduler(options => options.InstanceName = $"standalone-{Guid.NewGuid():N}")
                    .AddJobMiddleware<OuterMiddleware>()
                    .AddJobListener(new CompletionListener(recorder))
                    .ScheduleJob<RecordingJob>(
                        trigger => trigger.WithIdentity("standalone").StartNow(),
                        job => job.WithIdentity("standalone"));
            })
            .BuildScheduler();

        try
        {
            await scheduler.Start();
            await recorder.WaitForCompletion();
        }
        finally
        {
            await scheduler.Shutdown(waitForJobsToComplete: true);
        }

        recorder.Steps.Should().Equal(["outer:before", "job", "outer:after"],
            "QuartzSchedulerBuilder is an IQuartzBuilder over a container it creates itself, so a chain "
            + "that registers middleware has to reach the registration AddQuartz reaches");
    }

    /// <summary>
    /// A middleware that declines to call the rest of the chain.
    /// </summary>
    /// <remarks>
    /// This is the thing a listener cannot do. A trigger listener's veto is the scheduler's own refusal —
    /// it raises <c>JobExecutionVetoed</c> and the firing ends there — whereas a middleware that keeps
    /// the call to itself is invisible from outside: the listeners were notified as usual, and the
    /// trigger is left exactly as a successful execution leaves it.
    /// </remarks>
    [Test]
    public async Task MiddlewareThatDoesNotCallNext_SkipsTheJobWhileTheFiringStillCompletes()
    {
        Recorder recorder = new(expectedFirings: 1);

        await RunScheduler(
            recorder,
            quartz => quartz.AddJobMiddleware<ShortCircuitingMiddleware>(),
            // A trigger that may fire again, so the instruction reports what this firing decided rather
            // than that the trigger has run out of fire times.
            trigger => trigger.WithSimpleSchedule(schedule => schedule.WithInterval(TimeSpan.FromHours(1)).RepeatForever()));

        recorder.Steps.Should().Equal(["short-circuit"], "the job was never called");
        recorder.JobExecutions.Should().Be(0, "the middleware kept the call to itself");

        recorder.ToBeExecutedCount.Should().Be(1,
            "the listeners are notified by the run shell around the whole pipeline, so a job listener "
            + "still hears that the firing happened — a middleware is not a veto");
        recorder.WasExecutedCount.Should().Be(1);
        recorder.JobException.Should().BeNull("declining to run the job is not a failure");

        recorder.Instruction.Should().Be(SchedulerInstruction.NoInstruction,
            "a firing whose middleware short-circuited leaves the trigger where a successful one does");
    }

    /// <summary>
    /// A middleware translating what a job threw into Quartz's own vocabulary, which is the shape an
    /// adapter around a third-party library takes.
    /// </summary>
    [Test]
    public async Task MiddlewareTranslatingAnException_IsHonouredExactlyAsOneTheJobThrew()
    {
        Recorder recorder = new(expectedFirings: 1) { FailFirstExecution = true };

        await RunScheduler(recorder, quartz => quartz.AddJobMiddleware<TranslatingMiddleware>());

        recorder.JobExecutions.Should().Be(2,
            "the middleware turned the job's InvalidOperationException into a JobExecutionException "
            + "asking for an immediate refire, and the run shell honoured it the way it honours one the "
            + "job raised itself — which is the whole point of running outside the classification");

        recorder.RefireCounts.Should().Equal([0, 1], "the second attempt is the refire of the first");

        recorder.WasExecutedCount.Should().Be(1,
            "a refire is not a completion — job listeners hear about the firing once, when it is over");
        recorder.JobException.Should().BeNull("the second attempt succeeded, so the firing completed cleanly");
    }

    /// <summary>
    /// The pipeline runs inside the run shell's concurrency handling rather than around it.
    /// </summary>
    [Test]
    public async Task DisallowConcurrentExecution_IsUnaffectedByMiddleware()
    {
        Recorder recorder = new(expectedFirings: 2);

        await RunScheduler(
            recorder,
            quartz =>
            {
                // Both triggers are ready at once and the pool has room for both, so nothing but the
                // attribute keeps them apart.
                quartz.ConfigureScheduler(options => options.IdleWaitTime = TimeSpan.FromSeconds(1));
                quartz.UseDefaultThreadPool(maxConcurrency: 5);
                quartz.AddJobMiddleware<ConcurrencyWatchingMiddleware>();
            },
            secondTrigger: true,
            jobType: typeof(ExclusiveRecordingJob));

        recorder.JobExecutions.Should().Be(2, "both triggers fired");
        recorder.MaxConcurrentJobs.Should().Be(1,
            "[DisallowConcurrentExecution] is enforced by the store handing out one firing of the job at "
            + "a time, which is above the pipeline — a middleware cannot widen it");
        recorder.MaxConcurrentMiddleware.Should().Be(1,
            "and the middleware sits inside that same window, so it never sees two firings of one job "
            + "overlapping either");
    }

    [Test]
    public async Task TheFiringIsAmbientInsideMiddleware()
    {
        Recorder recorder = new(expectedFirings: 1);

        await RunScheduler(recorder, quartz => quartz.AddJobMiddleware<AmbientReadingMiddleware>());

        recorder.AmbientBefore.Should().NotBeNull().And.BeSameAs(recorder.JobContext,
            "the firing is ambient before the pipeline is entered, so a middleware reads it through "
            + "IJobExecutionContextAccessor without being handed anything — which is why no IServiceScope "
            + "is threaded through the signature");
        recorder.AmbientAfter.Should().BeSameAs(recorder.JobContext,
            "and it is still ambient on the way out, where a middleware's finally block runs");
    }

    /// <summary>
    /// A named scheduler's middleware is its own, the way its listeners and its job store are.
    /// </summary>
    [Test]
    public async Task ANamedSchedulerRunsOnlyItsOwnMiddleware()
    {
        Recorder recorder = new(expectedFirings: 1);
        string id = Guid.NewGuid().ToString("N");

        ServiceCollection services = new();
        services.AddSingleton(recorder);
        services.AddQuartz("reporting", quartz =>
        {
            quartz.AddJobMiddleware(new NamedMiddleware("reporting", recorder));
            quartz.AddJobListener(new CompletionListener(recorder));
            quartz.ScheduleJob<RecordingJob>(
                trigger => trigger.WithIdentity($"trigger-{id}").StartNow(),
                job => job.WithIdentity($"job-{id}"));
        });
        services.AddQuartz(quartz => quartz.AddJobMiddleware(new NamedMiddleware("default", recorder)));

        await using ServiceProvider provider = services.BuildServiceProvider();

        IScheduler scheduler = provider.GetRequiredKeyedService<IScheduler>("reporting");
        try
        {
            await scheduler.Start();
            await recorder.WaitForCompletion();
        }
        finally
        {
            await scheduler.Shutdown(waitForJobsToComplete: true);
        }

        recorder.Steps.Should().Equal(["reporting:before", "job", "reporting:after"],
            "middleware is registered under its scheduler's service key, so the default scheduler's "
            + "never wraps a named scheduler's firing");
    }

    /// <summary>
    /// The configuration nearly every application runs: no middleware, and therefore no pipeline at all.
    /// </summary>
    /// <remarks>
    /// The run shell calls the job directly when this is null. A chain that was always composed — even
    /// one stage long, wrapping only the job — would put a delegate allocation and an extra call on the
    /// hot path of every firing in every application, to buy a feature almost none of them use.
    /// </remarks>
    [Test]
    public async Task ASchedulerWithNoMiddleware_HasNoPipeline()
    {
        ServiceCollection services = new();
        services.AddQuartz(quartz => quartz.ConfigureScheduler(options => options.InstanceName = $"bare-{Guid.NewGuid():N}"));

        await using ServiceProvider provider = services.BuildServiceProvider();

        provider.GetRequiredService<QuartzSchedulerResources>().JobExecutionPipeline.Should().BeNull(
            "a scheduler nobody gave middleware to executes its jobs through the call it always did");
    }

    /// <summary>
    /// One instance per scheduler, shared by every firing — which is why a middleware must keep no
    /// per-firing state in a field.
    /// </summary>
    [Test]
    public async Task TheMiddlewareIsBuiltOncePerScheduler_AndSharedByEveryFiring()
    {
        Recorder recorder = new(expectedFirings: 2);

        await RunScheduler(
            recorder,
            quartz =>
            {
                quartz.ConfigureScheduler(options => options.IdleWaitTime = TimeSpan.FromSeconds(1));
                quartz.AddJobMiddleware<CountingMiddleware>();
            },
            secondTrigger: true);

        recorder.JobExecutions.Should().Be(2, "both triggers fired");
        recorder.MiddlewareConstructions.Should().Be(1,
            "the chain is folded once when the scheduler is built, not per firing — so the middleware is "
            + "constructed once and its dependencies are resolved once");
    }

    /// <summary>
    /// A middleware a library contributes through <c>ConfigureAllQuartzSchedulers</c> always composes
    /// <em>inside</em> one the application registered in its own <c>AddQuartz</c> callback, whichever of
    /// the two calls was written first.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Deliberate, and worth pinning: <c>AddQuartzScheduler</c> runs a scheduler's own <c>configure</c>
    /// and only then applies what every scheduler was told, so a library's outbox or unit-of-work sits
    /// within the application's tenant scope rather than around it. Because that ordering does not depend
    /// on the order of the calls, a library cannot change it by being loaded earlier — which is what makes
    /// it something an application can rely on.
    /// </para>
    /// <para>
    /// Both orders are exercised, because the claim is precisely that the two are the same.
    /// </para>
    /// </remarks>
    [TestCase(true, TestName = "AConfigureAllMiddlewareComposesInsideAnAddQuartzOne_ConfigureAllFirst")]
    [TestCase(false, TestName = "AConfigureAllMiddlewareComposesInsideAnAddQuartzOne_AddQuartzFirst")]
    public async Task AConfigureAllMiddlewareComposesInsideAnAddQuartzOne(bool configureAllFirst)
    {
        Recorder recorder = new(expectedFirings: 1);

        string id = Guid.NewGuid().ToString("N");
        JobKey jobKey = new($"job-{id}", $"group-{id}");

        ServiceCollection services = new();
        services.AddSingleton(recorder);

        Action registerConfigureAll = () => services.ConfigureAllQuartzSchedulers(
            quartz => quartz.AddJobMiddleware(new NamedMiddleware("library", recorder)));

        Action registerScheduler = () => services.AddQuartz(quartz =>
        {
            quartz.ConfigureScheduler(options => options.InstanceName = $"configure-all-{id}");
            quartz.AddJobListener(new CompletionListener(recorder));
            quartz.AddJobMiddleware(new NamedMiddleware("application", recorder));
            quartz.AddJob(typeof(RecordingJob), job => job.WithIdentity(jobKey).StoreDurably());
            quartz.AddTrigger(configurator => configurator.ForJob(jobKey).WithIdentity($"trigger-{id}").StartNow());
        });

        if (configureAllFirst)
        {
            registerConfigureAll();
            registerScheduler();
        }
        else
        {
            registerScheduler();
            registerConfigureAll();
        }

        await using ServiceProvider provider = services.BuildServiceProvider();

        IScheduler scheduler = provider.GetRequiredService<IScheduler>();
        try
        {
            await scheduler.Start();
            await recorder.WaitForCompletion();
        }
        finally
        {
            await scheduler.Shutdown(waitForJobsToComplete: true);
        }

        recorder.Steps.Should().Equal(
            ["application:before", "library:before", "job", "library:after", "application:after"],
            "a scheduler's own callback runs before what ConfigureAllQuartzSchedulers said about every "
            + "scheduler, so the application's middleware is outermost however the two calls were ordered");
    }

    /// <summary>
    /// The one channel a middleware has to a listener: the merged job data map, whose XML documentation
    /// used to say that writing to it produced an illegal state.
    /// </summary>
    /// <remarks>
    /// It does not. The map is this firing's own merged copy, so a value put into it reaches the job and
    /// the listeners and is written back to nothing. Pinned because the correction is the whole of the
    /// documentation change, and prose that nothing checks goes stale.
    /// </remarks>
    [Test]
    public async Task AMiddlewareCanPassAValueToAListenerThroughTheMergedJobDataMap()
    {
        Recorder recorder = new(expectedFirings: 1);

        await RunScheduler(recorder, quartz => quartz.AddJobMiddleware<MapWritingMiddleware>());

        recorder.MergedValueSeenByListener.Should().Be("from the middleware",
            "the merged map is built once per firing and handed to everything that sees the context, so it "
            + "is how a middleware tells a listener something");

        recorder.JobContext!.JobDetail.JobDataMap.Should().NotContainKey(MapWritingMiddleware.Key,
            "the merged map is a copy, so nothing written into it reaches what the job or the trigger has "
            + "stored - which is why writing to it is safe");
    }

    /// <summary>
    /// Builds a scheduler with the given middleware, runs the job its trigger fires, and shuts
    /// everything down before returning — so an assertion never races the execution it is about.
    /// </summary>
    private static async Task RunScheduler(
        Recorder recorder,
        Action<IQuartzBuilder> configure,
        Action<ITriggerConfigurator<IJob>>? trigger = null,
        bool secondTrigger = false,
        Type? jobType = null)
    {
        string id = Guid.NewGuid().ToString("N");
        JobKey jobKey = new($"job-{id}", $"group-{id}");

        ServiceCollection services = new();
        services.AddSingleton(recorder);
        services.AddQuartz(quartz =>
        {
            quartz.ConfigureScheduler(options => options.InstanceName = $"middleware-{id}");
            quartz.AddJobListener(new CompletionListener(recorder));
            quartz.AddTriggerListener(new InstructionListener(recorder));
            configure(quartz);

            // Durable, so the job is added once and its triggers are scheduled once. A non-durable job
            // is scheduled and then rescheduled by the content processor, and rescheduling moves a
            // simple trigger's start time out of the past while keeping the fire time it already had —
            // which makes a repeating trigger fire twice in a row and has nothing to do with middleware.
            quartz.AddJob(jobType ?? typeof(RecordingJob), job => job.WithIdentity(jobKey).StoreDurably());
            quartz.AddTrigger(configurator =>
            {
                configurator.ForJob(jobKey).WithIdentity($"trigger-{id}").StartNow();
                trigger?.Invoke(configurator);
            });

            if (secondTrigger)
            {
                quartz.AddTrigger(configurator => configurator.ForJob(jobKey).WithIdentity($"trigger-{id}-2").StartNow());
            }
        });

        await using ServiceProvider provider = services.BuildServiceProvider();

        IScheduler scheduler = provider.GetRequiredService<IScheduler>();
        try
        {
            await scheduler.Start();
            await recorder.WaitForCompletion();
        }
        finally
        {
            await scheduler.Shutdown(waitForJobsToComplete: true);
        }
    }

    /// <summary>
    /// What the pipeline did, shared by the middleware, the job and the listeners.
    /// </summary>
    public sealed class Recorder
    {
        private readonly Lock gate = new();
        private readonly List<string> steps = [];
        private readonly List<int> refireCounts = [];
        private readonly TaskCompletionSource completed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly int expectedFirings;

        private int concurrentJobs;
        private int concurrentMiddleware;

        public Recorder(int expectedFirings)
        {
            this.expectedFirings = expectedFirings;
        }

        /// <summary>
        /// Whether the job's first execution should throw, so a translating middleware has something to
        /// translate.
        /// </summary>
        public bool FailFirstExecution { get; init; }

        public int JobExecutions { get; private set; }

        public int MiddlewareConstructions { get; private set; }

        public int ToBeExecutedCount { get; private set; }

        public int WasExecutedCount { get; private set; }

        public int MaxConcurrentJobs { get; private set; }

        public int MaxConcurrentMiddleware { get; private set; }

        public SchedulerInstruction? Instruction { get; private set; }

        public JobExecutionException? JobException { get; private set; }

        public IJobExecutionContext? JobContext { get; set; }

        public IJobExecutionContext? AmbientBefore { get; set; }

        public IJobExecutionContext? AmbientAfter { get; set; }

        /// <summary>
        /// What the job listener read out of the merged job data map, which is where a middleware put it.
        /// </summary>
        public string? MergedValueSeenByListener { get; private set; }

        public IReadOnlyList<string> Steps
        {
            get
            {
                lock (gate)
                {
                    return [.. steps];
                }
            }
        }

        public IReadOnlyList<int> RefireCounts
        {
            get
            {
                lock (gate)
                {
                    return [.. refireCounts];
                }
            }
        }

        public void Step(string step)
        {
            lock (gate)
            {
                steps.Add(step);
            }
        }

        public void MiddlewareConstructed()
        {
            lock (gate)
            {
                MiddlewareConstructions++;
            }
        }

        /// <summary>
        /// Records one job execution, and answers whether this one is supposed to fail.
        /// </summary>
        public bool EnterJob(IJobExecutionContext context)
        {
            lock (gate)
            {
                JobContext = context;
                JobExecutions++;
                refireCounts.Add(context.RefireCount);
                concurrentJobs++;
                MaxConcurrentJobs = Math.Max(MaxConcurrentJobs, concurrentJobs);
                return FailFirstExecution && JobExecutions == 1;
            }
        }

        public void ExitJob()
        {
            lock (gate)
            {
                concurrentJobs--;
            }
        }

        public void EnterMiddleware()
        {
            lock (gate)
            {
                concurrentMiddleware++;
                MaxConcurrentMiddleware = Math.Max(MaxConcurrentMiddleware, concurrentMiddleware);
            }
        }

        public void ExitMiddleware()
        {
            lock (gate)
            {
                concurrentMiddleware--;
            }
        }

        public void JobToBeExecuted()
        {
            lock (gate)
            {
                ToBeExecutedCount++;
            }
        }

        public void JobWasExecuted(JobExecutionException? jobException, string? mergedValue = null)
        {
            bool done;
            lock (gate)
            {
                WasExecutedCount++;
                JobException = jobException;
                MergedValueSeenByListener ??= mergedValue;
                done = WasExecutedCount >= expectedFirings;
            }

            if (done)
            {
                completed.TrySetResult();
            }
        }

        public void TriggerComplete(SchedulerInstruction instruction)
        {
            lock (gate)
            {
                Instruction = instruction;
            }
        }

        public Task WaitForCompletion() => completed.Task.WaitAsync(TimeSpan.FromSeconds(30));
    }

    public class RecordingJob : IJob
    {
        private readonly Recorder recorder;

        public RecordingJob(Recorder recorder)
        {
            this.recorder = recorder;
        }

        public async ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
        {
            bool fail = recorder.EnterJob(context);
            recorder.Step("job");
            try
            {
                await Work(cancellationToken).ConfigureAwait(false);
                if (fail)
                {
                    throw new InvalidOperationException("this execution fails on purpose");
                }
            }
            finally
            {
                recorder.ExitJob();
            }
        }

        protected virtual Task Work(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    /// <summary>
    /// The same job, held long enough that two firings would overlap if anything let them.
    /// </summary>
    [DisallowConcurrentExecution]
    public sealed class ExclusiveRecordingJob : RecordingJob
    {
        public ExclusiveRecordingJob(Recorder recorder) : base(recorder)
        {
        }

        protected override Task Work(CancellationToken cancellationToken) => Task.Delay(250, cancellationToken);
    }

    public sealed class OuterMiddleware : IJobExecutionMiddleware
    {
        private readonly Recorder recorder;

        public OuterMiddleware(Recorder recorder) => this.recorder = recorder;

        public async ValueTask Invoke(IJobExecutionContext context, JobExecutionDelegate next, CancellationToken cancellationToken = default)
        {
            recorder.Step("outer:before");
            try
            {
                await next(context, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                recorder.Step("outer:after");
            }
        }
    }

    public sealed class InnerMiddleware : IJobExecutionMiddleware
    {
        private readonly Recorder recorder;

        public InnerMiddleware(Recorder recorder) => this.recorder = recorder;

        public async ValueTask Invoke(IJobExecutionContext context, JobExecutionDelegate next, CancellationToken cancellationToken = default)
        {
            recorder.Step("inner:before");
            try
            {
                await next(context, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                recorder.Step("inner:after");
            }
        }
    }

    /// <summary>
    /// A middleware that says which registration overload put it there.
    /// </summary>
    public sealed class NamedMiddleware : IJobExecutionMiddleware
    {
        private readonly string name;
        private readonly Recorder recorder;

        public NamedMiddleware(string name, Recorder recorder)
        {
            this.name = name;
            this.recorder = recorder;
        }

        public async ValueTask Invoke(IJobExecutionContext context, JobExecutionDelegate next, CancellationToken cancellationToken = default)
        {
            recorder.Step($"{name}:before");
            try
            {
                await next(context, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                recorder.Step($"{name}:after");
            }
        }
    }

    public sealed class ShortCircuitingMiddleware : IJobExecutionMiddleware
    {
        private readonly Recorder recorder;

        public ShortCircuitingMiddleware(Recorder recorder) => this.recorder = recorder;

        public ValueTask Invoke(IJobExecutionContext context, JobExecutionDelegate next, CancellationToken cancellationToken = default)
        {
            recorder.Step("short-circuit");
            return default;
        }
    }

    public sealed class TranslatingMiddleware : IJobExecutionMiddleware
    {
        public async ValueTask Invoke(IJobExecutionContext context, JobExecutionDelegate next, CancellationToken cancellationToken = default)
        {
            try
            {
                await next(context, cancellationToken).ConfigureAwait(false);
            }
            catch (InvalidOperationException e)
            {
                throw new JobExecutionException(e) { RefireImmediately = true };
            }
        }
    }

    public sealed class ConcurrencyWatchingMiddleware : IJobExecutionMiddleware
    {
        private readonly Recorder recorder;

        public ConcurrencyWatchingMiddleware(Recorder recorder) => this.recorder = recorder;

        public async ValueTask Invoke(IJobExecutionContext context, JobExecutionDelegate next, CancellationToken cancellationToken = default)
        {
            recorder.EnterMiddleware();
            try
            {
                await next(context, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                recorder.ExitMiddleware();
            }
        }
    }

    public sealed class CountingMiddleware : IJobExecutionMiddleware
    {
        private readonly Recorder recorder;

        public CountingMiddleware(Recorder recorder)
        {
            this.recorder = recorder;
            recorder.MiddlewareConstructed();
        }

        public ValueTask Invoke(IJobExecutionContext context, JobExecutionDelegate next, CancellationToken cancellationToken = default)
        {
            return next(context, cancellationToken);
        }
    }

    /// <summary>
    /// A middleware that leaves a value in the merged job data map for a listener to find.
    /// </summary>
    public sealed class MapWritingMiddleware : IJobExecutionMiddleware
    {
        public const string Key = "middleware-said";

        public ValueTask Invoke(IJobExecutionContext context, JobExecutionDelegate next, CancellationToken cancellationToken = default)
        {
            context.MergedJobDataMap[Key] = "from the middleware";
            return next(context, cancellationToken);
        }
    }

    public sealed class AmbientReadingMiddleware : IJobExecutionMiddleware
    {
        private readonly Recorder recorder;
        private readonly IJobExecutionContextAccessor accessor;

        public AmbientReadingMiddleware(Recorder recorder, IJobExecutionContextAccessor accessor)
        {
            this.recorder = recorder;
            this.accessor = accessor;
        }

        public async ValueTask Invoke(IJobExecutionContext context, JobExecutionDelegate next, CancellationToken cancellationToken = default)
        {
            recorder.AmbientBefore = accessor.Current;
            try
            {
                await next(context, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                recorder.AmbientAfter = accessor.Current;
            }
        }
    }

    private sealed class CompletionListener : IJobListener
    {
        private readonly Recorder recorder;

        public CompletionListener(Recorder recorder) => this.recorder = recorder;

        public string Name => "completion";

        public ValueTask JobToBeExecuted(IJobExecutionContext context, CancellationToken cancellationToken = default)
        {
            recorder.JobToBeExecuted();
            return default;
        }

        public ValueTask JobWasExecuted(IJobExecutionContext context, JobExecutionException? jobException, CancellationToken cancellationToken = default)
        {
            recorder.JobWasExecuted(
                jobException,
                context.MergedJobDataMap.TryGetString(MapWritingMiddleware.Key, out string? value) ? value : null);
            return default;
        }
    }

    private sealed class InstructionListener : ITriggerListener
    {
        private readonly Recorder recorder;

        public InstructionListener(Recorder recorder) => this.recorder = recorder;

        public string Name => "instruction";

        public ValueTask TriggerComplete(
            ITrigger trigger,
            IJobExecutionContext context,
            SchedulerInstruction triggerInstructionCode,
            CancellationToken cancellationToken = default)
        {
            recorder.TriggerComplete(triggerInstructionCode);
            return default;
        }
    }
}
