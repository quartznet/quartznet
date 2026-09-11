using FakeItEasy;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

using Quartz.Extensibility;
using Quartz.Tests.AspNetCore.Support;

namespace Quartz.Tests.AspNetCore.HttpApi;

public abstract class WebApiTest
{
    [OneTimeSetUp]
    public void OneTimeSetup()
    {
        TestContentRoot.Apply();
        WebApplicationFactory = new WebApplicationFactory<Program>();
        HttpScheduler = new HttpScheduler(TestData.SchedulerName, WebApplicationFactory.CreateClient());
    }

    [OneTimeTearDown]
    public async Task OneTimeTearDown()
    {
        ClearSchedulerRepository();

        // A no-op that pointedly does not shut the remote scheduler down; see HttpScheduler.DisposeAsync.
        await HttpScheduler.DisposeAsync();

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

    [TearDown]
    public async Task TearDown()
    {
        await FakeScheduler.DisposeAsync();
    }

    protected WebApplicationFactory<Program> WebApplicationFactory { get; private set; } = null!;
    protected HttpScheduler HttpScheduler { get; private set; } = null!;
    protected IScheduler FakeScheduler { get; private set; } = null!;

    protected virtual IScheduler CreateFakeScheduler()
    {
        var fake = A.Fake<IScheduler>();
        A.CallTo(() => fake.SchedulerName).Returns(TestData.Metadata.SchedulerName);
        A.CallTo(() => fake.SchedulerInstanceId).Returns(TestData.Metadata.SchedulerInstanceId);
        AnswerTheAsyncTwinsFromTheProperties(fake);

        return fake;
    }

    /// <summary>
    /// Makes a fake answer <see cref="IScheduler.GetStatus" /> and
    /// <see cref="IScheduler.GetSchedulerInstanceId" /> the way every real local scheduler does — with
    /// the property the default interface member wraps.
    /// </summary>
    /// <remarks>
    /// A fake intercepts a default interface member like any other, so without this the two answer
    /// dummies: the empty string, and <see cref="SchedulerStatus.Unknown" /> because it is the enum's
    /// zero. Both are read by the scheduler listing, which asks the asynchronous twins rather than the
    /// properties so that a scheduler in another process is not read by blocking a request thread.
    /// </remarks>
    protected static void AnswerTheAsyncTwinsFromTheProperties(IScheduler fake)
    {
        A.CallTo(() => fake.GetStatus(A<CancellationToken>._)).CallsBaseMethod();
        A.CallTo(() => fake.GetSchedulerInstanceId(A<CancellationToken>._)).CallsBaseMethod();
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
