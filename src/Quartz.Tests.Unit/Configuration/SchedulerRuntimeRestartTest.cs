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

        using ServiceProvider provider = Container(services => services.AddQuartz("main", _ => { }));

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

        using ServiceProvider provider = Container(services => services.AddQuartz("acme", q =>
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
    }

    /// <summary>
    /// A registration nothing has built yet is restarted by simply building it.
    /// </summary>
    [Test]
    public async Task RestartOfARegisteredButUnbuiltSchedulerBuildsIt()
    {
        using ServiceProvider provider = Container(services => services.AddQuartz("acme", _ => { }));

        IScheduler scheduler = await Restart(provider, "acme");

        scheduler.SchedulerName.Should().Be("acme");
        scheduler.Status.Should().Be(SchedulerStatus.Created,
            "nothing was running under that name, so there was nothing whose running to preserve");
    }

    [Test]
    public async Task RestartRefusesTheDefaultScheduler()
    {
        using ServiceProvider provider = Container(services =>
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
        using ServiceProvider provider = Container(services => services.AddQuartz("main", _ => { }));

        Func<Task> restart = async () => await Restart(provider, "nobody");

        (await restart.Should().ThrowAsync<SchedulerNotFoundException>(
                "there is no recipe to replay, and 'no such scheduler' is the one failure a caller can act on "
                + "without changing anything"))
            .Which.SchedulerName.Should().Be("nobody");
    }

    /// <summary>
    /// A recipe that closes over an object cannot produce a second generation, and says so instead of
    /// handing the new scheduler the instance the old one shut down.
    /// </summary>
    [Test]
    public async Task RestartRefusesARecipeThatSuppliesAJobStoreInstance()
    {
        IJobStore shared = TestJobStores.Ram();

        using ServiceProvider provider = Container(services => services.AddQuartz("main", _ => { }));

        IScheduler tenant = await Add(provider, "acme", q => q.UseJobStore(shared));

        Func<Task> restart = async () => await Restart(provider, "acme");

        (await restart.Should().ThrowAsync<SchedulerConfigException>(
                "replaying the recipe hands the new scheduler the object the old one is about to shut down, and "
                + "an instance is the one thing a recipe cannot produce twice"))
            .WithMessage("*a job store as an instance*")
            .WithMessage("*UseJobStore(IJobStore)*")
            .WithMessage("*still running*");

        tenant.Status.Should().Be(SchedulerStatus.Running,
            "the refusal is decided before anything is shut down, so a caller that gets it has lost nothing");
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

        using ServiceProvider provider = Container(services => services.AddQuartz("main", _ => { }));

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
        using ServiceProvider provider = Container(services => services.AddQuartz("main", _ => { }));

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
        using ServiceProvider provider = Container(services => services.AddQuartz("main", _ => { }));

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
        using ServiceProvider provider = Container(services =>
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
        using ServiceProvider provider = Container(services => services.AddQuartz("acme", _ => { }));

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

        using ServiceProvider provider = Container(services =>
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
        using ServiceProvider provider = Container(services => services.AddQuartz("main", _ => { }));

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

    private sealed class RestartJob : IJob
    {
        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
        {
            return default;
        }
    }
}
