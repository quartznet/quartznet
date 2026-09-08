using FakeItEasy;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

using Quartz.Extensibility;
using Quartz.Impl;

namespace Quartz.Tests.Unit.Configuration;

/// <summary>
/// Building a second generation of a scheduler from the recipe that built the first.
/// </summary>
/// <remarks>
/// <para>
/// Nothing here restarts anything. A scheduler's thread pool, job store, connection provider, plugins
/// and listeners are one-way — every one of them refuses work once it has been shut down — so a restart
/// is the recipe run again into a container of its own, and the name is the only thing the two
/// schedulers share. What the tests are really about is the order of the three steps and what each
/// refusal protects: build first so a broken recipe leaves the old scheduler running, drain second so
/// the new one's recovery sweep cannot run beside the old one's jobs, create last.
/// </para>
/// <para>
/// The corruption that ordering prevents needs a persistent store to be observable at all, and is in
/// <see cref="SchedulerRuntimeDrainTest" />.
/// </para>
/// </remarks>
[NonParallelizable]
public sealed class SchedulerRuntimeRestartTest
{
    private readonly List<IScheduler> started = [];

    [TearDown]
    public async Task ShutDownWhateverIsLeft()
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

    [Test]
    public async Task RestartOfARuntimeTenantIsANewSetOfInstances()
    {
        List<IJobStore> stores = [];

        await using ServiceProvider provider = Container(services => services.AddQuartz("main", _ => { }));

        IScheduler first = await Add(provider, "acme", q => q.UseJobStore(p => Record(stores, p)));
        first.Status.Should().Be(SchedulerStatus.Running);

        IScheduler second = await Restart(provider, "acme");

        second.Should().NotBeSameAs(first, "the handle a caller holds across a restart is the name, not the scheduler");
        second.Status.Should().Be(SchedulerStatus.Running, "the old one was running, so the new one is");
        first.Status.Should().Be(SchedulerStatus.Shutdown, "the old scheduler is shut down, not reused");

        stores.Should().HaveCount(2).And.OnlyHaveUniqueItems(
            "the recipe was run again, and a second Initialize on the same store is exactly what a restart "
            + "must never be");

        ISchedulerRepository repository = provider.GetRequiredService<ISchedulerRepository>();
        repository.Lookup("acme").Should().BeSameAs(second, "the repository holds the generation that is alive");
        repository.LookupAll().Should().ContainSingle(x => x.SchedulerName == "acme",
            "the old one unbound itself when it shut down, so nothing has to remember to");
    }

    /// <summary>
    /// A scheduler <c>AddQuartz(name, …)</c> registered is restartable, because the registration wrote
    /// down what it was told.
    /// </summary>
    [Test]
    public async Task RestartOfAContainerRegisteredSchedulerReplaysItsRecipe()
    {
        JobKey jobKey = new("nightly", "acme");

        await using ServiceProvider provider = Container(services => services.AddQuartz("acme", q =>
            q.AddJob<RestartJob>(job => job.WithIdentity(jobKey).StoreDurably())));

        IScheduler first = await provider.GetRequiredKeyedService<ISchedulerFactory>("acme").GetScheduler();
        started.Add(first);
        await first.Start();

        IScheduler second = await Restart(provider, "acme");

        second.Should().NotBeSameAs(first);
        (await second.GetJobDetail(jobKey)).Should().NotBeNull(
            "the declared content is part of the recipe, so the new generation applies it exactly as the "
            + "container's own registration did");

        provider.GetRequiredService<ISchedulerRepository>().Lookup("acme").Should().BeSameAs(second);

        IScheduler throughTheFactory = await provider.GetRequiredKeyedService<ISchedulerFactory>("acme").GetScheduler();
        throughTheFactory.Should().BeSameAs(second,
            "the factory answers from the repository, so a name that was restarted resolves to the generation "
            + "that is alive rather than to the one it replaced");

        IScheduler handle = provider.GetRequiredKeyedService<IScheduler>("acme");
        handle.SchedulerInstanceId.Should().Be(second.SchedulerInstanceId);
        handle.Status.Should().Be(SchedulerStatus.Running,
            "every [FromKeyedServices(\"acme\")] IScheduler in the application is one of these handles, and a "
            + "handle that went on answering for the generation it first resolved would leave the application "
            + "injecting the dead one while everything that reads the repository showed the live one");
    }

