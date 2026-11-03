using System.Reflection;

using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;

namespace Quartz.Tests.Aspire;

/// <summary>
/// What <c>AddQuartzDashboard</c>, <c>WithAttachedStore</c> and <c>WithReference(dashboard)</c> put in the
/// application model.
/// </summary>
public sealed class QuartzDashboardResourceTest
{
    [Test]
    public void TheDashboardRunsTheImageAtThePackagesOwnVersion()
    {
        IDistributedApplicationBuilder builder = AppHost.Builder();

        IResourceBuilder<QuartzDashboardResource> dashboard = builder.AddQuartzDashboard("quartz");

        ContainerImageAnnotation image = dashboard.Resource.Annotations.OfType<ContainerImageAnnotation>().Single();
        image.Registry.Should().Be("ghcr.io");
        image.Image.Should().Be("quartznet/quartz-dashboard");

        string informational = typeof(QuartzDashboardResource).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion;
        image.Tag.Should().Be(informational.Split('+')[0], "the image is released with the package, so the package runs its own version");
        image.Tag.Should().NotContain("+", "a tag cannot carry build metadata");

        dashboard.WithImageTag("4.8.0");
        dashboard.Resource.Annotations.OfType<ContainerImageAnnotation>().Single().Tag.Should().Be("4.8.0");
    }

    [TestCase("4.8.0+0123456789abcdef", "4.8.0")]
    [TestCase("4.8.0-rc.1", "4.8.0-rc.1")]
    [TestCase("4.8.0-preview-20261103-1200+0123456789abcdef", "4.8.0-preview-20261103-1200")]
    public void TheTagIsTheInformationalVersionWithoutItsCommit(string informational, string tag)
    {
        QuartzDashboardResourceBuilderExtensions.Tag(informational).Should().Be(tag);
    }

    [Test]
    public void TheDashboardServesHttpOnItsPortAndIsProbedAtHealthz()
    {
        IDistributedApplicationBuilder builder = AppHost.Builder();

        IResourceBuilder<QuartzDashboardResource> dashboard = builder.AddQuartzDashboard("quartz", port: 18080);

        EndpointAnnotation endpoint = dashboard.Resource.Annotations.OfType<EndpointAnnotation>().Single();
        endpoint.Name.Should().Be("http");
        endpoint.UriScheme.Should().Be("http");
        endpoint.TargetPort.Should().Be(8080, "the image listens on 8080");
        endpoint.Port.Should().Be(18080);

        dashboard.Resource.Annotations.OfType<HealthCheckAnnotation>().Should().ContainSingle();
    }

    [Test]
    public async Task TheTokensAreGeneratedSecretsUnlessGiven()
    {
        IDistributedApplicationBuilder builder = AppHost.Builder();

        QuartzDashboardResource generated = builder.AddQuartzDashboard("fresh").Resource;

        generated.AdminToken.Name.Should().Be("fresh-admin-token");
        generated.AdminToken.Secret.Should().BeTrue();
        generated.AgentToken.Name.Should().Be("fresh-agent-token");
        generated.AgentToken.Secret.Should().BeTrue();

        string generatedAdmin = (await generated.AdminToken.GetValueAsync(CancellationToken.None))!;
        string generatedAgent = (await generated.AgentToken.GetValueAsync(CancellationToken.None))!;
        generatedAdmin.Should().HaveLength(32).And.MatchRegex("^[A-Za-z0-9]+$", "the administrator's token travels in a URL");
        generatedAgent.Should().HaveLength(32).And.NotBe(generatedAdmin, "each token is generated on its own");

        IResourceBuilder<ParameterResource> admin = builder.AddParameter("ops-admin", "AnAdministratorToken0123456789ab", secret: true);
        IResourceBuilder<ParameterResource> agent = builder.AddParameter("ops-agent", "AnAgentToken0123456789abcdefABCD", secret: true);
        QuartzDashboardResource given = builder.AddQuartzDashboard("ops", adminToken: admin, agentToken: agent).Resource;

        given.AdminToken.Should().BeSameAs(admin.Resource);
        given.AgentToken.Should().BeSameAs(agent.Resource);
    }

