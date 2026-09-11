using System.Diagnostics;

using FakeItEasy;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using Quartz.Configuration;
using Quartz.Extensibility;
using Quartz.Impl;

namespace Quartz.Tests.Unit.Extensions.DependencyInjection;

/// <summary>
/// <see cref="ISchedulerRegistry" /> is the read that separates <em>registered</em> from
/// <em>running</em>: an operator has to be able to enumerate tenants without starting every one of
/// them, which is precisely what <c>LookupAll</c> and <c>GetAllSchedulers</c> cannot do.
/// </summary>
[NonParallelizable]
public sealed class SchedulerRegistryTest
{
    [Test]
    public async Task ARegisteredSchedulerIsListedWithoutBeingCreated()
    {
        using ServiceProvider provider = Container(services =>
        {
            services.AddQuartz("acme", _ => { });
            services.AddQuartz("initech", _ => { });
        });

        List<SchedulerRegistration> registrations = await provider.GetRequiredService<ISchedulerRegistry>().QuerySchedulers();

        registrations.Select(x => x.Name).Should().Equal(["acme", "initech"]);
        registrations.Should().OnlyContain(x => x.Origin == SchedulerOrigin.Container);
        registrations.Should().OnlyContain(x => x.Status == null && !x.IsCreated,
            "asking what is registered must not build anything - starting every tenant is the cost this query exists to avoid");

        provider.GetRequiredService<ISchedulerRepository>().LookupAll().Should().BeEmpty(
            "nothing was created, so the repository - which is what GetAllSchedulers reads - still knows about nothing");
    }

    [Test]
    public async Task CreatingOneSchedulerLeavesTheOthersRegisteredAndUncreated()
    {
        using ServiceProvider provider = Container(services =>
        {
            services.AddQuartz("acme", _ => { });
            services.AddQuartz("initech", _ => { });
        });

        await provider.GetRequiredKeyedService<ISchedulerFactory>("acme").GetScheduler();

        List<SchedulerRegistration> registrations = await provider.GetRequiredService<ISchedulerRegistry>().QuerySchedulers();

        registrations.Should().ContainSingle(x => x.Name == "acme")
            .Which.Status.Should().Be(SchedulerStatus.Created,
                "a scheduler that has been built and never started reports the state it is actually in, rather "
                + "than the standby a started scheduler is stood down into");
        registrations.Should().ContainSingle(x => x.Name == "initech")
            .Which.Status.Should().BeNull("the second tenant is still only a registration");
    }

    [Test]
    public async Task AStartedSchedulerIsReportedAsRunningAndAStandbyOneAsStandby()
    {
        using ServiceProvider provider = Container(services => services.AddQuartz("acme", _ => { }));

        IScheduler scheduler = await provider.GetRequiredKeyedService<ISchedulerFactory>("acme").GetScheduler();
        await scheduler.Start();

        ISchedulerRegistry registry = provider.GetRequiredService<ISchedulerRegistry>();
        List<SchedulerRegistration> started = await registry.QuerySchedulers();
        started.Should().ContainSingle().Which.Status.Should().Be(SchedulerStatus.Running);

        await scheduler.Standby();

        List<SchedulerRegistration> standby = await registry.QuerySchedulers();
        standby.Should().ContainSingle().Which.Status.Should().Be(SchedulerStatus.Standby);

        await scheduler.Shutdown();
    }

    [Test]
    public async Task TheDefaultSchedulerIsListedUnderTheNameItsOptionsGiveIt()
    {
        using ServiceProvider provider = Container(services =>
        {
            services.AddQuartz(q => q.ConfigureScheduler(o => o.InstanceName = "TheDefaultOne"));
            services.AddQuartz("acme", _ => { });
        });

        List<SchedulerRegistration> registrations = await provider.GetRequiredService<ISchedulerRegistry>().QuerySchedulers();

        registrations.Select(x => x.Name).Should().Equal(
            ["TheDefaultOne", "acme"],
            "the default scheduler has no service key, so its name comes from its options - and the order is ordinal by name");
        registrations.Should().OnlyContain(x => x.Origin == SchedulerOrigin.Container);
    }

    [Test]
    public async Task AContainerWithOnlyNamedSchedulersDoesNotInventADefaultOne()
    {
        using ServiceProvider provider = Container(services => services.AddQuartz("acme", _ => { }));

        List<SchedulerRegistration> registrations = await provider.GetRequiredService<ISchedulerRegistry>().QuerySchedulers();

        registrations.Select(x => x.Name).Should().Equal(
            ["acme"],
            "AddQuartz(name, ...) registers no default scheduler, and QuartzScheduler is the name one would otherwise have had");
    }