    /// <summary>
    /// A registration nothing has built yet is restarted by simply building it.
    /// </summary>
    [Test]
    public async Task RestartOfARegisteredButUnbuiltSchedulerBuildsIt()
    {
        await using ServiceProvider provider = Container(services => services.AddQuartz("acme", _ => { }));

        IScheduler scheduler = await Restart(provider, "acme");

        scheduler.SchedulerName.Should().Be("acme");
        scheduler.Status.Should().Be(SchedulerStatus.Created,
            "nothing was running under that name, so there was nothing whose running to preserve");
    }

    [Test]
    public async Task RestartRefusesTheDefaultScheduler()
    {
        await using ServiceProvider provider = Container(services =>
            services.AddQuartz(q => q.ConfigureScheduler(o => o.InstanceName = "TheDefaultOne")));

        Func<Task> restart = async () => await Restart(provider, "TheDefaultOne");

        (await restart.Should().ThrowAsync<SchedulerConfigException>(
                "the default scheduler's parts are the container's unkeyed registrations, and nothing can tell "
                + "them apart from the application's own"))
            .WithMessage("*registered without a name*")
            .WithMessage("*AddQuartz(\"TheDefaultOne\", …)*")
            .WithMessage("*Standby()/Start()*", "the pause-and-resume pair is what the caller probably wanted");
    }

    [Test]
    public async Task RestartOfAnUnknownNameThrowsSchedulerNotFound()
    {
        await using ServiceProvider provider = Container(services => services.AddQuartz("main", _ => { }));

        Func<Task> restart = async () => await Restart(provider, "nobody");

        (await restart.Should().ThrowAsync<SchedulerNotFoundException>(
                "there is no recipe to replay, and 'no such scheduler' is the one failure a caller can act on "
                + "without changing anything"))
            .Which.SchedulerName.Should().Be("nobody");
    }

    /// <summary>
    /// A recipe that closes over an object cannot produce a second generation, and says so before it has
    /// built anything at all.
    /// </summary>
    /// <remarks>
    /// The timing is what the assertions are about. A refusal reached by building the next generation and
    /// comparing the parts would have to release the container it built, and Microsoft's container
    /// releases whatever a factory delegate returned — which here is the store the <em>running</em>
    /// scheduler is using. So the test watches the store: it must not have been initialized a second
    /// time, it must not have been disposed, and the scheduler that has it must still fire.
    /// </remarks>
    [Test]
    public async Task RestartRefusesARecipeThatSuppliesAJobStoreInstance()
    {
        RecordingJobStore shared = new(TestJobStores.Ram());

        await using ServiceProvider provider = Container(services => services.AddQuartz("main", _ => { }));

        IScheduler tenant = await Add(provider, "acme", q => q.UseJobStore(shared));

        Func<Task> restart = async () => await Restart(provider, "acme");

        (await restart.Should().ThrowAsync<SchedulerConfigException>(
                "replaying the recipe would hand the new scheduler the object the old one is using, and an "
                + "instance is the one thing a recipe cannot produce twice"))
            .WithMessage("*IJobStore as an instance*")
            .WithMessage("*UseJobStore(IJobStore)*")
            .WithMessage("*was not touched*");

        shared.Disposed.Should().BeFalse(
            "the refusal happens before a provider exists, so there is no container to release — and one that "
            + "had to be released would have taken the running scheduler's own store with it");
        shared.Initializations.Should().Be(1,
            "nothing of the next generation was built, so the store was never initialized a second time");

        tenant.Status.Should().Be(SchedulerStatus.Running,
            "the refusal is decided before anything is shut down, so a caller that gets it has lost nothing");
        provider.GetRequiredService<ISchedulerRepository>().Lookup("acme").Should().BeSameAs(tenant);

        await FiresAJob(tenant);
    }

