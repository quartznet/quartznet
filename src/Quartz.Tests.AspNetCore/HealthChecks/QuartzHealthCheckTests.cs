using FakeItEasy;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Quartz.Tests.AspNetCore.HealthChecks;

/// <summary>
/// What the health check reports for each state a scheduler can be in.
/// </summary>
/// <remarks>
/// The check used to read one boolean, which made a scheduler in standby healthy — it had been started
/// once — and a shut-down one fall through to the store probe, where the failure was reported as a
/// connectivity problem rather than as the scheduler being gone.
/// </remarks>
public class QuartzHealthCheckTests
{
    [Test]
    public async Task ARunningSchedulerThatCanReachItsStoreIsHealthy()
    {
        HealthReportEntry result = await Check(SchedulerStatus.Running);

        result.Status.Should().Be(HealthStatus.Healthy);
    }

    [Test]
    public async Task ARunningSchedulerThatCannotReachItsStoreIsUnhealthy()
    {
        HealthReportEntry result = await Check(SchedulerStatus.Running, storeFailure: new SchedulerException("no connection"));

        result.Status.Should().Be(HealthStatus.Unhealthy);
        result.Description.Should().Contain("store");
    }

    [Test]
    public async Task ASchedulerInStandbyIsDegradedRatherThanHealthyOrUnhealthy()
    {
        HealthReportEntry result = await Check(SchedulerStatus.Standby);

        result.Status.Should().Be(
            HealthStatus.Degraded,
            "standby is deliberate and reversible: calling it healthy hides an application that never started its "
            + "scheduler, and calling it unhealthy takes a node out of rotation for doing what it was told");
        result.Description.Should().Contain("standby").And.Contain("core");
    }

    [TestCase(SchedulerStatus.Created, "never started")]
    [TestCase(SchedulerStatus.ShuttingDown, "shutting down")]
    [TestCase(SchedulerStatus.Shutdown, "shut down")]
    public async Task ASchedulerThatIsNotRunningIsUnhealthyAndSaysWhy(SchedulerStatus status, string expected)
    {
        HealthReportEntry result = await Check(status);

        result.Status.Should().Be(HealthStatus.Unhealthy);
        result.Description.Should().Contain(expected).And.Contain("core",
            "an operator reading a failed probe needs to know which scheduler it was and what state it was in");
    }

    /// <summary>
    /// A container whose schedulers are all named has no default one, and the check for it must say so
    /// rather than throw.
    /// </summary>
    /// <remarks>
    /// The factory was resolved with <c>GetRequiredService</c> while the check was being constructed, so
    /// the first probe threw <see cref="InvalidOperationException"/> out of the health-check pipeline —
    /// a 500 from <c>/health</c> with a dependency-injection message in it.
    /// </remarks>
    [Test]
    public async Task ADefaultCheckInAContainerOfOnlyNamedSchedulersIsUnhealthyRatherThanThrowing()
    {
        ServiceCollection services = new();
        services.AddLogging();
        services.AddQuartz("reporting", q => q.UseInMemoryStore());
        services.AddQuartzHealthChecks();

        await using ServiceProvider provider = services.BuildServiceProvider();

        provider.GetService<ISchedulerFactory>().Should().BeNull(
            "the premise of the test is a container with no default scheduler in it");

        HealthReportEntry result = await Run(provider, "quartz-scheduler");

        result.Status.Should().Be(HealthStatus.Unhealthy);
        result.Description.Should().Contain("AddQuartzHealthChecks",
            "the message has to name the way out, since the check cannot know which scheduler was meant");
    }

    private static async Task<HealthReportEntry> Check(SchedulerStatus status, SchedulerException? storeFailure = null)
    {
        IScheduler scheduler = A.Fake<IScheduler>();
        A.CallTo(() => scheduler.SchedulerName).Returns("core");
        A.CallTo(() => scheduler.Status).Returns(status);

        if (storeFailure is not null)
        {
            A.CallTo(() => scheduler.Exists(A<JobKey>._, A<CancellationToken>._)).Throws(storeFailure);
        }

        ISchedulerFactory factory = A.Fake<ISchedulerFactory>();
        A.CallTo(() => factory.GetScheduler(A<CancellationToken>._)).Returns(new ValueTask<IScheduler>(scheduler));

        ServiceCollection services = new();
        services.AddLogging();
        services.AddSingleton(factory);
        services.AddQuartzHealthChecks();

        await using ServiceProvider provider = services.BuildServiceProvider();

        return await Run(provider, "quartz-scheduler");
    }

    private static async Task<HealthReportEntry> Run(ServiceProvider provider, string checkName)
    {
        HealthReport report = await provider.GetRequiredService<HealthCheckService>()
            .CheckHealthAsync(registration => registration.Name == checkName);

        return report.Entries[checkName];
    }
}
