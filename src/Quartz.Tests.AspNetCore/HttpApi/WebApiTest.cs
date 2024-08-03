using FakeItEasy;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

using Quartz.HttpClient;
using Quartz.Spi;
using Quartz.Tests.AspNetCore.Support;

namespace Quartz.Tests.AspNetCore.HttpApi;

public abstract class WebApiTest
{
    [OneTimeSetUp]
    public void OneTimeSetup()
    {
        WebApplicationFactory = new WebApplicationFactory<Program>();
        HttpScheduler = new HttpScheduler(TestData.SchedulerName, WebApplicationFactory.CreateClient());
    }

    [OneTimeTearDown]
    public async Task OneTimeTearDown()
    {
        ClearSchedulerRepository();
        if (WebApplicationFactory is not null)
        {
            await WebApplicationFactory.DisposeAsync();
            WebApplicationFactory = null!;
        }
    }

    [SetUp]
    public void Setup()
    {
        ClearSchedulerRepository();
        FakeScheduler = CreateFakeScheduler();
        WebApplicationFactory.Services.GetRequiredService<ISchedulerRepository>().Bind(FakeScheduler);
    }

    protected WebApplicationFactory<Program> WebApplicationFactory { get; private set; } = null!;
    protected HttpScheduler HttpScheduler { get; private set; } = null!;
    protected IScheduler FakeScheduler { get; private set; } = null!;

    protected virtual IScheduler CreateFakeScheduler()
    {
        var fake = A.Fake<IScheduler>();
        A.CallTo(() => fake.SchedulerName).Returns(TestData.Metadata.SchedulerName);
        A.CallTo(() => fake.SchedulerInstanceId).Returns(TestData.Metadata.SchedulerInstanceId);

        return fake;
    }

    private void ClearSchedulerRepository()
    {
        ISchedulerRepository schedulerRepository = WebApplicationFactory.Services.GetRequiredService<ISchedulerRepository>();
        foreach (var scheduler in schedulerRepository.LookupAll())
        {
            schedulerRepository.Remove(scheduler.SchedulerName);
        }
    }
}