using FakeItEasy;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

using Quartz.Extensibility;

namespace Quartz.Tests.Unit.Extensions.DependencyInjection;

public class QuartzHttpClientServiceCollectionExtensionsTest
{
    private HttpClient testClient;

    [SetUp]
    public void SetUp()
    {
        testClient = new HttpClient
        {
            BaseAddress = new Uri("http://localhost:8080")
        };
    }

    [TearDown]
    public void TearDown()
    {
        testClient?.Dispose();
        testClient = null;

        // Nothing else to clean up: each test builds its own container, and the repository the
        // schedulers bind into goes away with it.
    }

    [Test]
    public async Task ShouldBeAbleToRegisterSchedulerUsingAClientFactory()
    {
        var services = new ServiceCollection();
        services.AddQuartzHttpClient("Scheduler", _ => testClient);

        await using var serviceProvider = services.BuildServiceProvider();

        var scheduler = serviceProvider.GetRequiredService<IScheduler>();
        scheduler.Should().NotBeNull();
        scheduler.Should().BeOfType<HttpScheduler>();
        scheduler.SchedulerName.Should().Be("Scheduler");
    }

    [Test]
    public async Task ShouldRegisterSchedulersUnderTheirOwnNames()
    {
        var services = new ServiceCollection();
        services.AddQuartzHttpClient("Scheduler", _ => testClient);
        services.AddQuartzHttpClient("SecondScheduler", _ => testClient);

        await using var serviceProvider = services.BuildServiceProvider();

        var scheduler = serviceProvider.GetRequiredKeyedService<IScheduler>("Scheduler");
        scheduler.Should().BeOfType<HttpScheduler>();
        scheduler.SchedulerName.Should().Be("Scheduler");

        var second = serviceProvider.GetRequiredKeyedService<IScheduler>("SecondScheduler");
        second.Should().BeOfType<HttpScheduler>();
        second.SchedulerName.Should().Be("SecondScheduler");

        serviceProvider.GetRequiredService<IScheduler>().Should().BeSameAs(scheduler,
            "the unkeyed registration is the first remote scheduler, not whichever was registered last");
    }

    [Test]
    public async Task ShouldBeAbleToRegisterSchedulerUsingHttpClientFactory()
    {
        var httpClientFactory = A.Fake<IHttpClientFactory>();
        A.CallTo(() => httpClientFactory.CreateClient("MyHttpClient")).Returns(testClient);

        var services = new ServiceCollection();
        services.AddSingleton(httpClientFactory);
        services.AddQuartzHttpClient("Scheduler", "MyHttpClient");

        await using var serviceProvider = services.BuildServiceProvider();

        var scheduler = serviceProvider.GetRequiredService<IScheduler>();
        scheduler.Should().NotBeNull();
        scheduler.Should().BeOfType<HttpScheduler>();
        scheduler.SchedulerName.Should().Be("Scheduler");
    }

    [Test]
    public async Task ShouldBeAbleToRegisterSchedulersUsingHttpClientFactoryUnderTheirOwnNames()
    {
        var httpClientFactory = A.Fake<IHttpClientFactory>();
        A.CallTo(() => httpClientFactory.CreateClient("MyHttpClient")).Returns(testClient);

        var services = new ServiceCollection();
        services.AddSingleton(httpClientFactory);
        services.AddQuartzHttpClient("Scheduler", "MyHttpClient");
        services.AddQuartzHttpClient("SecondScheduler", "MyHttpClient");

        await using var serviceProvider = services.BuildServiceProvider();

        serviceProvider.GetRequiredKeyedService<IScheduler>("Scheduler").SchedulerName.Should().Be("Scheduler");
        serviceProvider.GetRequiredKeyedService<IScheduler>("SecondScheduler").SchedulerName.Should().Be("SecondScheduler");
    }

    [Test]
    public void AClientHasToBeNamedOrBuilt()
    {
        var services = new ServiceCollection();

        var neither = () => services.AddQuartzHttpClient(options => options.SchedulerName = "Scheduler");

        neither.Should().Throw<OptionsValidationException>()
            .WithMessage("*HttpClientName*CreateHttpClient*",
                "a client that cannot be reached is a misconfiguration, and the two ways to name one are what it has to say");
    }

