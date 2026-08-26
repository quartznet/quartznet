#nullable enable

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using Quartz.Extensibility;

namespace Quartz.Tests.Unit.Configuration;

/// <summary>
/// A registered job is built by the container, which resolves constructor parameters without a
/// scheduler's service key — so a job type the container holds and one it does not were handed
/// different collaborators, and nothing about the job said which of the two it was (#3388). A registered
/// job may therefore not take a scheduler's own parts by constructor, and startup says so.
/// </summary>
/// <remarks>
/// <see cref="IStartupValidator"/> is what a host resolves and runs at the end of <c>Build()</c>, so
/// resolving it here is the same check an application gets — without needing a host.
/// </remarks>
public sealed class RegisteredJobConstructorTest
{
    [Test]
    public void AJobRegisteredWithAddJobCannotTakeItsSchedulerByConstructor()
    {
        ServiceCollection services = new();
        services.AddQuartz(q => q.AddJob<SchedulerJob>(job => job.WithIdentity("rotate")));

        using ServiceProvider provider = services.BuildServiceProvider();

        Action act = () => provider.GetRequiredService<IStartupValidator>().Validate();

        act.Should().Throw<OptionsValidationException>()
            .WithMessage("*SchedulerJob*IScheduler scheduler*IJobExecutionContext.Scheduler*",
                "the report has to name the job, the parameter and what to do instead, or it only says "
                + "that something is wrong");
    }

    /// <summary>
    /// Every type <c>SchedulerScopedServiceProvider</c> answers per scheduler, in the shapes a job would
    /// plausibly ask for one.
    /// </summary>
    [TestCase(typeof(SchedulerJob), "IScheduler scheduler")]
    [TestCase(typeof(SchedulerFactoryJob), "ISchedulerFactory schedulerFactory")]
    [TestCase(typeof(JobStoreJob), "IJobStore jobStore")]
    [TestCase(typeof(ThreadPoolJob), "IThreadPool threadPool")]
    [TestCase(typeof(SchedulerOptionsJob), "IOptions<QuartzSchedulerOptions> options")]
    public void EveryPartThatBelongsToOneSchedulerIsRefused(Type jobType, string parameter)
    {
        ServiceCollection services = new();
        services.AddQuartz(q => q.AddJob(jobType, job => job.WithIdentity("rotate")));

        using ServiceProvider provider = services.BuildServiceProvider();

        Action act = () => provider.GetRequiredService<IStartupValidator>().Validate();

        act.Should().Throw<OptionsValidationException>().WithMessage($"*{parameter}*");
    }

    /// <summary>
    /// The keyed registration <c>AddJobType</c> makes is the case that half-works today: it is this
    /// scheduler's own registration, and the container still builds it unkeyed.
    /// </summary>
    [Test]
    public void AJobTypeRegisteredForANamedSchedulerIsRefusedTheSameWay()
    {
        ServiceCollection services = new();
        services.AddQuartz("acme", q => q.AddJobType<TenantJob, SchedulerTenantJob>());

        using ServiceProvider provider = services.BuildServiceProvider();

        Action act = () => provider.GetRequiredService<IStartupValidator>().Validate();

        act.Should().Throw<OptionsValidationException>()
            .WithMessage("*TenantJob, built as SchedulerTenantJob, is registered on scheduler 'acme'*",
                "the job type named on the job detail and the type actually constructed can differ, and "
                + "the constructor that matters is the one being constructed");
    }

    /// <summary>
    /// One instance serving two schedulers is the worst of the cases: whichever scheduler's parts it was
    /// built with, the other one's fires are run by a job holding the wrong collaborators for the whole
    /// life of the container.
    /// </summary>
    [Test]
    public void ASingletonJobTypeIsRefusedToo()
    {
        ServiceCollection services = new();
        services.AddQuartz("acme", q => q.AddJobType<TenantJob, SchedulerTenantJob>(ServiceLifetime.Singleton));

        using ServiceProvider provider = services.BuildServiceProvider();

        Action act = () => provider.GetRequiredService<IStartupValidator>().Validate();

        act.Should().Throw<OptionsValidationException>().WithMessage("*SchedulerTenantJob*IScheduler scheduler*");
    }