    /// <summary>
    /// The same, for the thread pool — the other part whose object a recipe most often closes over.
    /// </summary>
    [Test]
    public async Task RestartRefusesARecipeThatSuppliesAThreadPoolInstance()
    {
        DisposableThreadPool shared = new();

        await using ServiceProvider provider = Container(services => services.AddQuartz("main", _ => { }));

        IScheduler tenant = await Add(provider, "acme", q => q.UseThreadPool(shared));

        Func<Task> restart = async () => await Restart(provider, "acme");

        (await restart.Should().ThrowAsync<SchedulerConfigException>())
            .WithMessage("*IThreadPool as an instance*")
            .WithMessage("*UseThreadPool(IThreadPool)*");

        shared.Disposed.Should().BeFalse(
            "a pool the running scheduler draws its threads from has to survive a refusal that exists to "
            + "protect it");
        shared.ShutdownCalled.Should().BeFalse("nothing was shut down, because nothing was built");
        tenant.Status.Should().Be(SchedulerStatus.Running);
    }

    /// <summary>
    /// A factory that closes over a shared object is refused too — by the second line, which is why its
    /// message says a factory must return a new instance.
    /// </summary>
    /// <remarks>
    /// This is the case the collection cannot be read for: <c>UseJobStore(provider =&gt; shared)</c> is a
    /// factory registration by its shape and an instance registration by its effect, and only comparing
    /// what two containers produced tells them apart.
    /// </remarks>
    [Test]
    public async Task RestartRefusesAFactoryThatReturnsTheStoreTheRunningSchedulerHas()
    {
        IJobStore shared = TestJobStores.Ram();

        await using ServiceProvider provider = Container(services => services.AddQuartz("main", _ => { }));

        IScheduler tenant = await Add(provider, "acme", q => q.UseJobStore(_ => shared));

        Func<Task> restart = async () => await Restart(provider, "acme");

        (await restart.Should().ThrowAsync<SchedulerConfigException>(
                "a factory is replayed once per generation, so one that hands back the same object every time "
                + "is an instance registration wearing a factory's clothes"))
            .WithMessage("*the same job store (UseJobStore(provider => *")
            .WithMessage("*must return a new instance each time*")
            .WithMessage("*still running*");

        tenant.Status.Should().Be(SchedulerStatus.Running);
        provider.GetRequiredService<ISchedulerRepository>().Lookup("acme").Should().BeSameAs(tenant);
    }

    /// <summary>
    /// The new generation is built before the old one is touched, so a recipe that no longer works costs
    /// nothing.
    /// </summary>
    /// <remarks>
    /// The failure is provoked on the second run of the recipe rather than the first, because that is the
    /// shape of the real thing: a connection string that disappeared from configuration, a validator a
    /// package added, a setting a later version rejects. The scheduler that is running has to survive it.
    /// </remarks>
    [Test]
    public async Task AConfigurationErrorInTheNewGenerationLeavesTheOldRunning()
    {
        int runs = 0;

        await using ServiceProvider provider = Container(services => services.AddQuartz("main", _ => { }));

        IScheduler tenant = await Add(provider, "acme", q =>
        {
            if (Interlocked.Increment(ref runs) > 1)
            {
                q.ConfigureScheduler(o => o.IdleWaitTime = TimeSpan.Zero);
            }
        });

        Func<Task> restart = async () => await Restart(provider, "acme");

        (await restart.Should().ThrowAsync<SchedulerConfigException>(
                "options validation is how configuration is checked, and a caller should not have to catch the "
                + "options framework's own exception type"))
            .WithMessage("*IdleWaitTime*");

        tenant.Status.Should().Be(SchedulerStatus.Running,
            "building comes first precisely so that a recipe which no longer works leaves the scheduler that "
            + "is running exactly where it was");
        provider.GetRequiredService<ISchedulerRepository>().Lookup("acme").Should().BeSameAs(tenant);
    }

    [Test]
    public async Task RestartStartsTheNewSchedulerOnlyIfTheOldWasRunning()
    {
        await using ServiceProvider provider = Container(services => services.AddQuartz("main", _ => { }));

        IScheduler tenant = await Add(provider, "acme");
        await tenant.Standby();

        IScheduler second = await Restart(provider, "acme");

        second.Status.Should().Be(SchedulerStatus.Created,
            "a scheduler in standby is not firing, and a restart that started it would be a second decision "
            + "hidden inside the first");

        IScheduler third = await Restart(provider, "acme", new SchedulerRestartOptions { Start = true });

        third.Status.Should().Be(SchedulerStatus.Running, "saying so decides it deliberately");

        IScheduler fourth = await Restart(provider, "acme", SchedulerRestartOptions.WithoutStarting);

        fourth.Status.Should().Be(SchedulerStatus.Created, "and so does saying the other thing");
    }