    [Test]
    public async Task ASchedulerBoundByHandIsReportedAsRuntime()
    {
        using ServiceProvider provider = Container(services => services.AddQuartz("acme", _ => { }));

        IScheduler standalone = await QuartzSchedulerBuilder
            .Create(q => q.ConfigureScheduler(o => o.InstanceName = "bound-by-hand"))
            .BuildScheduler();

        try
        {
            provider.GetRequiredService<ISchedulerRepository>().Bind(standalone);

            List<SchedulerRegistration> registrations = await provider.GetRequiredService<ISchedulerRegistry>().QuerySchedulers();

            registrations.Should().ContainSingle(x => x.Name == "acme")
                .Which.Origin.Should().Be(SchedulerOrigin.Container);
            registrations.Should().ContainSingle(x => x.Name == "bound-by-hand")
                .Which.Origin.Should().Be(SchedulerOrigin.Runtime,
                    "nothing in this container registered it, so nothing in this container owns its lifetime");
        }
        finally
        {
            await standalone.Shutdown();
        }
    }

    [Test]
    public async Task ARegistrationAndTheSchedulerBuiltFromItAreOneEntry()
    {
        using ServiceProvider provider = Container(services => services.AddQuartz("Acme", _ => { }));

        IScheduler standalone = await QuartzSchedulerBuilder
            .Create(q => q.ConfigureScheduler(o => o.InstanceName = "ACME"))
            .BuildScheduler();

        try
        {
            provider.GetRequiredService<ISchedulerRepository>().Bind(standalone);

            List<SchedulerRegistration> registrations = await provider.GetRequiredService<ISchedulerRegistry>().QuerySchedulers();

            SchedulerRegistration registration = registrations.Should().ContainSingle(
                "the repository indexes names ignoring case, so the join against it has to as well - otherwise one "
                + "scheduler is listed twice, once as a registration and once as a stranger").Subject;
            registration.Name.Should().Be("Acme", "a registration is reported with the spelling it was registered with");
            registration.Origin.Should().Be(SchedulerOrigin.Container);
        }
        finally
        {
            await standalone.Shutdown();
        }
    }

    [Test]
    public async Task AShutDownSchedulerLeavesItsRegistrationBehindWithNoStatus()
    {
        using ServiceProvider provider = Container(services => services.AddQuartz("acme", _ => { }));

        IScheduler scheduler = await provider.GetRequiredKeyedService<ISchedulerFactory>("acme").GetScheduler();
        await scheduler.Shutdown();

        List<SchedulerRegistration> registrations = await provider.GetRequiredService<ISchedulerRegistry>().QuerySchedulers();

        SchedulerRegistration registration = registrations.Should().ContainSingle(
            "shutting a scheduler down does not unregister it - the registration is what the container was told").Subject;
        registration.Origin.Should().Be(SchedulerOrigin.Container);
        registration.Status.Should().BeNull(
            "the repository drops a shut-down scheduler as soon as a read notices it, and one cannot be rebuilt in "
            + "the same container either way, so null covers both 'not yet' and 'not any more'");
    }

    [Test]
    public async Task RegisteringTheGraphDirectlyStillCountsAsADefaultScheduler()
    {
        using ServiceProvider provider = Container(services => services.AddQuartzScheduler());

        List<SchedulerRegistration> registrations = await provider.GetRequiredService<ISchedulerRegistry>().QuerySchedulers();

        SchedulerRegistration registration = registrations.Should().ContainSingle(
            "AddQuartzScheduler() is the call every road to a default scheduler passes through - AddQuartz(), "
            + "the standalone builder, and registering the graph by hand").Subject;
        registration.Name.Should().Be(QuartzSchedulerOptions.DefaultInstanceName);
        registration.Origin.Should().Be(SchedulerOrigin.Container);
        registration.Status.Should().BeNull();
    }

    [Test]
    public async Task ASchedulerThatCannotAnswerIsListedRatherThanThrown()
    {
        using ServiceProvider provider = Container(services => services.AddQuartz("acme", _ => { }));

        IScheduler unreachable = A.Fake<IScheduler>();
        A.CallTo(() => unreachable.SchedulerName).Returns("far-away");
        A.CallTo(() => unreachable.GetStatus(A<CancellationToken>._))
            .Throws(new HttpRequestException("the remote scheduler is not answering"));

        provider.GetRequiredService<ISchedulerRepository>().Bind(unreachable, "remote");

        List<SchedulerRegistration> registrations = await provider.GetRequiredService<ISchedulerRegistry>().QuerySchedulers();

        SchedulerRegistration listed = registrations.Should().ContainSingle(x => x.Name == "far-away").Subject;
        listed.Status.Should().Be(SchedulerStatus.Unknown,
            "unreachable is not the same as absent, and an inventory of tenants must not fail because one of "
            + "them is behind a network that is down");
        listed.SchedulerInstanceId.Should().BeNull("the scheduler that would have said which node it is could not be reached");
    }

