using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using Quartz.Dashboard.Host;

namespace Quartz.Tests.Aspire;

/// <summary>
/// The contract <c>Quartz.Aspire.Hosting</c> keeps with the two programs it configures and shares no code
/// with: what the AppHost writes is fed to the dashboard host's own reading of its settings and to the
/// agent's, and both have to come out configured as the AppHost meant.
/// </summary>
/// <remarks>
/// The environment is resolved by Aspire's <see cref="ExecutionConfigurationBuilder" />, the way the
/// orchestrator hands it to a container or a process, and read back as a .NET process reads environment
/// variables, <c>__</c> as the section separator. A key renamed on either side fails here rather than in
/// somebody's AppHost.
/// </remarks>
public sealed class EnvironmentContractTest
{
    [Test]
    public async Task TheDashboardHostStartsOnWhatTheResourceWrites()
    {
        IDistributedApplicationBuilder builder = AppHost.Builder();
        IResourceBuilder<IResourceWithConnectionString> database = builder.AddConnectionString("quartz-db");
        IResourceBuilder<QuartzDashboardResource> dashboard = builder.AddQuartzDashboard("quartz")
            .WithAttachedStore(database)
            .WithAttachedStore(database, "billing", settings =>
            {
                settings.TablePrefix = "BILLING_";
                settings.Serializer = "Newtonsoft";
                settings.ExecutionHistory = true;
                settings.RediscoveryInterval = TimeSpan.Zero;
            });

        await using DistributedApplication app = builder.Build();
        IConfiguration configuration = AppHost.Configuration(await AppHost.Environment(app, dashboard.Resource));
        DashboardHostPlan plan = DashboardHostPlan.Read(configuration);

        plan.Problems.Should().BeEmpty("the host has to start on exactly what the resource gives it");
        plan.Mode.Should().Be(HostAuthenticationMode.Token);
        plan.Settings.Authentication.AllowTokenInUrl.Should().BeTrue("the resource's URL signs the administrator in");
        plan.Settings.Authentication.Tokens.Administrator.Should().Be(AppHost.AdminToken);
        plan.Settings.Authentication.Tokens.Viewer.Should().BeNull();
        plan.AcceptsAgents.Should().BeTrue();
        plan.Settings.Agents.Tokens.Primary.Should().Be(AppHost.AgentToken);

        plan.StoreTargets.Should().HaveCount(2);

        StoreTargetPlan inferred = plan.StoreTargets.Single(x => x.Name == "quartz-db");
        inferred.ConnectionString.Should().Be(AppHost.DatabaseConnectionString);
        inferred.Provider.Should().Be("Npgsql", "the host recognises Aspire's PostgreSQL connection string, as Quartz.Aspire does");
        inferred.Serializer.Should().Be("SystemTextJson");
        inferred.RediscoveryInterval.Should().Be(TimeSpan.FromMinutes(1));

        StoreTargetPlan billing = plan.StoreTargets.Single(x => x.Name == "billing");
        billing.TablePrefix.Should().Be("BILLING_");
        billing.Serializer.Should().Be("Newtonsoft");
        billing.ExecutionHistory.Should().BeTrue();
        billing.RediscoveryInterval.Should().BeNull("zero asks the database once");
    }

    [Test]
    public async Task AWorkerReferencingTheDashboardIsConfiguredAsItsAgent()
    {
        IDistributedApplicationBuilder builder = AppHost.Builder();
        IResourceBuilder<QuartzDashboardResource> dashboard = builder.AddQuartzDashboard("quartz");
        IResourceBuilder<ExecutableResource> worker = builder.AddExecutable("orders", "orders-worker", ".").WithReference(dashboard);
        AppHost.Allocate(dashboard.Resource, 18080);

        await using DistributedApplication app = builder.Build();
        IConfiguration configuration = AppHost.Configuration(await AppHost.Environment(app, worker.Resource));

        DashboardAgentOptions options = Agent(configuration, services => services.AddQuartz(quartz => quartz.UseDashboardAgent()), Options.DefaultName);

        options.Endpoint.Should().Be(new Uri("http://localhost:18080/quartz/agents"));
        options.Token.Should().Be(AppHost.AgentToken, "the agent presents the token the dashboard accepts");
        options.Target.Should().Be("orders");
    }

    [Test]
    public async Task ANamedSchedulerAmongSeveralIsConfiguredByItsOwnSection()
    {
        IDistributedApplicationBuilder builder = AppHost.Builder();
        IResourceBuilder<QuartzDashboardResource> dashboard = builder.AddQuartzDashboard("quartz");
        IResourceBuilder<ExecutableResource> worker = builder.AddExecutable("billing", "billing-worker", ".").WithReference(dashboard, schedulerName: "invoices");
        AppHost.Allocate(dashboard.Resource, 18080);

        await using DistributedApplication app = builder.Build();
        IConfiguration configuration = AppHost.Configuration(await AppHost.Environment(app, worker.Resource));

        DashboardAgentOptions options = Agent(
            configuration,
            services =>
            {
                services.AddQuartz("invoices", quartz => quartz.UseDashboardAgent());
                services.AddQuartz("reminders");
            },
            "invoices");

        options.Endpoint.Should().Be(new Uri("http://localhost:18080/quartz/agents"));
        options.Token.Should().Be(AppHost.AgentToken);
        options.Target.Should().Be("billing-invoices");
    }

    private static DashboardAgentOptions Agent(IConfiguration configuration, Action<IServiceCollection> register, string schedulerName)
    {
        ServiceCollection services = new();
        services.AddSingleton(configuration);
        register(services);

        using ServiceProvider provider = services.BuildServiceProvider();
        return provider.GetRequiredService<IOptionsMonitor<DashboardAgentOptions>>().Get(schedulerName);
    }
}