    [Test]
    public async Task AShutDownTenantCanBeRestarted()
    {
        await using ServiceProvider provider = Container(services => services.AddQuartz("main", _ => { }));

        IScheduler tenant = await Add(provider, "acme");
        await tenant.Shutdown();

        IScheduler second = await Restart(provider, "acme");

        second.Should().NotBeSameAs(tenant);
        second.Status.Should().Be(SchedulerStatus.Created,
            "there was nothing running, so restarting is starting again rather than replacing");
        provider.GetRequiredService<ISchedulerRepository>().Lookup("acme").Should().BeSameAs(second);
    }

    /// <summary>
    /// Configuration written beside <c>AddQuartz</c> is not part of the recipe, and a restart is where
    /// that stops being a curiosity.
    /// </summary>
    /// <remarks>
    /// Generation one reads it, because it is the container's own scheduler and the container is where
    /// the setting was written. Generation two does not, because it is built from the registration and
    /// nothing in a service descriptor says which scheduler an application wrote a <c>Configure</c> for.
    /// Move the line inside the <c>AddQuartz("acme", …)</c> callback and both generations read it.
    /// </remarks>
    [Test]
    public async Task OptionsConfiguredBesideAddQuartzAreNotReplayed()
    {
        await using ServiceProvider provider = Container(services =>
        {
            services.AddQuartz("acme", _ => { });
            services.Configure<QuartzSchedulerOptions>("acme", o => o.InstanceId = "written-beside-the-call");
        });

        IScheduler first = await provider.GetRequiredKeyedService<ISchedulerFactory>("acme").GetScheduler();
        started.Add(first);

        first.SchedulerInstanceId.Should().Be("written-beside-the-call",
            "the container's own scheduler reads the container's options, whoever wrote them");

        IScheduler second = await Restart(provider, "acme");

        second.SchedulerInstanceId.Should().Be(QuartzSchedulerOptions.DefaultInstanceId,
            "the recipe is what AddQuartz was handed; the next generation is built in a container of its own, "
            + "which was never told about a Configure written outside the call");
    }

    [Test]
    public async Task QuerySchedulersKeepsOriginContainerAcrossARestart()
    {
        await using ServiceProvider provider = Container(services => services.AddQuartz("acme", _ => { }));

        IScheduler first = await provider.GetRequiredKeyedService<ISchedulerFactory>("acme").GetScheduler();
        started.Add(first);
        await first.Start();

        await Restart(provider, "acme");

        List<SchedulerRegistration> listing = await provider.GetRequiredService<ISchedulerRegistry>().QuerySchedulers();

        listing.Should().ContainSingle(x => x.Name == "acme",
            "a restarted registration is one scheduler, not one registration and one runtime tenant")
            .Which.Should().BeEquivalentTo(new
            {
                Name = "acme",
                Origin = SchedulerOrigin.Container,
                Status = SchedulerStatus.Running
            },
            "a restart changes which instances answer for a name; it does not change where the name came from");
    }

    [Test]
    public async Task RestartRefusesWhileTheHostIsStopping()
    {
        using CancellationTokenSource stopping = new();
        IHostApplicationLifetime lifetime = A.Fake<IHostApplicationLifetime>();
        A.CallTo(() => lifetime.ApplicationStopping).Returns(stopping.Token);

        await using ServiceProvider provider = Container(services =>
        {
            services.AddSingleton(lifetime);
            services.AddQuartz("main", _ => { });
        });

        IScheduler tenant = await Add(provider, "acme");
        await stopping.CancelAsync();

        Func<Task> restart = async () => await Restart(provider, "acme");

        (await restart.Should().ThrowAsync<SchedulerConfigException>())
            .WithMessage("*host is stopping*")
            .WithMessage("*was not restarted*");

        tenant.Status.Should().Be(SchedulerStatus.Running,
            "the refusal comes before the shutdown, so the host still has a scheduler to drain rather than a "
            + "name with nothing under it");
    }