    [Test]
    public async Task AProxyIsListedAsRemoteWithTheNodeItAnswered()
    {
        using ServiceProvider provider = Container(services => services.AddQuartz("acme", _ => { }));

        IScheduler target = A.Fake<IScheduler>();
        A.CallTo(() => target.SchedulerName).Returns("far-away");
        A.CallTo(() => target.GetStatus(A<CancellationToken>._)).Returns(new ValueTask<SchedulerStatus>(SchedulerStatus.Running));
        A.CallTo(() => target.GetSchedulerInstanceId(A<CancellationToken>._)).Returns(new ValueTask<string>("far-away-node-1"));

        provider.GetRequiredService<ISchedulerRepository>().Bind(new TestProxyScheduler(target), "far-away");

        List<SchedulerRegistration> registrations = await provider.GetRequiredService<ISchedulerRegistry>().QuerySchedulers();

        SchedulerRegistration listed = registrations.Should().ContainSingle(x => x.Name == "far-away").Subject;
        listed.Origin.Should().Be(SchedulerOrigin.Remote,
            "a scheduler reached through a proxy runs in somebody else's process, which is what a reader has to be told");
        listed.Status.Should().Be(SchedulerStatus.Running);
        listed.SchedulerInstanceId.Should().Be("far-away-node-1",
            "the registration carries the node so that nothing downstream has to read the blocking property for it");
    }

    /// <summary>
    /// A target that never answers costs the listing the deadline and nothing more.
    /// </summary>
    /// <remarks>
    /// The two members are the ones an unreachable <c>HttpScheduler</c> leaves pending until its
    /// <see cref="HttpClient.Timeout" /> — 100 seconds out of the box — which is what a page rendering
    /// the schedulers of a process would otherwise wait for.
    /// </remarks>
    [Test]
    public async Task AnUnreachableProxyCostsTheListingTheDeadlineAndNoMore()
    {
        using ServiceProvider provider = Container(services => services.AddQuartz("acme", _ => { }));

        provider.GetRequiredService<ISchedulerRepository>().Bind(new StallingProxyScheduler("far-away"), "far-away");

        long started = Stopwatch.GetTimestamp();
        List<SchedulerRegistration> registrations = await provider.GetRequiredService<ISchedulerRegistry>().QuerySchedulers();
        TimeSpan elapsed = Stopwatch.GetElapsedTime(started);

        elapsed.Should().BeLessThan(ContainerSchedulerRegistry.StatusTimeout + TimeSpan.FromSeconds(5),
            "the listing asks under a deadline of its own rather than under the client's timeout");

        SchedulerRegistration listed = registrations.Should().ContainSingle(x => x.Name == "far-away").Subject;
        listed.Origin.Should().Be(SchedulerOrigin.Remote);
        listed.Status.Should().Be(SchedulerStatus.Unknown, "the target never said, and saying nothing is what Unknown is for");
        listed.SchedulerInstanceId.Should().BeNull();
    }


    [Test]
    public async Task ARuntimeTenantIsListedWithOriginRuntime()
    {
        using ServiceProvider provider = Container(services => services.AddQuartz("acme", _ => { }));

        IScheduler tenant = await provider.GetRequiredService<ISchedulerRuntime>().Add("initech");

        try
        {
            List<SchedulerRegistration> registrations = await provider.GetRequiredService<ISchedulerRegistry>().QuerySchedulers();

            registrations.Select(x => x.Name).Should().Equal(["acme", "initech"],
                "one listing answers for both doors a scheduler can come through, ordered the same way");
            registrations.Should().ContainSingle(x => x.Name == "acme")
                .Which.Origin.Should().Be(SchedulerOrigin.Container);

            SchedulerRegistration runtime = registrations.Should().ContainSingle(x => x.Name == "initech").Subject;
            runtime.Origin.Should().Be(SchedulerOrigin.Runtime,
                "no registration accounts for it - it was added to a container that was already built");
            runtime.Status.Should().Be(SchedulerStatus.Running);
        }
        finally
        {
            await tenant.Shutdown();
        }
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
    /// Stands in for <c>HttpScheduler</c>: the marker is what the registry reads, and the forwarding
    /// base is what keeps a stand-in from having to implement every member of <see cref="IScheduler" />.
    /// </summary>
    private sealed class TestProxyScheduler : DelegatingScheduler, IProxyScheduler
    {
        public TestProxyScheduler(IScheduler scheduler) : base(scheduler)
        {
        }
    }

    /// <summary>
    /// A proxy whose target never answers, which is what an unreachable one looks like from here.
    /// </summary>
    private sealed class StallingProxyScheduler : DelegatingScheduler, IProxyScheduler
    {
        public StallingProxyScheduler(string schedulerName) : base(A.Fake<IScheduler>())
        {
            SchedulerName = schedulerName;
        }

        public override string SchedulerName { get; }

        public override async ValueTask<SchedulerStatus> GetStatus(CancellationToken cancellationToken = default)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
            return SchedulerStatus.Running;
        }

        public override async ValueTask<string> GetSchedulerInstanceId(CancellationToken cancellationToken = default)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
            return SchedulerName;
        }
    }
}
