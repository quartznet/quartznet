using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

using Quartz.Dashboard.Services;
using Quartz.Tests.AspNetCore.Support;

namespace Quartz.Tests.AspNetCore.Dashboard;

/// <summary>
/// Whether the next start stores a trigger again over what the dashboard saves (#4412), read through the
/// dashboard's client over each carrier: a scheduler in this process, one behind the HTTP API, and one that
/// dialled the dashboard as an agent.
/// </summary>
/// <remarks>
/// Each scheduler declares one trigger with <c>AddTrigger</c>, and the test adds a second at run time. Over HTTP
/// and through the agent the answer is a field of the trigger-state route, which a host older than 4.9 does
/// not send: <see langword="false" /> rather than <see langword="null" /> is what shows the field travelled.
/// </remarks>
[TestFixture(Carrier.Local)]
[TestFixture(Carrier.Http)]
[TestFixture(Carrier.Agent)]
public sealed class DeclaredAtStartupCarrierTest
{
    private static readonly JobKey job = new("declared-job", "declared");
    private static readonly TriggerKey declared = new("declared-trigger", "declared");
    private static readonly TriggerKey addedAtRunTime = new("run-time-trigger", "declared");

    private readonly Carrier carrier;
    private WebApplicationFactory<Program>? host;
    private Agents.AgentDashboard? dashboard;
    private ServiceProvider? owned;
    private IServiceScope scope = null!;
    private IScheduler scheduler = null!;
    private IQuartzApiClient client = null!;
    private string key = null!;

    public DeclaredAtStartupCarrierTest(Carrier carrier)
    {
        this.carrier = carrier;
    }

    [SetUp]
    public async Task SetUp()
    {
        IServiceProvider provider;
        switch (carrier)
        {
            case Carrier.Local:
            {
                ServiceCollection services = new();
                services.AddQuartzDashboard();
                services.AddQuartz(quartz =>
                {
                    quartz.ConfigureScheduler(options => options.InstanceName = $"declared-at-startup-{Guid.NewGuid():N}");
                    Declare(quartz);
                });
                owned = services.BuildServiceProvider();
                provider = owned;
                scheduler = await owned.GetRequiredService<ISchedulerFactory>().GetScheduler();
                key = scheduler.SchedulerName;
                break;
            }
            case Carrier.Http:
            {
                TestContentRoot.Apply();
                host = new WebApplicationFactory<Program>().WithWebHostBuilder(builder => builder.ConfigureServices(services => services.AddQuartz(Declare)));
                scheduler = await host.Services.GetRequiredService<ISchedulerFactory>().GetScheduler();

                ServiceCollection services = new();
                services.AddQuartzDashboard();
                services.AddQuartzHttpClient(scheduler.SchedulerName, _ => host.CreateClient());
                owned = services.BuildServiceProvider();
                provider = owned;
                provider.GetRequiredKeyedService<IScheduler>(scheduler.SchedulerName).Should().BeOfType<HttpScheduler>();
                key = scheduler.SchedulerName;
                break;
            }
            default:
            {
                dashboard = await Agents.AgentDashboard.Start();
                Agents.AgentWorker worker = await dashboard.StartAgent("w1", clock: TimeProvider.System, configureQuartz: Declare);
                scheduler = worker.Scheduler;
                await scheduler.Standby();
                provider = dashboard.App.Services;
                key = worker.Key;
                break;
            }
        }

        scope = provider.CreateScope();
        client = scope.ServiceProvider.GetRequiredService<IQuartzApiClient>();

        await scheduler.ScheduleJob(TriggerBuilder.Create()
            .WithIdentity(addedAtRunTime)
            .ForJob(job)
            .WithCronSchedule("0 0 7 * * ?")
            .Build());
    }

    [TearDown]
    public async Task TearDown()
    {
        scope?.Dispose();

        if (scheduler is not null)
        {
            await scheduler.Clear();
        }

        if (owned is not null)
        {
            await owned.DisposeAsync();
            owned = null;
        }

        if (scheduler is not null && carrier == Carrier.Local)
        {
            await scheduler.Shutdown(waitForJobsToComplete: false);
        }

        if (host is not null)
        {
            await host.DisposeAsync();
            host = null;
        }

        if (dashboard is not null)
        {
            await dashboard.DisposeAsync();
            dashboard = null;
        }
    }

    [Test]
    public async Task ADeclaredTriggerIsSaidToBeStoredAgainAtTheNextStart()
    {
        (await client.IsTriggerDeclaredAtStartup(key, new TriggerKeyDto(declared.Group, declared.Name))).Should().BeTrue(
            "the scheduler's AddTrigger registration stores it over the stored copy at every start");
    }

    [Test]
    public async Task ATriggerAddedAtRunTimeIsNot()
    {
        (await client.IsTriggerDeclaredAtStartup(key, new TriggerKeyDto(addedAtRunTime.Group, addedAtRunTime.Name))).Should().BeFalse(
            "no declaration names it; null would mean the answer never arrived");
    }

    private static void Declare(IQuartzBuilder quartz)
    {
        quartz.AddJob<DummyJob>(j => j.WithIdentity(job).StoreDurably());
        quartz.AddTrigger(t => t.WithIdentity(declared).ForJob(job).WithCronSchedule("0 0 6 * * ?"));
    }
}
