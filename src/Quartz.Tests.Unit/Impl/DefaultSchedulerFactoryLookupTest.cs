using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using Quartz.Extensibility;

namespace Quartz.Tests.Unit.Impl;

/// <summary>
/// What <see cref="ISchedulerFactory.LookupScheduler" /> can find, now that a registration is enough.
/// </summary>
/// <remarks>
/// The lookup used to answer only from the repository for any name but its own factory's, so under a
/// multi-tenant registration "give me tenant acme" depended on whether something else had happened to
/// resolve acme first — the same call returning a scheduler or <see langword="null" /> according to what
/// the rest of the application had done. What makes a scheduler exist is the registration, so that is
/// what the lookup reads.
/// </remarks>
[NonParallelizable]
public sealed class DefaultSchedulerFactoryLookupTest
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
    public async Task LookupSchedulerBuildsARegisteredButUnbuiltScheduler()
    {
        using ServiceProvider provider = Container(services =>
        {
            services.AddQuartz("acme", _ => { });
            services.AddQuartz("initech", _ => { });
        });

        ISchedulerRepository repository = provider.GetRequiredService<ISchedulerRepository>();
        repository.LookupAll().Should().BeEmpty("the premise is that nothing has been built");

        ISchedulerFactory acme = provider.GetRequiredKeyedService<ISchedulerFactory>("acme");

        IScheduler initech = await Track(await acme.LookupScheduler("initech"));

        initech.Should().NotBeNull(
            "the registration is what says the scheduler exists, so asking one tenant's factory for "
            + "another's name builds it rather than answering that there is no such scheduler");
        initech.SchedulerName.Should().Be("initech");

        repository.Lookup("initech").Should().BeSameAs(initech,
            "it was built through its own factory, so it is bound exactly as it would have been had the "
            + "container resolved it");

        (await acme.LookupScheduler("INITECH")).Should().BeSameAs(initech,
            "the comparison ignores case, because that is how the repository indexes names");
    }

    [Test]
    public async Task LookupSchedulerBuildsTheDefaultSchedulerFromANamedOnesFactory()
    {
        using ServiceProvider provider = Container(services =>
        {
            services.AddQuartz(q => q.ConfigureScheduler(o => o.InstanceName = "TheDefaultOne"));
            services.AddQuartz("acme", _ => { });
        });

        ISchedulerFactory acme = provider.GetRequiredKeyedService<ISchedulerFactory>("acme");

        IScheduler standard = await Track(await acme.LookupScheduler("TheDefaultOne"));

        standard.Should().NotBeNull(
            "the default scheduler is the one registration whose name is not the name it was registered "
            + "under - it has no service key at all - so its options are read to learn it");
        standard.SchedulerName.Should().Be("TheDefaultOne");
    }

    [Test]
    public async Task LookupSchedulerStillAnswersNullForANameNothingRegistered()
    {
        using ServiceProvider provider = Container(services => services.AddQuartz("acme", _ => { }));

        ISchedulerFactory acme = provider.GetRequiredKeyedService<ISchedulerFactory>("acme");

        (await acme.LookupScheduler("nobody")).Should().BeNull();

        provider.GetRequiredService<ISchedulerRepository>().LookupAll().Should().BeEmpty(
            "a name nothing registered builds nothing at all, so the lookup is still the read it says it is");
    }

    [Test]
    public async Task LookupSchedulerDoesNotBuildADownRuntimeTenant()
    {
        using ServiceProvider provider = Container(services => services.AddQuartz("acme", _ => { }));

        ISchedulerFactory acme = provider.GetRequiredKeyedService<ISchedulerFactory>("acme");
        IScheduler tenant = await Track(await provider.GetRequiredService<ISchedulerRuntime>().Add("initech"));

        (await acme.LookupScheduler("initech")).Should().BeSameAs(tenant,
            "while it is alive it is in the repository, which is where the lookup finds it");

        await tenant.Shutdown();

        (await acme.LookupScheduler("initech")).Should().BeNull(
            "a scheduler added at runtime has no registration to be rebuilt from - its parts were one set "
            + "of instances in a container of their own - so adding it again is ISchedulerRuntime's "
            + "rather than something a lookup does behind the caller's back");
    }

    private Task<IScheduler> Track(IScheduler scheduler)
    {
        if (scheduler is not null)
        {
            started.Add(scheduler);
        }

        return Task.FromResult(scheduler);
    }

    private static ServiceProvider Container(Action<IServiceCollection> configure)
    {
        ServiceCollection services = new();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddLogging();
        configure(services);
        return services.BuildServiceProvider();
    }
}