    [Test]
    public void AClientCannotBeBothNamedAndBuilt()
    {
        var services = new ServiceCollection();

        var both = () => services.AddQuartzHttpClient(options =>
        {
            options.SchedulerName = "Scheduler";
            options.HttpClientName = "MyHttpClient";
            options.CreateHttpClient = _ => testClient;
        });

        both.Should().Throw<OptionsValidationException>()
            .WithMessage("*HttpClientName*CreateHttpClient*both*");
    }

    [Test]
    public async Task AClientFactoryRunsOnceAndIsHandedTheContainer()
    {
        var calls = 0;
        IServiceProvider seen = null;

        var services = new ServiceCollection();
        services.AddQuartzHttpClient("Scheduler", provider =>
        {
            calls++;
            seen = provider;
            return testClient;
        });

        await using var serviceProvider = services.BuildServiceProvider();

        serviceProvider.GetRequiredService<IScheduler>().Should().BeOfType<HttpScheduler>();
        serviceProvider.GetRequiredService<IScheduler>().Should().BeOfType<HttpScheduler>();

        calls.Should().Be(1, "the scheduler is a singleton, so its client is built once");
        seen.Should().NotBeNull("the factory is handed the container, so a client can be assembled from other services");
    }

    [Test]
    public async Task ShouldBindEveryRemoteSchedulerWhenTheHostStarts()
    {
        var services = new ServiceCollection();
        services.AddQuartzHttpClient("Scheduler", _ => testClient);
        services.AddQuartzHttpClient("SecondScheduler", _ => testClient);

        await using var serviceProvider = services.BuildServiceProvider();

        var repository = serviceProvider.GetRequiredService<ISchedulerRepository>();
        repository.LookupAll().Should().BeEmpty("nothing has asked for a scheduler yet");

        var hostedServices = serviceProvider.GetServices<IHostedService>().ToList();
        hostedServices.Should().ContainSingle("one binder covers every remote scheduler in the container");

        await hostedServices[0].StartAsync(CancellationToken.None);

        repository.LookupAll().Select(s => s.SchedulerName)
            .Should().BeEquivalentTo(["Scheduler", "SecondScheduler"]);

        await hostedServices[0].StopAsync(CancellationToken.None);
    }

    [Test]
    public async Task ALocalDefaultSchedulerRegisteredFirstShouldKeepTheUnkeyedSlot()
    {
        var services = new ServiceCollection();
        services.AddQuartz();
        services.AddQuartzHttpClient("Remote", _ => testClient);

        await using var serviceProvider = services.BuildServiceProvider();

        serviceProvider.GetRequiredService<IScheduler>().SchedulerName.Should().Be("QuartzScheduler",
            "the unkeyed registration is a TryAdd, so the local default scheduler keeps what "
            + "GetRequiredService<IScheduler>() means");
        serviceProvider.GetRequiredKeyedService<IScheduler>("Remote").Should().BeOfType<HttpScheduler>(
            "the remote scheduler is reachable under its own name either way");
    }

    [Test]
    public void ADefaultSchedulerCannotBeAddedAfterARemoteOneHasTakenTheUnkeyedSlot()
    {
        var services = new ServiceCollection();
        services.AddQuartzHttpClient("Remote", _ => testClient);

        var act = () => services.AddQuartz();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*AddQuartzHttpClient*")
            .WithMessage("*GetRequiredKeyedService<IScheduler>*",
                "registration is first-wins, so this order used to make 'the scheduler' the remote one "
                + "with no error at all — a program scheduling a job would have sent it to another process");
    }

    [Test]
    public void ANamedSchedulerCanStillBeAddedBesideARemoteOne()
    {
        var services = new ServiceCollection();
        services.AddQuartzHttpClient("Remote", _ => testClient);

        var act = () => services.AddQuartz("Local");

        act.Should().NotThrow("a named scheduler is keyed by its name and never wanted the unkeyed slot");
    }

    /// <summary>
    /// A second target under a name already registered is refused rather than quietly taking the first
    /// one's place.
    /// </summary>
    /// <remarks>
    /// The keyed <see cref="IScheduler" /> registration is appended, so the last call won and the first
    /// target simply stopped existing — with the repository holding one entry under the name either way,
    /// and nothing anywhere saying which process it pointed at.
    /// </remarks>
    [Test]
    public void ASecondTargetCannotTakeARegisteredSchedulerName()
    {
        var services = new ServiceCollection();
        services.AddQuartzHttpClient("Remote", _ => testClient);

        var act = () => services.AddQuartzHttpClient("Remote", _ => testClient);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Remote*")
            .WithMessage("*3387*", "the fleet model is where fronting several schedulers under one name belongs");
    }

