using System.Collections.Concurrent;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using Quartz.Configuration;
using Quartz.Extensibility;

namespace Quartz.Tests.Unit.Configuration;

/// <summary>
/// What a job of a scheduler built at runtime is handed, and whose scope it runs in.
/// </summary>
/// <remarks>
/// <para>
/// This is the test that decides whether the mechanism is honest. A scheduler added at runtime lives in
/// a container of its own, and the well-known way of doing that — a composite provider that asks the
/// child and falls back to the parent — resolves <see cref="IServiceScopeFactory" /> from the child, so
/// the per-execution scope is not the application's at all. Every application-scoped service a job takes
/// is then created outside any scope the application owns and never disposed, which shows up in
/// production as a leak and in development as an exception, and is the reason the DI platform declined
/// to ship child containers.
/// </para>
/// <para>
/// So: the firing's scope is the application's, a scoped service is one instance across everything the
/// firing touches, it is disposed when the job is returned, and the scheduler's own parts are still the
/// tenant's.
/// </para>
/// </remarks>
[NonParallelizable]
public sealed class SchedulerGenerationScopeTest
{
    private static readonly TimeSpan firingTimeout = TimeSpan.FromSeconds(30);

    [Test]
    public async Task AJobOfARuntimeTenantIsBuiltInTheApplicationsScope()
    {
        using ServiceProvider application = Application(services =>
        {
            services.AddScoped<UnitOfWork>();
            services.AddScoped<ScopedProbeJob>();
        });

        ScopeRecorder recorder = application.GetRequiredService<ScopeRecorder>();

        await using SchedulerGeneration acme = Generation(application, "acme");
        await using SchedulerGeneration initech = Generation(application, "initech");

        IScheduler acmeScheduler = await Fire<ScopedProbeJob>(acme);
        IScheduler initechScheduler = await Fire<ScopedProbeJob>(initech);

        Observation first = await recorder.Wait("acme");
        Observation second = await recorder.Wait("initech");

        first.UnitOfWork.Should().BeSameAs(recorder.FromScope["acme"],
            "the job and the scheduler's own ConfigureJobScope hook run inside one firing, so a scoped "
            + "service they both reach has to be one instance - two would mean two scopes, and the "
            + "application's half of the firing living outside the one that gets disposed");
        first.UnitOfWork.Should().NotBeSameAs(second.UnitOfWork,
            "each firing gets its own scope, and two tenants firing the same job are two firings");

        await acmeScheduler.Shutdown(waitForJobsToComplete: true);
        await initechScheduler.Shutdown(waitForJobsToComplete: true);

        first.UnitOfWork.Disposed.Should().BeTrue(
            "the scope is closed when the job is returned, and the application's scope is closed with it - "
            + "otherwise every firing of every tenant leaks whatever its jobs resolved");
        second.UnitOfWork.Disposed.Should().BeTrue();
    }

    [Test]
    public async Task ARuntimeTenantsJobGetsItsOwnSchedulerParts()
    {
        using ServiceProvider application = Application(services => { });

        ScopeRecorder recorder = application.GetRequiredService<ScopeRecorder>();

        await using SchedulerGeneration generation = Generation(application, "acme");
        IScheduler scheduler = await Fire<PartsProbeJob>(generation);

        Observation observed = await recorder.Wait("acme");

        try
        {
            IScheduler resolved = await observed.SchedulerFactory.GetScheduler();
            resolved.Should().BeSameAs(scheduler,
                "a scheduler's parts are keyed by its name in its own container, so a job activated for a "
                + "tenant is handed that tenant's factory rather than the application's default scheduler's");
        }
        finally
        {
            await scheduler.Shutdown(waitForJobsToComplete: true);
        }
    }

    [Test]
    public async Task ApplicationOptionsAreTheApplications()
    {
        using ServiceProvider application = Application(services =>
            services.Configure<ApplicationOptions>(options => options.Value = "from-the-application"));

        ScopeRecorder recorder = application.GetRequiredService<ScopeRecorder>();

        await using SchedulerGeneration generation = Generation(application, "acme");
        IScheduler scheduler = await Fire<PartsProbeJob>(generation);

        Observation observed = await recorder.Wait("acme");

        try
        {
            observed.ApplicationOption.Should().Be("from-the-application",
                "nothing in the tenant's recipe said anything about this options type, so reading it from "
                + "the tenant's container would answer with a defaulted instance nobody wrote");
        }
        finally
        {
            await scheduler.Shutdown(waitForJobsToComplete: true);
        }
    }

    [Test]
    public async Task ASchedulerScopedOptionsSnapshotIsTheTenants()
    {
        using ServiceProvider application = Application(services => { });

        ScopeRecorder recorder = application.GetRequiredService<ScopeRecorder>();

        await using SchedulerGeneration generation = Generation(application, "acme");
        IScheduler scheduler = await Fire<PartsProbeJob>(generation);

        Observation observed = await recorder.Wait("acme");

        try
        {
            observed.SnapshotInstanceName.Should().Be("acme",
                "a tenant's container holds exactly one scheduler, and Quartz's own options read inside it "
                + "are that scheduler's - reading them from the application would answer with whatever the "
                + "application's default scheduler was configured with");
        }
        finally
        {
            await scheduler.Shutdown(waitForJobsToComplete: true);
        }
    }

