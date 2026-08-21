using FakeItEasy;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

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
    public async Task ShouldBeAbelToRegisterSchedulerUsingHttpClient()
    {
        var services = new ServiceCollection();
        services.AddQuartzHttpClient("Scheduler", testClient);

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
        services.AddQuartzHttpClient("Scheduler", testClient);
        services.AddQuartzHttpClient("SecondScheduler", testClient);

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
    public async Task ShouldBeAbelToRegisterSchedulerUsingHttpClientFactor()
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
    public async Task ShouldBeAbelToRegisterSchedulersUsingHttpClientFactoryUnderTheirOwnNames()
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
    public async Task ShouldBindEveryRemoteSchedulerWhenTheHostStarts()
    {
        var services = new ServiceCollection();
        services.AddQuartzHttpClient("Scheduler", testClient);
        services.AddQuartzHttpClient("SecondScheduler", testClient);

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
    public async Task EachContainerShouldGetItsOwnSchedulerRepository()
    {
        var firstServices = new ServiceCollection();
        firstServices.AddQuartzHttpClient("Scheduler", testClient);

        var secondServices = new ServiceCollection();
        secondServices.AddQuartzHttpClient("Scheduler", testClient);

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