    /// <summary>
    /// The approximation this check makes: every public constructor is examined, not the one the
    /// container would pick.
    /// </summary>
    /// <remarks>
    /// Which constructor that is depends on what else the container holds — one is only chosen when
    /// every parameter of it can be resolved — so a job whose clean constructor is picked today is
    /// picked differently the moment an unrelated registration appears, and the trap would be back
    /// without the job having changed.
    /// </remarks>
    [Test]
    public void AJobWithACleanConstructorBesideAnOffendingOneIsStillRefused()
    {
        ServiceCollection services = new();
        services.AddQuartz(q => q.AddJob<TwoConstructorJob>(job => job.WithIdentity("rotate")));

        using ServiceProvider provider = services.BuildServiceProvider();

        Action act = () => provider.GetRequiredService<IStartupValidator>().Validate();

        act.Should().Throw<OptionsValidationException>()
            .WithMessage("*TwoConstructorJob*ISchedulerFactory schedulerFactory*",
                "which constructor the container picks is a property of the container's contents rather "
                + "than of the job, so a job that must not be built with a scheduler's parts does not "
                + "declare a constructor that takes them");
    }

    /// <summary>
    /// What a job is supposed to take: the firing it is part of, the clock, and services of the
    /// application's own.
    /// </summary>
    /// <remarks>
    /// <see cref="TimeProvider"/> is the deliberate exclusion. A scheduler given no clock of its own
    /// inherits the container's, and injecting one is what the rest of the repository asks code to do
    /// rather than reading a clock statically.
    /// </remarks>
    [Test]
    public void AJobTakingTheFiringOrAnApplicationServiceIsFine()
    {
        ServiceCollection services = new();
        services.AddSingleton<TenantDirectory>();
        services.AddQuartz(q => q.AddJob<WellBehavedJob>(job => job.WithIdentity("rotate")));

        using ServiceProvider provider = services.BuildServiceProvider();

        Action act = () => provider.GetRequiredService<IStartupValidator>().Validate();

        act.Should().NotThrow();
    }

    /// <summary>
    /// The escape hatch the documentation points at: a job that genuinely has to be <em>constructed</em>
    /// with something of its scheduler's is registered with a factory, and resolves the part by key
    /// inside it.
    /// </summary>
    [Test]
    public void AJobBuiltByAFactoryOfItsOwnIsNotRefused()
    {
        ServiceCollection services = new();
        services.AddQuartz("acme", q =>
        {
            q.AddJob<SchedulerJob>(job => job.WithIdentity("rotate"));
            q.AddJobType<SchedulerJob>(provider =>
                new SchedulerJob(provider.GetRequiredKeyedService<IScheduler>("acme")));
        });

        using ServiceProvider provider = services.BuildServiceProvider();

        Action act = () => provider.GetRequiredService<IStartupValidator>().Validate();

        act.Should().NotThrow(
            "the scheduler's own registration is what the job factory resolves first, and a factory "
            + "constructs the job itself — there is no constructor for the container to fill in");
    }

    /// <summary>
    /// A job type the container does not hold is activated by the job factory through the
    /// scheduler-scoped provider, which hands it its own scheduler's parts. That path is unchanged, so
    /// there is nothing to refuse.
    /// </summary>
    [Test]
    public void AJobTypeTheContainerDoesNotHoldIsNotInspected()
    {
        ServiceCollection services = new();
        services.AddQuartz("acme", q => q.AddTrigger<SchedulerJob>(
            trigger => trigger.WithIdentity("rotate-trigger").ForJob("rotate", "tenancy")));

        using ServiceProvider provider = services.BuildServiceProvider();

        provider.GetService<SchedulerJob>().Should().BeNull(
            "naming a job type on a trigger does not register it, which is the premise of this test");

        Action act = () => provider.GetRequiredService<IStartupValidator>().Validate();

        act.Should().NotThrow(
            "a job the container was never told about is activated by the job factory, which builds it "
            + "from its own scheduler's parts");
    }