    [Test]
    public void ARefusedDuplicateLeavesTheContainerAsItFoundIt()
    {
        var services = new ServiceCollection();
        services.AddQuartzHttpClient("Remote", _ => testClient);
        int registered = services.Count;

        var act = () => services.AddQuartzHttpClient("remote", _ => testClient);

        act.Should().Throw<InvalidOperationException>("the name is matched the way the repository indexes it, ignoring case");
        services.Count.Should().Be(registered,
            "the refusal happens before anything is registered, so a caught exception does not leave a half-registered scheduler behind");
    }

    [Test]
    public async Task ARemoteSchedulerIsListedAsRemote()
    {
        var services = new ServiceCollection();
        services.AddQuartzHttpClient("Remote", _ => testClient);

        await using var serviceProvider = services.BuildServiceProvider();

        // Resolving is what binds it into the repository, which is what the registry reads.
        serviceProvider.GetRequiredKeyedService<IScheduler>("Remote").Should().BeOfType<HttpScheduler>();

        List<SchedulerRegistration> registrations = await serviceProvider.GetRequiredService<ISchedulerRegistry>().QuerySchedulers();

        registrations.Should().ContainSingle(x => x.Name == "Remote")
            .Which.Origin.Should().Be(SchedulerOrigin.Remote,
                "an HttpScheduler stands for a scheduler in another process, and nothing in this one runs it");
    }

    /// <summary>
    /// A target's execution history is registered beside it, under the same key.
    /// </summary>
    /// <remarks>
    /// Keyed only, and deliberately: history is recorded where a scheduler runs, so "the" history store
    /// of a container that also holds local schedulers is the one recording them — not this reader of
    /// somebody else's.
    /// </remarks>
    [Test]
    public async Task ARemoteSchedulersHistoryIsRegisteredUnderItsName()
    {
        var services = new ServiceCollection();
        services.AddQuartzHttpClient("Remote", _ => testClient);

        await using var serviceProvider = services.BuildServiceProvider();

        serviceProvider.GetRequiredKeyedService<IExecutionHistoryStore>("Remote").Should().NotBeNull(
            "the dashboard asks the process a scheduler runs in for that scheduler's history");
        serviceProvider.GetService<IExecutionHistoryStore>().Should().BeNull(
            "a reader of another process's history is not what this container records into");
    }

    [Test]
    public async Task OneClientIsBuiltForOneTarget()
    {
        int built = 0;
        var services = new ServiceCollection();
        services.AddQuartzHttpClient("Remote", _ =>
        {
            built++;
            return testClient;
        });

        await using var serviceProvider = services.BuildServiceProvider();

        serviceProvider.GetRequiredKeyedService<IScheduler>("Remote").Should().NotBeNull();
        serviceProvider.GetRequiredKeyedService<IExecutionHistoryStore>("Remote").Should().NotBeNull();

        built.Should().Be(1,
            "the factory runs once per registration, as its documentation says - the scheduler and its history share the client");
    }

    [Test]
    public async Task EachContainerShouldGetItsOwnSchedulerRepository()
    {
        var firstServices = new ServiceCollection();
        firstServices.AddQuartzHttpClient("Scheduler", _ => testClient);

        var secondServices = new ServiceCollection();
        secondServices.AddQuartzHttpClient("Scheduler", _ => testClient);

        await using var first = firstServices.BuildServiceProvider();
        await using var second = secondServices.BuildServiceProvider();

        // Resolving the scheduler is what binds it into its container's repository.
        first.GetRequiredService<IScheduler>();
        second.GetRequiredService<IScheduler>();

        var firstRepository = first.GetRequiredService<ISchedulerRepository>();
        var secondRepository = second.GetRequiredService<ISchedulerRepository>();

        firstRepository.Should().NotBeSameAs(secondRepository);
        firstRepository.LookupAll().Should().ContainSingle("a repository only holds its own container's schedulers");
        secondRepository.LookupAll().Should().ContainSingle();
    }
}