    /// <summary>
    /// An application container with its own default scheduler, so that the tenant is genuinely a second
    /// scheduler beside one rather than the only thing in the process.
    /// </summary>
    private static ServiceProvider Application(Action<IServiceCollection> configure)
    {
        ServiceCollection services = new();
        services.AddLogging();
        services.AddQuartz();
        services.AddSingleton<ScopeRecorder>();
        configure(services);

        // Scope validation on, because the leak this test exists to catch is exactly what it reports.
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }

    private static SchedulerGeneration Generation(IServiceProvider application, string schedulerName)
    {
        return SchedulerGeneration.Build(
            application,
            schedulerName,
            default,
            builder => builder.ConfigureJobScope((scope, _, scheduler) =>
            {
                ScopeRecorder recorder = scope.ServiceProvider.GetService<ScopeRecorder>();
                UnitOfWork unitOfWork = scope.ServiceProvider.GetService<UnitOfWork>();
                if (recorder is not null && unitOfWork is not null)
                {
                    recorder.FromScope[scheduler.SchedulerName] = unitOfWork;
                }
            }),
            number: 1,
            refuseInstanceParts: false);
    }

    /// <summary>
    /// Creates and starts the tenant, and schedules one firing of a job the recipe never registered — so
    /// the job factory activates it through the tenant's provider, which is the path a job's constructor
    /// parameters are decided on one at a time.
    /// </summary>
    private static async Task<IScheduler> Fire<T>(SchedulerGeneration generation) where T : IJob
    {
        IScheduler scheduler = await generation.Create();
        await scheduler.Start();

        await scheduler.ScheduleJob(
            JobBuilder.Create<T>().WithIdentity("probe").Build(),
            TriggerBuilder.Create().WithIdentity("probe").StartNow().Build());

        return scheduler;
    }

    private sealed class ScopeRecorder
    {
        private readonly ConcurrentDictionary<string, TaskCompletionSource<Observation>> firings = new();

        /// <summary>
        /// What the scheduler's own per-firing hook saw, by scheduler name.
        /// </summary>
        public ConcurrentDictionary<string, UnitOfWork> FromScope { get; } = new();

        public void Record(Observation observation)
        {
            Slot(observation.SchedulerName).TrySetResult(observation);
        }

        public async Task<Observation> Wait(string schedulerName)
        {
            Task<Observation> firing = Slot(schedulerName).Task;
            Task finished = await Task.WhenAny(firing, Task.Delay(firingTimeout));
            finished.Should().BeSameAs(firing, $"the job of scheduler '{schedulerName}' has to have fired");
            return await firing;
        }

        private TaskCompletionSource<Observation> Slot(string schedulerName)
        {
            return firings.GetOrAdd(
                schedulerName,
                static _ => new TaskCompletionSource<Observation>(TaskCreationOptions.RunContinuationsAsynchronously));
        }
    }

    private sealed record Observation(
        string SchedulerName,
        UnitOfWork UnitOfWork,
        ISchedulerFactory SchedulerFactory,
        string ApplicationOption,
        string SnapshotInstanceName);

    /// <summary>
    /// Registered in the application's container, and taking only application services — the shape the
    /// documentation asks a registered job to have.
    /// </summary>
    private sealed class ScopedProbeJob : IJob
    {
        private readonly ScopeRecorder recorder;
        private readonly UnitOfWork unitOfWork;

        public ScopedProbeJob(ScopeRecorder recorder, UnitOfWork unitOfWork)
        {
            this.recorder = recorder;
            this.unitOfWork = unitOfWork;
        }

        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
        {
            recorder.Record(new Observation(context.Scheduler.SchedulerName, unitOfWork, null, null, null));
            return default;
        }
    }

    /// <summary>
    /// Registered nowhere, so the job factory activates it and every constructor parameter is routed one
    /// at a time — which is where "this scheduler's" and "the application's" have to come apart.
    /// </summary>
    private sealed class PartsProbeJob : IJob
    {
        private readonly ScopeRecorder recorder;
        private readonly ISchedulerFactory schedulerFactory;
        private readonly IOptions<ApplicationOptions> applicationOptions;
        private readonly IOptionsSnapshot<QuartzSchedulerOptions> schedulerOptions;

        public PartsProbeJob(
            ScopeRecorder recorder,
            ISchedulerFactory schedulerFactory,
            IOptions<ApplicationOptions> applicationOptions,
            IOptionsSnapshot<QuartzSchedulerOptions> schedulerOptions)
        {
            this.recorder = recorder;
            this.schedulerFactory = schedulerFactory;
            this.applicationOptions = applicationOptions;
            this.schedulerOptions = schedulerOptions;
        }

        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
        {
            recorder.Record(new Observation(
                context.Scheduler.SchedulerName,
                null,
                schedulerFactory,
                applicationOptions.Value.Value,
                schedulerOptions.Value.InstanceName));

            return default;
        }
    }

    private sealed class ApplicationOptions
    {
        public string Value { get; set; }
    }

    private sealed class UnitOfWork : IDisposable
    {
        public bool Disposed { get; private set; }

        public void Dispose() => Disposed = true;
    }
}