    [Test]
    public async Task TheDashboardIsToldToSignInByTokenAndToAcceptAgents()
    {
        IDistributedApplicationBuilder builder = AppHost.Builder();
        IResourceBuilder<QuartzDashboardResource> dashboard = builder.AddQuartzDashboard("quartz");

        await using DistributedApplication app = builder.Build();
        Dictionary<string, string> environment = await AppHost.Environment(app, dashboard.Resource);

        environment.Should().Contain("Quartz__Dashboard__Authentication__Mode", "Token");
        environment.Should().Contain("Quartz__Dashboard__Authentication__AllowTokenInUrl", "true");
        environment.Should().Contain("Quartz__Dashboard__Authentication__Tokens__Administrator", AppHost.AdminToken);
        environment.Should().Contain("Quartz__Dashboard__Agents__Tokens__Primary", AppHost.AgentToken);
        environment.Should().ContainKey("OTEL_EXPORTER_OTLP_ENDPOINT", "the dashboard's own telemetry goes to the Aspire dashboard");
    }

    [Test]
    public async Task AnAttachedStoreIsATargetNamedForTheDatabase()
    {
        IDistributedApplicationBuilder builder = AppHost.Builder();
        IResourceBuilder<IResourceWithConnectionString> database = builder.AddConnectionString("quartz-db");

        IResourceBuilder<QuartzDashboardResource> dashboard = builder.AddQuartzDashboard("quartz")
            .WithAttachedStore(database)
            .WithAttachedStore(database, "billing", settings =>
            {
                settings.Provider = "Npgsql";
                settings.TablePrefix = "BILLING_";
                settings.Serializer = "Newtonsoft";
                settings.ExecutionHistory = true;
                settings.RediscoveryInterval = TimeSpan.FromMinutes(5);
            });

        await using DistributedApplication app = builder.Build();
        Dictionary<string, string> environment = await AppHost.Environment(app, dashboard.Resource);

        environment.Should().Contain("Quartz__Dashboard__Targets__quartz-db__ConnectionString", AppHost.DatabaseConnectionString);
        environment.Keys.Where(x => x.StartsWith("Quartz__Dashboard__Targets__quartz-db__", StringComparison.Ordinal))
            .Should().ContainSingle("a store with no settings says only where it is");

        environment.Should().Contain("Quartz__Dashboard__Targets__billing__ConnectionString", AppHost.DatabaseConnectionString);
        environment.Should().Contain("Quartz__Dashboard__Targets__billing__Provider", "Npgsql");
        environment.Should().Contain("Quartz__Dashboard__Targets__billing__TablePrefix", "BILLING_");
        environment.Should().Contain("Quartz__Dashboard__Targets__billing__Serializer", "Newtonsoft");
        environment.Should().Contain("Quartz__Dashboard__Targets__billing__ExecutionHistory", "true");
        environment.Should().Contain("Quartz__Dashboard__Targets__billing__RediscoveryInterval", "00:05:00");

        dashboard.Resource.Annotations.OfType<WaitAnnotation>().Select(x => x.Resource).Should().Contain(database.Resource,
            "the dashboard waits for the database it reads");
    }

