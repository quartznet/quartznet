using System.Collections.Frozen;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using Quartz.Configuration;

namespace Quartz.Tests.Unit.Configuration;

/// <summary>
/// <see cref="SchedulerScopedServiceProvider" /> over two containers: a scheduler's own, and the
/// application's.
/// </summary>
/// <remarks>
/// <para>
/// This is the composite a scheduler added at runtime resolves through. The two containers are built
/// here by hand rather than through <c>SchedulerGeneration</c>, because what is under test is the
/// routing itself — which request each container answers, and whose scope a firing runs in — and that
/// is decided in this class rather than in whatever assembled the pair.
/// </para>
/// <para>
/// The DI platform declined to ship parent/child containers, and the reason it gives is exactly the
/// case the last two tests here pin down: the folk implementation resolves <c>IServiceScopeFactory</c>
/// from the child, so the per-execution scope is not the application's, and every application-scoped
/// service a job takes is leaked instead of disposed. So each of these is a rule that has to hold
/// rather than a preference.
/// </para>
/// </remarks>
public sealed class SchedulerScopedServiceProviderLinkTest
{
    [Test]
    public void AJobIsBuiltByTheApplicationEvenWhenTheSchedulersOwnContainerHasOne()
    {
        using ServiceProvider application = Application(services => services.AddSingleton(new LinkedJob("application")));
        using ServiceProvider tenant = Tenant(application, services => services.AddSingleton(new LinkedJob("tenant")));

        IServiceProvider composite = SchedulerScopedServiceProvider.For(tenant, "acme");

        composite.GetService<LinkedJob>()!.Origin.Should().Be("application",
            "a job's constructor takes the application's services, and a container resolves a service's "
            + "dependencies from itself - so a job built by the tenant's container would be built out of the "
            + "half of the graph that does not have them");
    }

    [Test]
    public void AnApplicationsOptionsTypeIsTheApplications()
    {
        using ServiceProvider application = Application(services =>
            services.Configure<LinkedOptions>(options => options.Owner = "application"));
        using ServiceProvider tenant = Tenant(application, _ => { });

        IServiceProvider composite = SchedulerScopedServiceProvider.For(tenant, "acme");

        composite.GetService<IOptions<LinkedOptions>>()!.Value.Owner.Should().Be("application",
            "nothing in the tenant's recipe configured these, so reading them there would answer with a "
            + "freshly defaulted instance nobody wrote");
    }

    [Test]
    public void AnOptionsTypeTheRecipeConfiguredIsTheTenants()
    {
        using ServiceProvider application = Application(services =>
            services.Configure<LinkedOptions>(options => options.Owner = "application"));
        using ServiceProvider tenant = Tenant(
            application,
            services => services.Configure<LinkedOptions>(options => options.Owner = "tenant"),
            configuredOptions: [typeof(LinkedOptions)]);

        IServiceProvider composite = SchedulerScopedServiceProvider.For(tenant, "acme");

        composite.GetService<IOptions<LinkedOptions>>()!.Value.Owner.Should().Be("tenant",
            "the tenant said something about this options type, and what a tenant says about its own "
            + "configuration is the whole point of giving it a recipe");
    }

    [Test]
    public void AQuartzOptionsTypeIsAlwaysTheTenants()
    {
        using ServiceProvider application = Application(services =>
            services.Configure<JobFactoryOptions>(options => options.ConfigureScope = (_, _, _) => { }));
        using ServiceProvider tenant = Tenant(application, _ => { });

        IServiceProvider composite = SchedulerScopedServiceProvider.For(tenant, "acme");

        composite.GetService<IOptions<JobFactoryOptions>>()!.Value.ConfigureScope.Should().BeNull(
            "a tenant's container holds exactly one scheduler, so Quartz's own options read there are that "
            + "scheduler's - reading them from the application would hand the tenant whatever the "
            + "application's own scheduler was configured with");
    }

    [Test]
    public void AnEnumerableComesFromWhicheverContainerDeclaresIt()
    {
        using ServiceProvider application = Application(services => services.AddSingleton(new LinkedNote("application")));
        using ServiceProvider tenant = Tenant(application, services => services.AddSingleton(new LinkedMark("tenant")));

        IServiceProvider composite = SchedulerScopedServiceProvider.For(tenant, "acme");

        composite.GetService<IEnumerable<LinkedNote>>().Should().ContainSingle().Which.Origin.Should().Be("application",
            "an empty sequence and 'this container has none of these' are the same answer, so the decision "
            + "is made on which container declares the item type rather than on what came back");
        composite.GetService<IEnumerable<LinkedMark>>().Should().ContainSingle().Which.Origin.Should().Be("tenant");
    }

    [Test]
    public void ThisSchedulersKeyMeansItsOwnContainerAndAnotherKeyMeansTheApplications()
    {
        using ServiceProvider application = Application(services =>
        {
            services.AddKeyedSingleton("acme", new LinkedNote("application-acme"));
            services.AddKeyedSingleton("initech", new LinkedNote("application-initech"));
        });
        using ServiceProvider tenant = Tenant(application, services =>
            services.AddKeyedSingleton("acme", new LinkedNote("tenant-acme")));

        IKeyedServiceProvider composite = (IKeyedServiceProvider) SchedulerScopedServiceProvider.For(tenant, "acme");

        composite.GetKeyedService<LinkedNote>("acme")!.Origin.Should().Be("tenant-acme",
            "this scheduler's own key is where its recipe put whatever it keyed");
        composite.GetKeyedService<LinkedNote>("initech")!.Origin.Should().Be("application-initech",
            "another key is another scheduler's, and the application is what holds those");
    }

