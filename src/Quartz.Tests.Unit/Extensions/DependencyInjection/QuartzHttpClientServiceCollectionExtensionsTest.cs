using FakeItEasy;


using Microsoft.Extensions.DependencyInjection;

using Quartz;
using Quartz.Impl;
using Quartz.Extensibility;

using QuartzHttpClientServiceCollectionExtensionsTestTypes;

namespace Quartz.Tests.Unit.Extensions.DependencyInjection
{
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
        public void ShouldBeAbelToRegisterSchedulerUsingHttpClient()
        {
            var services = new ServiceCollection();
            services.AddQuartzHttpClient("Scheduler", testClient);

            using var serviceProvider = services.BuildServiceProvider();

            var scheduler = serviceProvider.GetRequiredService<IScheduler>();
            scheduler.Should().NotBeNull();
            scheduler.Should().BeOfType<HttpScheduler>();
            scheduler.SchedulerName.Should().Be("Scheduler");
        }

        [Test]
        public void ShouldBeAbelToRegisterSchedulerUsingHttpClientAndMarkerInterface()
        {
            var services = new ServiceCollection();
            services.AddQuartzHttpClient<IMyScheduler>("Scheduler", testClient);
            services.AddQuartzHttpClient<IMySecondScheduler>("SecondScheduler", testClient);

            using var serviceProvider = services.BuildServiceProvider();

            IScheduler scheduler = serviceProvider.GetRequiredService<IMyScheduler>();
            scheduler.Should().NotBeNull();
            scheduler.Should().BeAssignableTo<DelegatingScheduler>();
            scheduler.SchedulerName.Should().Be("Scheduler");

            scheduler = serviceProvider.GetRequiredService<IMySecondScheduler>();
            scheduler.Should().NotBeNull();
            scheduler.Should().BeAssignableTo<DelegatingScheduler>();
            scheduler.SchedulerName.Should().Be("SecondScheduler");

            scheduler = serviceProvider.GetService<IScheduler>();
            scheduler.Should().BeNull();
        }

        [Test]
        public void ShouldBeAbelToRegisterSchedulerUsingHttpClientFactor()
        {
            var httpClientFactory = A.Fake<IHttpClientFactory>();
            A.CallTo(() => httpClientFactory.CreateClient("MyHttpClient")).Returns(testClient);

            var services = new ServiceCollection();
            services.AddSingleton(httpClientFactory);
            services.AddQuartzHttpClient("Scheduler", "MyHttpClient");

            using var serviceProvider = services.BuildServiceProvider();

            var scheduler = serviceProvider.GetRequiredService<IScheduler>();
            scheduler.Should().NotBeNull();
            scheduler.Should().BeOfType<HttpScheduler>();
            scheduler.SchedulerName.Should().Be("Scheduler");
        }

        [Test]
        public void ShouldBeAbelToRegisterSchedulerUsingHttpClientFactorAndMarkerInterface()
        {
            var httpClientFactory = A.Fake<IHttpClientFactory>();
            A.CallTo(() => httpClientFactory.CreateClient("MyHttpClient")).Returns(testClient);

            var services = new ServiceCollection();
            services.AddSingleton(httpClientFactory);
            services.AddQuartzHttpClient<IMyScheduler>("Scheduler", "MyHttpClient");
            services.AddQuartzHttpClient<IMySecondScheduler>("SecondScheduler", "MyHttpClient");

            using var serviceProvider = services.BuildServiceProvider();

            IScheduler scheduler = serviceProvider.GetRequiredService<IMyScheduler>();
            scheduler.Should().NotBeNull();
            scheduler.Should().BeAssignableTo<DelegatingScheduler>();
            scheduler.SchedulerName.Should().Be("Scheduler");

            scheduler = serviceProvider.GetRequiredService<IMySecondScheduler>();
            scheduler.Should().NotBeNull();
            scheduler.Should().BeAssignableTo<DelegatingScheduler>();
            scheduler.SchedulerName.Should().Be("SecondScheduler");

            scheduler = serviceProvider.GetService<IScheduler>();
            scheduler.Should().BeNull();
        }

        [Test]
        public void EachContainerShouldGetItsOwnSchedulerRepository()
        {
            var firstServices = new ServiceCollection();
            firstServices.AddQuartzHttpClient("Scheduler", testClient);

            var secondServices = new ServiceCollection();
            secondServices.AddQuartzHttpClient("Scheduler", testClient);

            using var first = firstServices.BuildServiceProvider();
            using var second = secondServices.BuildServiceProvider();

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
}

namespace QuartzHttpClientServiceCollectionExtensionsTestTypes
{
    public interface IMyScheduler : IScheduler;

    public interface IMySecondScheduler : IScheduler;
}