using FakeItEasy;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using Quartz.Configuration;
using Quartz.Extensibility;

namespace Quartz.Tests.Unit.Configuration;

/// <summary>
/// Adding and removing schedulers in a container that has already been built.
/// </summary>
/// <remarks>
/// Two halves. The first is that a scheduler added here is an ordinary scheduler: built, bound into the
/// container's repository, started, listed, and visible to everything that reads the repository. The
/// second — most of the tests — is the refusals, because the value of this API is decided by what it
/// says no to. A name already spoken for must be refused where the caller can read why, rather than
/// raced into the repository's own duplicate check, and a failed add must leave nothing behind at all:
/// runtime fails soft, and the only thing that hears about it is the caller.
/// </remarks>
[NonParallelizable]
public sealed class SchedulerRuntimeTest
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
    public async Task AddBuildsBindsAndStartsATenant()
    {
        using ServiceProvider provider = Container(services => services.AddQuartz("main", _ => { }));

        IScheduler tenant = await Add(provider, "acme");

        tenant.SchedulerName.Should().Be("acme");
        tenant.Status.Should().Be(SchedulerStatus.Running, "a scheduler added at runtime is started by default");

        provider.GetRequiredService<ISchedulerRepository>().Lookup("acme").Should().BeSameAs(tenant,
            "binding it into the container's own repository is what makes the HTTP API, the dashboard and "
            + "GetAllSchedulers see it without any of them knowing it arrived late");

        List<SchedulerRegistration> listing = await provider.GetRequiredService<ISchedulerRegistry>().QuerySchedulers();
        listing.Should().ContainSingle(x => x.Name == "acme").Which.Should().BeEquivalentTo(
            new { Name = "acme", Origin = SchedulerOrigin.Runtime, Status = SchedulerStatus.Running });
    }

    [Test]
    public async Task AddWithStartFalseLeavesTheTenantCreated()
    {
        using ServiceProvider provider = Container(services => services.AddQuartz("main", _ => { }));

        IScheduler tenant = await Add(provider, "acme", options: SchedulerAddOptions.WithoutStarting);

        tenant.Status.Should().Be(SchedulerStatus.Created,
            "the starting policy is the whole of what this option decides, and CreateWithoutStarting means the "
            + "application presses start");
        provider.GetRequiredService<ISchedulerRepository>().Lookup("acme").Should().BeSameAs(tenant,
            "a scheduler that has not been started is still bound, exactly as one the hosted service created "
            + "with AutoStart false is");
    }

    [Test]
    public async Task AddRefusesAContainerRegisteredName()
    {
        using ServiceProvider provider = Container(services => services.AddQuartz("acme", _ => { }));

        Func<Task> add = async () => await Add(provider, "ACME");

        (await add.Should().ThrowAsync<SchedulerConfigException>(
                "the container registered that name, and its parts are already registered under it - a second "
                + "set would be two schedulers wearing one name"))
            .WithMessage("*registered with the container*")
            .WithMessage("*GetRequiredKeyedService<ISchedulerFactory>(\"acme\")*",
                "the message names the spelling the container used, so the caller can copy it");
    }

    [Test]
    public async Task AddRefusesTheDefaultSchedulersName()
    {
        using ServiceProvider provider = Container(services =>
            services.AddQuartz(q => q.ConfigureScheduler(o => o.InstanceName = "TheDefaultOne")));

        Func<Task> add = async () => await Add(provider, "TheDefaultOne");

        await add.Should().ThrowAsync<SchedulerConfigException>(
            "the default scheduler is the one registration whose name is not the name it was registered "
            + "under - it has no service key at all - so the name has to be read out of its options");
    }

    [Test]
    public async Task AddRefusesABoundScheduler()
    {
        using ServiceProvider provider = Container(services => services.AddQuartz("main", _ => { }));

        IScheduler standalone = await QuartzSchedulerBuilder
            .Create(q => q.ConfigureScheduler(o => o.InstanceName = "bound-by-hand"))
            .BuildScheduler();
        started.Add(standalone);

        provider.GetRequiredService<ISchedulerRepository>().Bind(standalone);

        Func<Task> add = async () => await Add(provider, "bound-by-hand");

        (await add.Should().ThrowAsync<SchedulerConfigException>(
                "a scheduler bound by hand, or one AddQuartzHttpClient bound, occupies the name as surely as a "
                + "registration does"))
            .WithMessage("*already bound in this container's repository*");
    }

    [Test]
    public async Task AddRefusesANameAlreadyAdded()
    {
        using ServiceProvider provider = Container(services => services.AddQuartz("main", _ => { }));

        await Add(provider, "acme");

        Func<Task> again = async () => await Add(provider, "acme");

        (await again.Should().ThrowAsync<SchedulerConfigException>(
                "a scheduler's thread pool and job store cannot be replaced underneath it, which is why there "
                + "is no way to add over one"))
            .WithMessage("*has already been added at runtime*")
            .WithMessage("*Remove(\"acme\")*");
    }

    [Test]
    public async Task AddRefusesWhileTheHostIsStopping()
    {
        using CancellationTokenSource stopping = new();
        IHostApplicationLifetime lifetime = A.Fake<IHostApplicationLifetime>();
        A.CallTo(() => lifetime.ApplicationStopping).Returns(stopping.Token);

        using ServiceProvider provider = Container(services =>
        {
            services.AddSingleton(lifetime);
            services.AddQuartz("main", _ => { });
        });

        await stopping.CancelAsync();

        Func<Task> add = async () => await Add(provider, "acme");

        (await add.Should().ThrowAsync<SchedulerConfigException>(
                "a scheduler created after the shutdown that would have stopped it is one nothing comes back for"))
            .WithMessage("*host is stopping*");

        provider.GetRequiredService<ISchedulerRepository>().LookupAll().Should().BeEmpty();
    }

    [Test]
    public async Task AFailedAddRetainsNothing()
    {
        using ServiceProvider provider = Container(services => services.AddQuartz("main", _ => { }));

        ISchedulerRegistry registry = provider.GetRequiredService<ISchedulerRegistry>();
        List<SchedulerRegistration> before = await registry.QuerySchedulers();

        Func<Task> add = async () => await Add(
            provider,
            "acme",
            options: new SchedulerAddOptions
            {
                Properties = new Dictionary<string, string> { ["quartz.thisKeyIsNotRead"] = "true" }
            });

        await add.Should().ThrowAsync<SchedulerConfigException>(
            "the property bag is checked exactly as AddQuartz(name, properties) checks it, so a misspelling "
            + "is reported rather than ignored");

        List<SchedulerRegistration> after = await registry.QuerySchedulers();
        after.Should().BeEquivalentTo(before,
            "runtime fails soft: only the caller hears about it, and half a tenant in the listing would be "
            + "worse than none because nothing would ever come back for it");

        provider.GetRequiredService<ISchedulerRepository>().LookupAll().Should().BeEmpty();
    }

    [Test]
    public async Task AddRefusesSettingsGivenTwoWaysAtOnce()
    {
        using ServiceProvider provider = Container(services => services.AddQuartz("main", _ => { }));

        Func<Task> add = async () => await Add(
            provider,
            "acme",
            options: new SchedulerAddOptions
            {
                Properties = new Dictionary<string, string> { ["quartz.scheduler.instanceId"] = "one" },
                Configuration = new ConfigurationBuilder().Build()
            });

        (await add.Should().ThrowAsync<SchedulerConfigException>(
                "a section says everything a property bag does, so honouring one of them would drop the "
                + "other without a word - which is what AddQuartzSchedulers refuses in the same words"))
            .WithMessage("*Use one or the other*");

        provider.GetRequiredService<ISchedulerRepository>().LookupAll().Should().BeEmpty();
    }

    [Test]
    public async Task ConfigureAllQuartzSchedulersReachesARuntimeTenant()
    {
        using ServiceProvider provider = Container(services =>
        {
            services.AddQuartz("main", _ => { });
            services.ConfigureAllQuartzSchedulers(q => q.ConfigureScheduler(o => o.Context["applied"] = "yes"));
        });

        IScheduler tenant = await Add(provider, "acme");

        tenant.Context.Should().ContainKey("applied").WhoseValue.Should().Be("yes",
            "ConfigureAllQuartzSchedulers says 'every scheduler in this container', and a tenant bound into "
            + "this container's repository is one of them - a package that adds something to every scheduler "
            + "cannot know which door a scheduler came through");
    }

    [Test]
    public async Task AddJobTypeWithAnImplementationMappingIsRefusedForARuntimeTenant()
    {
        using ServiceProvider provider = Container(services => services.AddQuartz("main", _ => { }));

        Func<Task> add = async () => await Add(
            provider,
            "acme",
            configure: q => q.AddJobType<RuntimeJob, DerivedRuntimeJob>());

        (await add.Should().ThrowAsync<SchedulerConfigException>(
                "a mapping nothing reads is worse than a mapping refused: the tenant would run the type the "
                + "recipe replaced, and nothing would say so"))
            .WithMessage("*does not build its own jobs*")
            .WithMessage($"*{typeof(RuntimeJob).FullName}*");

        provider.GetRequiredService<ISchedulerRepository>().LookupAll().Should().BeEmpty();
    }

    [Test]
    public async Task RemoveShutsDownUnbindsAndForgets()
    {
        using ServiceProvider provider = Container(services => services.AddQuartz("main", _ => { }));

        IScheduler tenant = await Add(provider, "acme");

        ISchedulerRuntime runtime = provider.GetRequiredService<ISchedulerRuntime>();
        (await runtime.Remove("acme")).Should().BeTrue();

        tenant.Status.Should().Be(SchedulerStatus.Shutdown);
        provider.GetRequiredService<ISchedulerRepository>().Lookup("acme").Should().BeNull(
            "a scheduler unbinds itself from the repository when it shuts down");

        List<SchedulerRegistration> listing = await provider.GetRequiredService<ISchedulerRegistry>().QuerySchedulers();
        listing.Should().NotContain(x => x.Name == "acme",
            "removing forgets the unit, which is what tells a shut-down tenant waiting to be removed from one "
            + "that has been");

        (await runtime.Remove("acme")).Should().BeFalse(
            "a name this runtime does not hold is not an error - removing twice says what happened rather "
            + "than throwing");
    }

    [Test]
    public async Task RemoveRefusesAContainerName()
    {
        using ServiceProvider provider = Container(services => services.AddQuartz("acme", _ => { }));

        Func<Task> remove = async () => await provider.GetRequiredService<ISchedulerRuntime>().Remove("acme");

        (await remove.Should().ThrowAsync<SchedulerConfigException>(
                "the container owns the parts of what it registered, and disposing them is the container's "
                + "business rather than this runtime's"))
            .WithMessage("*IScheduler.Shutdown()*");
    }

    [Test]
    public async Task AShutDownTenantIsListedWithANullStatusUntilRemoved()
    {
        using ServiceProvider provider = Container(services => services.AddQuartz("main", _ => { }));

        IScheduler tenant = await Add(provider, "acme");
        await tenant.Shutdown();

        ISchedulerRegistry registry = provider.GetRequiredService<ISchedulerRegistry>();

        List<SchedulerRegistration> listing = await registry.QuerySchedulers();
        SchedulerRegistration listed = listing.Should().ContainSingle(x => x.Name == "acme",
            "the repository drops a shut-down scheduler, and the registrations never knew about this one - so "
            + "without the runtime's own list it would vanish while Remove still had work to do").Subject;
        listed.Origin.Should().Be(SchedulerOrigin.Runtime);
        listed.Status.Should().BeNull("there is no live scheduler under this name any more");

        (await provider.GetRequiredService<ISchedulerRuntime>().Remove("acme")).Should().BeTrue(
            "shutting a tenant down by hand does not remove it - its container is still there to release");

        (await registry.QuerySchedulers()).Should().NotContain(x => x.Name == "acme");
    }

    [Test]
    public async Task TheDefaultRegistryListingIsUnchangedForAContainerWithNoRuntimeTenants()
    {
        using ServiceProvider provider = Container(services =>
        {
            services.AddQuartz(q => q.ConfigureScheduler(o => o.InstanceName = "TheDefaultOne"));
            services.AddQuartz("acme", _ => { });
        });

        ISchedulerRegistry registry = provider.GetRequiredService<ISchedulerRegistry>();

        registry.Should().BeOfType<SchedulerRuntime>(
            "one object answers both interfaces, because a listing that omitted the runtime tenants would be "
            + "wrong and a second registry to merge with the first is how it would come to be wrong");

        List<SchedulerRegistration> listing = await registry.QuerySchedulers();

        listing.Select(x => x.Name).Should().Equal(["TheDefaultOne", "acme"],
            "a container with no runtime tenants answers exactly what it answered before this existed");
        listing.Should().OnlyContain(x => x.Origin == SchedulerOrigin.Container && x.Status == null);
    }

    private async Task<IScheduler> Add(
        ServiceProvider provider,
        string schedulerName,
        Action<IQuartzBuilder> configure = null,
        SchedulerAddOptions options = default)
    {
        IScheduler scheduler = await provider.GetRequiredService<ISchedulerRuntime>()
            .Add(schedulerName, configure, options);

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

    private class RuntimeJob : IJob
    {
        public virtual ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
        {
            return default;
        }
    }

    private sealed class DerivedRuntimeJob : RuntimeJob;
}