    /// <summary>
    /// A job belongs to the scheduler it was added to, and so does the failure.
    /// </summary>
    [Test]
    public void OnlyTheSchedulerThatWasGivenTheJobIsRefused()
    {
        ServiceCollection services = new();
        services.AddQuartz("acme", q => q.AddJobType<TenantJob, SchedulerTenantJob>());
        services.AddQuartz("initech", q => q.AddJobType<TenantJob, PlainTenantJob>());

        using ServiceProvider provider = services.BuildServiceProvider();

        IOptionsMonitor<QuartzSchedulerOptions> options =
            provider.GetRequiredService<IOptionsMonitor<QuartzSchedulerOptions>>();

        Action initech = () => options.Get("initech");
        initech.Should().NotThrow("initech builds the job type its own way, and its way is clean");

        Action acme = () => options.Get("acme");
        acme.Should().Throw<OptionsValidationException>().WithMessage("*scheduler 'acme'*");
    }

    public sealed class SchedulerJob : IJob
    {
        public SchedulerJob(IScheduler scheduler)
        {
            Scheduler = scheduler;
        }

        public IScheduler Scheduler { get; }

        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
    }

    public sealed class SchedulerFactoryJob : IJob
    {
        public SchedulerFactoryJob(ISchedulerFactory schedulerFactory)
        {
            SchedulerFactory = schedulerFactory;
        }

        public ISchedulerFactory SchedulerFactory { get; }

        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
    }

    public sealed class JobStoreJob : IJob
    {
        public JobStoreJob(IJobStore jobStore)
        {
            JobStore = jobStore;
        }

        public IJobStore JobStore { get; }

        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
    }

    public sealed class ThreadPoolJob : IJob
    {
        public ThreadPoolJob(IThreadPool threadPool)
        {
            ThreadPool = threadPool;
        }

        public IThreadPool ThreadPool { get; }

        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
    }

    public sealed class SchedulerOptionsJob : IJob
    {
        public SchedulerOptionsJob(IOptions<QuartzSchedulerOptions> options)
        {
            Options = options;
        }

        public IOptions<QuartzSchedulerOptions> Options { get; }

        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
    }

    public sealed class TwoConstructorJob : IJob
    {
        public TwoConstructorJob(TenantDirectory directory)
        {
            Directory = directory;
        }

        public TwoConstructorJob(TenantDirectory directory, ISchedulerFactory schedulerFactory)
        {
            Directory = directory;
            SchedulerFactory = schedulerFactory;
        }

        public TenantDirectory Directory { get; }

        public ISchedulerFactory? SchedulerFactory { get; }

        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
    }

    public sealed class WellBehavedJob : IJob
    {
        public WellBehavedJob(IJobExecutionContextAccessor accessor, TimeProvider clock, TenantDirectory directory)
        {
            Accessor = accessor;
            Clock = clock;
            Directory = directory;
        }

        public IJobExecutionContextAccessor Accessor { get; }

        public TimeProvider Clock { get; }

        public TenantDirectory Directory { get; }

        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
    }

    public abstract class TenantJob : IJob
    {
        public abstract ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default);
    }

    public sealed class SchedulerTenantJob : TenantJob
    {
        public SchedulerTenantJob(IScheduler scheduler)
        {
            Scheduler = scheduler;
        }

        public IScheduler Scheduler { get; }

        public override ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
    }

    public sealed class PlainTenantJob : TenantJob
    {
        public override ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
    }

    /// <summary>
    /// A service of the application's own, which a job is welcome to take.
    /// </summary>
    public sealed class TenantDirectory;
}
