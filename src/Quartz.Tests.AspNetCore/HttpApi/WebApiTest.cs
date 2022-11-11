using FakeItEasy;

using Microsoft.AspNetCore.Mvc.Testing;

using NUnit.Framework;

using Quartz.HttpClient;
using Quartz.Impl;
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
        await ClearSchedulerRepository();
        if (WebApplicationFactory != null)
        {
            await WebApplicationFactory.DisposeAsync();
            WebApplicationFactory = null!;
        }
    }

    [SetUp]
    public async Task Setup()
    {
        await ClearSchedulerRepository();
        FakeScheduler = CreateFakeScheduler();
        SchedulerRepository.Instance.Bind(FakeScheduler);
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

    private static async Task ClearSchedulerRepository()
    {
        var allSchedulers = await SchedulerRepository.Instance.LookupAll();
        foreach (var scheduler in allSchedulers)
        {
            SchedulerRepository.Instance.Remove(scheduler.SchedulerName);
        }
    }
}