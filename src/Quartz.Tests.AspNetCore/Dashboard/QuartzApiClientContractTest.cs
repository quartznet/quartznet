using Microsoft.Extensions.DependencyInjection;

using Quartz.Dashboard.Services;

namespace Quartz.Tests.AspNetCore.Dashboard;

/// <summary>
/// The exception contract <see cref="IQuartzApiClient" /> states, held against whichever implementation
/// the container answers with.
/// </summary>
/// <remarks>
/// <para>
/// The interface is public so that an application can replace it, and its four "return the thing itself"
/// members have non-nullable return types — so what they do when the thing is gone is contract, not an
/// implementation detail of the one client Quartz ships. The dashboard's error boundary renders a
/// <see cref="KeyNotFoundException" /> as the not-found page; a replacement answering <c>null!</c>
/// instead would fault the page with a <see cref="NullReferenceException" /> raised somewhere further in,
/// and nothing said so until this test did.
/// </para>
/// <para>
/// It resolves the client out of a container rather than constructing <c>InProcessQuartzApiClient</c>,
/// because the promise is about whatever <c>AddQuartzDashboard</c> left registered — the registration is
/// <c>TryAdd</c>, so an application that registers its own client first is the one the pages read, and it
/// is the one this contract binds.
/// </para>
/// </remarks>
public class QuartzApiClientContractTest
{
    private ServiceProvider provider = null!;
    private IServiceScope scope = null!;
    private IScheduler scheduler = null!;
    private IQuartzApiClient client = null!;

    [SetUp]
    public async Task SetUp()
    {
        string schedulerName = $"api-client-contract-{Guid.NewGuid():N}";

        ServiceCollection services = new();
        services.AddQuartzDashboard();
        services.AddQuartz(quartz => quartz.ConfigureScheduler(options => options.InstanceName = schedulerName));

        provider = services.BuildServiceProvider();

        // Resolving the scheduler is what binds it into the repository the client looks names up in.
        scheduler = await provider.GetRequiredService<ISchedulerFactory>().GetScheduler();

        // Scoped, because that is the lifetime the pages resolve it with.
        scope = provider.CreateScope();
        client = scope.ServiceProvider.GetRequiredService<IQuartzApiClient>();
    }

    [TearDown]
    public async Task TearDown()
    {
        scope?.Dispose();

        if (scheduler is not null)
        {
            await scheduler.Shutdown(waitForJobsToComplete: false);
        }

        if (provider is not null)
        {
            await provider.DisposeAsync();
        }
    }

    [Test]
    public async Task AnUnknownSchedulerNameIsReportedAsAMissingKey()
    {
        Func<Task> act = () => client.GetScheduler("no-such-scheduler").AsTask();

        (await act.Should().ThrowAsync<KeyNotFoundException>(
            "GetScheduler returns a non-nullable detail, so a name nothing goes by has no other answer"))
            .Which.Message.Should().Contain("no-such-scheduler", "the page shows the name that was not found");
    }

    [Test]
    public async Task AMissingJobIsReportedAsAMissingKey()
    {
        Func<Task> act = () => client.GetJobDetail(scheduler.SchedulerName, new JobKeyDto("ghosts", "no-such-job")).AsTask();

        (await act.Should().ThrowAsync<KeyNotFoundException>(
            "the scheduler exists and holds no such job, which is the case the non-nullable JobDetailDto cannot express"))
            .Which.Message.Should().Contain("no-such-job");
    }

    [Test]
    public async Task AMissingTriggerIsReportedAsAMissingKey()
    {
        Func<Task> act = () => client.GetTrigger(scheduler.SchedulerName, new TriggerKeyDto("ghosts", "no-such-trigger")).AsTask();

        (await act.Should().ThrowAsync<KeyNotFoundException>())
            .Which.Message.Should().Contain("no-such-trigger");
    }

    [Test]
    public async Task AMissingCalendarIsReportedAsAMissingKey()
    {
        Func<Task> act = () => client.GetCalendar(scheduler.SchedulerName, "no-such-calendar").AsTask();

        (await act.Should().ThrowAsync<KeyNotFoundException>())
            .Which.Message.Should().Contain("no-such-calendar");
    }

    /// <summary>
    /// The other half of the contract: a capability the source does not have is a value rather than an
    /// exception, so the overview can draw "cannot say" differently from "nothing is limited".
    /// </summary>
    [Test]
    public async Task ASchedulerThatLimitsNothingReportsNoLimitsRatherThanRefusing()
    {
        ExecutionLimitsDto limits = await client.GetExecutionLimits(scheduler.SchedulerName);

        limits.Should().NotBeNull("the member never answers null");
        limits.CanReport.Should().BeTrue("a scheduler Quartz ships can always say what its limits are");
        limits.Limits.Should().BeEmpty("nothing was limited, which is a different fact from being unable to report");
    }
}