    [Test]
    public async Task RestartRefusesABlankName()
    {
        await using ServiceProvider provider = Container(services => services.AddQuartz("main", _ => { }));

        Func<Task> restart = async () => await provider.GetRequiredService<ISchedulerRuntime>().Restart("  ");

        await restart.Should().ThrowAsync<ArgumentException>();
    }

    private async Task<IScheduler> Add(
        ServiceProvider provider,
        string schedulerName,
        Action<IQuartzBuilder> configure = null)
    {
        IScheduler scheduler = await provider.GetRequiredService<ISchedulerRuntime>().Add(schedulerName, configure);
        started.Add(scheduler);
        return scheduler;
    }

    private async Task<IScheduler> Restart(
        ServiceProvider provider,
        string schedulerName,
        SchedulerRestartOptions options = default)
    {
        IScheduler scheduler = await provider.GetRequiredService<ISchedulerRuntime>().Restart(schedulerName, options);
        started.Add(scheduler);
        return scheduler;
    }

    private static ServiceProvider Container(Action<IServiceCollection> configure)
    {
        ServiceCollection services = new();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddLogging();
        configure(services);
        return services.BuildServiceProvider();
    }

    /// <summary>
    /// A store built by the recipe, kept so a test can say whether the second generation got one of its
    /// own.
    /// </summary>
    private static IJobStore Record(List<IJobStore> stores, IServiceProvider provider)
    {
        IJobStore store = ActivatorUtilities.CreateInstance<RAMJobStore>(provider);
        lock (stores)
        {
            stores.Add(store);
        }

        return store;
    }

    /// <summary>
    /// Proves a scheduler still works by making it fire something, which is the only assertion a refusal
    /// that claims to have left it alone can be held to.
    /// </summary>
    private static async Task FiresAJob(IScheduler scheduler)
    {
        SignallingJob.Reset();

        await scheduler.ScheduleJob(
            JobBuilder.Create<SignallingJob>().WithIdentity("after-the-refusal").Build(),
            TriggerBuilder.Create().WithIdentity("after-the-refusal").StartNow().Build());

        await SignallingJob.Fired.WaitAsync(TimeSpan.FromSeconds(30));
    }

    private sealed class RestartJob : IJob
    {
        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
        {
            return default;
        }
    }

    private sealed class SignallingJob : IJob
    {
        private static TaskCompletionSource fired = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public static Task Fired => fired.Task;

        public static void Reset()
        {
            fired = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
        {
            fired.TrySetResult();
            return default;
        }
    }

    /// <summary>
    /// A store that is <see cref="IDisposable" />, which is what makes the refusal's timing observable:
    /// a container built for a refused generation would dispose it, and the running scheduler's store is
    /// the very object it holds.
    /// </summary>
    private sealed class RecordingJobStore : DelegatingJobStore, IDisposable
    {
        public RecordingJobStore(IJobStore inner) : base(inner)
        {
        }

        public bool Disposed { get; private set; }

        public int Initializations { get; private set; }

        public override ValueTask Initialize(SchedulerIdentity identity, CancellationToken cancellationToken = default)
        {
            Initializations++;
            return base.Initialize(identity, cancellationToken);
        }

        public void Dispose()
        {
            Disposed = true;
        }
    }

    /// <inheritdoc cref="RecordingJobStore" />
    private sealed class DisposableThreadPool : IThreadPool, IDisposable
    {
        public bool Disposed { get; private set; }

        public bool ShutdownCalled { get; private set; }

        public int PoolSize => 1;

        public ValueTask Initialize(CancellationToken cancellationToken = default) => default;

        public ValueTask<int> WaitForAvailableThreads(CancellationToken cancellationToken = default)
        {
            return new ValueTask<int>(ShutdownCalled ? 0 : 1);
        }

        public ValueTask<bool> TryRun(Func<ValueTask> action, CancellationToken cancellationToken = default)
        {
            return new ValueTask<bool>(false);
        }

        public ValueTask Shutdown(bool waitForJobsToComplete = true, CancellationToken cancellationToken = default)
        {
            ShutdownCalled = true;
            return default;
        }

        public void Dispose()
        {
            Disposed = true;
        }
    }
}