    [Test]
    public void WhatCanBeSuppliedIsTheUnionOfBothContainers()
    {
        using ServiceProvider application = Application(services => services.AddSingleton(new LinkedNote("application")));
        using ServiceProvider tenant = Tenant(application, services => services.AddSingleton(new LinkedMark("tenant")));

        IServiceProviderIsService composite = (IServiceProviderIsService) SchedulerScopedServiceProvider.For(tenant, "acme");

        composite.IsService(typeof(LinkedNote)).Should().BeTrue(
            "ActivatorUtilities treats a service it is told does not exist as a parameter it cannot supply, "
            + "and this provider can supply it");
        composite.IsService(typeof(LinkedMark)).Should().BeTrue();
        composite.IsService(typeof(SchedulerScopedServiceProviderLinkTest)).Should().BeFalse();
    }

    [Test]
    public void AFiringsScopeIsTheApplicationsScope()
    {
        using ServiceProvider application = Application(services => services.AddScoped<LinkedUnitOfWork>());
        using ServiceProvider tenant = Tenant(application, _ => { });

        IServiceProvider composite = SchedulerScopedServiceProvider.For(tenant, "acme");

        LinkedUnitOfWork first;
        using (IServiceScope scope = composite.CreateScope())
        {
            first = scope.ServiceProvider.GetRequiredService<LinkedUnitOfWork>();
            scope.ServiceProvider.GetRequiredService<LinkedUnitOfWork>().Should().BeSameAs(first,
                "everything one firing resolves shares one scope, so a unit of work a job and a middleware "
                + "both take is one unit of work");
            first.Disposed.Should().BeFalse();
        }

        first.Disposed.Should().BeTrue(
            "the application's scope is disposed with the tenant's, so an application-scoped service a job "
            + "took is torn down when the job is returned rather than leaked for the life of the process");

        using IServiceScope second = composite.CreateScope();
        second.ServiceProvider.GetRequiredService<LinkedUnitOfWork>().Should().NotBeSameAs(first,
            "each firing gets its own scope");
    }

    [Test]
    public async Task AFiringsScopeIsDisposedAsynchronouslyThroughBothContainers()
    {
        using ServiceProvider application = Application(services => services.AddScoped<LinkedUnitOfWork>());
        using ServiceProvider tenant = Tenant(application, services => services.AddScoped<LinkedUnitOfWork>());

        IServiceProvider composite = SchedulerScopedServiceProvider.For(tenant, "acme");

        IServiceScope scope = composite.CreateScope();
        LinkedUnitOfWork resolved = scope.ServiceProvider.GetRequiredService<LinkedUnitOfWork>();

        await ((IAsyncDisposable) scope).DisposeAsync();

        resolved.Disposed.Should().BeTrue("jobs are torn down through IAsyncDisposable, and both scopes are");
    }

    [Test]
    public void ASchedulerWithNoApplicationBehindItResolvesExactlyAsItDidBefore()
    {
        ServiceCollection services = new();
        services.AddSingleton(new LinkedNote("container"));

        using ServiceProvider provider = services.BuildServiceProvider();
        IServiceProvider composite = SchedulerScopedServiceProvider.For(provider, "acme");

        composite.GetService<LinkedNote>()!.Origin.Should().Be("container");
        composite.GetService<LinkedMark>().Should().BeNull(
            "with nothing linked there is no second container to fall back to, which is the container-registered "
            + "scheduler's path and has to stay what it was");

        using IServiceScope scope = composite.CreateScope();
        scope.ServiceProvider.GetService<LinkedNote>()!.Origin.Should().Be("container");
    }

    private static ServiceProvider Application(Action<IServiceCollection> configure)
    {
        ServiceCollection services = new();
        services.AddOptions();
        configure(services);
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }

    /// <summary>
    /// The container one scheduler lives in, linked back to the application's — the shape
    /// <c>SchedulerGeneration</c> builds.
    /// </summary>
    private static ServiceProvider Tenant(
        IServiceProvider application,
        Action<IServiceCollection> configure,
        Type[] configuredOptions = null)
    {
        ServiceCollection services = new();
        services.AddOptions();
        configure(services);
        services.AddSingleton(new ApplicationLink(application)
        {
            ConfiguredOptions = (configuredOptions ?? []).ToFrozenSet()
        });

        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }

    private sealed class LinkedJob : IJob
    {
        public LinkedJob(string origin) => Origin = origin;

        public string Origin { get; }

        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
    }

    private sealed class LinkedNote
    {
        public LinkedNote(string origin) => Origin = origin;

        public string Origin { get; }
    }

    private sealed class LinkedMark
    {
        public LinkedMark(string origin) => Origin = origin;

        public string Origin { get; }
    }

    private sealed class LinkedOptions
    {
        public string Owner { get; set; }
    }

    private sealed class LinkedUnitOfWork : IDisposable, IAsyncDisposable
    {
        public bool Disposed { get; private set; }

        public void Dispose() => Disposed = true;

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return default;
        }
    }
}
