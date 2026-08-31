#nullable enable

using FakeItEasy;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Quartz.Tests.Unit.HealthChecks;

/// <summary>
/// What the health check reports for each state a scheduler can be in.
/// </summary>
/// <remarks>
/// <para>
/// The check used to read one boolean, which made a scheduler in standby healthy — it had been started
/// once — and a shut-down one fall through to the store probe, where the failure was reported as a
/// connectivity problem rather than as the scheduler being gone.
/// </para>
/// <para>
/// This lives in <c>Quartz.Tests.Unit</c>, which references no ASP.NET Core anything, so the project
/// failing to compile is what says the check still needs none of it (issue #3532).
/// </para>
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
    /// A scheduler the application starts itself is in <see cref="SchedulerStatus.Created" /> by design,
    /// so the probe reports what standby reports rather than taking the node out of rotation.
    /// </summary>
    [Test]
    public async Task ACreatedSchedulerTheApplicationStartsIsDegradedRatherThanUnhealthy()
    {
        HealthReportEntry result = await Check(SchedulerStatus.Created, hostedService: options => options.AutoStart = false);

        result.Status.Should().Be(
            HealthStatus.Degraded,
            "AutoStart = false says the application presses start, so a created scheduler is the configuration "
            + "working rather than failing - and unhealthy would take a correctly configured node out of rotation");
        result.Description.Should().Contain("created").And.Contain("application").And.Contain("core");
    }

    /// <summary>
    /// The other side of the same branch: a scheduler the hosted service was going to start, and has
    /// not, is a fault.
    /// </summary>
    [Test]
    public async Task ACreatedSchedulerTheHostedServiceWasGoingToStartIsStillUnhealthy()
    {
        HealthReportEntry result = await Check(SchedulerStatus.Created, hostedService: _ => { });

        result.Status.Should().Be(
            HealthStatus.Unhealthy,
            "nothing opted out of the automatic start, so a scheduler still sitting in Created is the failure it "
            + "always was");
        result.Description.Should().Contain("never started");
    }

    /// <summary>
    /// <c>AutoStart</c> is one scheduler's setting, and the check has to read the options registered under
    /// the name of the scheduler it reports on.
    /// </summary>
    [Test]
    public async Task ACheckReadsTheAutoStartOfItsOwnSchedulerRatherThanAnothers()
    {
        ServiceCollection services = new();
        services.AddLogging();
        services.AddSingleton(FactoryFor("core", SchedulerStatus.Created));
        services.AddKeyedSingleton(typeof(ISchedulerFactory), "reporting", (_, _) => FactoryFor("reporting", SchedulerStatus.Created));
        services.AddHealthChecks().AddQuartz();
        services.AddHealthChecks().AddQuartz("reporting");
        services.AddQuartzHostedService("reporting", options => options.AutoStart = false);

        await using ServiceProvider provider = services.BuildServiceProvider();

        (await Run(provider, "quartz-scheduler-reporting")).Status.Should().Be(
            HealthStatus.Degraded,
            "'reporting' is the scheduler that opted out");

        (await Run(provider, "quartz-scheduler")).Status.Should().Be(
            HealthStatus.Unhealthy,
            "the default scheduler's options are its own, and nothing set AutoStart on them");
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
        services.AddHealthChecks().AddQuartz();

        await using ServiceProvider provider = services.BuildServiceProvider();

        provider.GetService<ISchedulerFactory>().Should().BeNull(
            "the premise of the test is a container with no default scheduler in it");

        HealthReportEntry result = await Run(provider, "quartz-scheduler");

        result.Status.Should().Be(HealthStatus.Unhealthy);
        result.Description.Should().Contain("AddQuartzHealthChecks",
            "the message has to name the way out, since the check cannot know which scheduler was meant");
    }

    /// <param name="status">The state the scheduler reports.</param>
    /// <param name="storeFailure">What the store probe throws, when the test is about that.</param>
    /// <param name="hostedService">
    /// Registers the hosted service and configures it. Left <see langword="null" /> there is none in the
    /// container at all, which is the case that leaves <c>AutoStart</c> at its default.
    /// </param>
    private static async Task<HealthReportEntry> Check(
        SchedulerStatus status,
        SchedulerException? storeFailure = null,
        Action<QuartzHostedServiceOptions>? hostedService = null)
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
        services.AddHealthChecks().AddQuartz();

        if (hostedService is not null)
        {
            services.AddQuartzHostedService(hostedService);
        }

        await using ServiceProvider provider = services.BuildServiceProvider();

        return await Run(provider, "quartz-scheduler");
    }

    private static ISchedulerFactory FactoryFor(string name, SchedulerStatus status)
    {
        IScheduler scheduler = A.Fake<IScheduler>();
        A.CallTo(() => scheduler.SchedulerName).Returns(name);
        A.CallTo(() => scheduler.Status).Returns(status);

        ISchedulerFactory factory = A.Fake<ISchedulerFactory>();
        A.CallTo(() => factory.GetScheduler(A<CancellationToken>._)).Returns(new ValueTask<IScheduler>(scheduler));
        return factory;
    }

    private static async Task<HealthReportEntry> Run(ServiceProvider provider, string checkName)
    {
        HealthReport report = await provider.GetRequiredService<HealthCheckService>()
            .CheckHealthAsync(registration => registration.Name == checkName);

        return report.Entries[checkName];
    }
}
