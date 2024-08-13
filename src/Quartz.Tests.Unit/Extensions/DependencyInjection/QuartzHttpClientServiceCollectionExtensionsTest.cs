using FakeItEasy;

using FluentAssertions;

using Microsoft.Extensions.DependencyInjection;

using Quartz;
using Quartz.HttpClient;
using Quartz.Impl;

using QuartzHttpClientServiceCollectionExtensionsTestTypes;

namespace Quartz.Tests.Unit.Extensions.DependencyInjection
{
    public class QuartzHttpClientServiceCollectionExtensionsTest
    {
        private System.Net.Http.HttpClient testClient;

        [SetUp]
        public void SetUp()
        {
            testClient = new System.Net.Http.HttpClient
            {
                BaseAddress = new Uri("http://localhost:8080")
            };
        }

        [TearDown]
        public void TearDown()
        {
            testClient?.Dispose();
            testClient = null;

            ClearSchedulerRepository();
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

        private static void ClearSchedulerRepository()
        {
            foreach (var scheduler in SchedulerRepository.Instance.LookupAll())
            {
                SchedulerRepository.Instance.Remove(scheduler.SchedulerName);
            }
        }
    }
}

namespace QuartzHttpClientServiceCollectionExtensionsTestTypes
{
    public interface IMyScheduler : IScheduler;

    public interface IMySecondScheduler : IScheduler;
}