    [Test]
    public async Task AWorkerIsToldWhereTheDashboardIsWithWhatTokenAndUnderWhichTarget()
    {
        IDistributedApplicationBuilder builder = AppHost.Builder();
        IResourceBuilder<QuartzDashboardResource> dashboard = builder.AddQuartzDashboard("quartz");
        IResourceBuilder<ExecutableResource> worker = builder.AddExecutable("orders", "orders-worker", ".").WithReference(dashboard);
        IResourceBuilder<ExecutableResource> billing = builder.AddExecutable("billing", "billing-worker", ".").WithReference(dashboard, schedulerName: "invoices");

        AppHost.Allocate(dashboard.Resource, 18080);

        await using DistributedApplication app = builder.Build();
        Dictionary<string, string> orders = await AppHost.Environment(app, worker.Resource);
        orders.Should().Contain("Quartz__DashboardAgent__Endpoint", "http://localhost:18080/quartz/agents");
        orders.Should().Contain("Quartz__DashboardAgent__Token", AppHost.AgentToken);
        orders.Should().Contain("Quartz__DashboardAgent__Target", "orders");

        Dictionary<string, string> invoices = await AppHost.Environment(app, billing.Resource);
        invoices.Should().Contain("Quartz__Schedulers__invoices__DashboardAgent__Endpoint", "http://localhost:18080/quartz/agents");
        invoices.Should().Contain("Quartz__Schedulers__invoices__DashboardAgent__Token", AppHost.AgentToken);
        invoices.Should().Contain("Quartz__Schedulers__invoices__DashboardAgent__Target", "billing-invoices");
        invoices.Keys.Should().NotContain(x => x.StartsWith("Quartz__DashboardAgent__", StringComparison.Ordinal));
    }

    [Test]
    public async Task TheResourceUrlIsTheAdministratorsSignInLink()
    {
        IDistributedApplicationBuilder builder = AppHost.Builder();
        IResourceBuilder<QuartzDashboardResource> dashboard = builder.AddQuartzDashboard("quartz");
        AppHost.Allocate(dashboard.Resource, 18080);

        EndpointAnnotation endpoint = dashboard.Resource.Annotations.OfType<EndpointAnnotation>().Single();
        List<ResourceUrlAnnotation> urls = [new() { Url = "http://localhost:18080", Endpoint = new EndpointReference(dashboard.Resource, endpoint) }];
        ResourceUrlsCallbackContext context = new(
            new DistributedApplicationExecutionContext(DistributedApplicationOperation.Run),
            dashboard.Resource,
            urls,
            CancellationToken.None);

        foreach (ResourceUrlsCallbackAnnotation callback in dashboard.Resource.Annotations.OfType<ResourceUrlsCallbackAnnotation>())
        {
            await callback.Callback(context);
        }

        urls.Should().ContainSingle().Which.Url.Should().Be($"http://localhost:18080/login?t={AppHost.AdminToken}");
    }

    [Test]
    public void ATargetNameTakenTwiceIsRefusedWhileTheAppHostIsBuilt()
    {
        IDistributedApplicationBuilder builder = AppHost.Builder();
        IResourceBuilder<IResourceWithConnectionString> database = builder.AddConnectionString("quartz-db");
        IResourceBuilder<QuartzDashboardResource> dashboard = builder.AddQuartzDashboard("quartz").WithAttachedStore(database);
        IResourceBuilder<ExecutableResource> worker = builder.AddExecutable("orders", "orders-worker", ".").WithReference(dashboard);

        Action secondStore = () => dashboard.WithAttachedStore(database);
        secondStore.Should().Throw<InvalidOperationException>().WithMessage("*already has a target named 'quartz-db'*");

        Action secondAgent = () => worker.WithReference(dashboard);
        secondAgent.Should().Throw<InvalidOperationException>().WithMessage("*already has a target named 'orders'*");

        Action storeNamedLikeTheAgent = () => dashboard.WithAttachedStore(builder.AddConnectionString("other-db"), "orders");
        storeNamedLikeTheAgent.Should().Throw<InvalidOperationException>("an agent and a store share the dashboard's target names")
            .WithMessage("*already has a target named 'orders'*");

        Action slash = () => dashboard.WithAttachedStore(database, "a/b");
        slash.Should().Throw<ArgumentException>().WithMessage("*cannot be a target*");
    }
}
