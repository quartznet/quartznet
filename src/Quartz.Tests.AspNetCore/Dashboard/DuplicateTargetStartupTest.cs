using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Quartz.Tests.AspNetCore.Dashboard;

/// <summary>
/// One name holds one target of any kind: a store and an HTTP target cannot both be <c>prod</c>, and
/// neither can two HTTP targets.
/// </summary>
public sealed class DuplicateTargetStartupTest
{
    private SqliteTestDatabase database = null!;

    [SetUp]
    public void CreateEmptyDatabase()
    {
        database = new SqliteTestDatabase("duplicate-target");
    }

    [TearDown]
    public void DeleteDatabase()
    {
        database.Dispose();
    }

    [Test]
    public async Task AnHttpTargetAndAnAttachedStoreOfOneNameFailHostStartNamingBoth()
    {
        HostApplicationBuilder builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder();
        builder.Services.AddQuartzHttpClient(options =>
        {
            options.SchedulerName = "QuartzScheduler";
            options.Target = "prod";
            options.CreateHttpClient = _ => new HttpClient { BaseAddress = new Uri("http://unreachable.test/") };
        });
        builder.Services.AddQuartzDashboard(options => options.AttachStore(
            "prod",
            store => store.UseSqlite(SqliteFactory.Instance, database.ConnectionString)));

        using IHost host = builder.Build();

        Func<Task> start = () => host.StartAsync();

        (await start.Should().ThrowAsync<SchedulerConfigException>(
            "a target's name is half of every key its schedulers are shown under, so two targets under one "
            + "name would give two schedulers one spelling"))
            .Which.Message.Should().Contain("'prod'").And.Contain("store attached").And.Contain("HTTP target");
    }

    [Test]
    public void TwoHttpTargetsOfOneNameAreRefusedAtTheSecondRegistration()
    {
        ServiceCollection services = new();
        services.AddQuartzHttpClient(options =>
        {
            options.SchedulerName = "QuartzScheduler";
            options.Target = "prod";
            options.CreateHttpClient = _ => new HttpClient { BaseAddress = new Uri("http://one.test/") };
        });

        Action second = () => services.AddQuartzHttpClient(options =>
        {
            options.SchedulerName = "billing";
            options.Target = "PROD";
            options.CreateHttpClient = _ => new HttpClient { BaseAddress = new Uri("http://two.test/") };
        });

        second.Should().Throw<SchedulerConfigException>()
            .WithMessage("*'PROD'*", "the target is compared ignoring case, the way names are");
    }

    [TestCase("a/b")]
    [TestCase("a+b")]
    public void ATargetContainingASeparatorIsRefusedAtRegistration(string target)
    {
        ServiceCollection services = new();

        Action http = () => services.AddQuartzHttpClient(options =>
        {
            options.SchedulerName = "QuartzScheduler";
            options.Target = target;
            options.CreateHttpClient = _ => new HttpClient { BaseAddress = new Uri("http://one.test/") };
        });
        Action store = () => new QuartzDashboardOptions().AttachStore(target, _ => { });

        http.Should().Throw<Microsoft.Extensions.Options.OptionsValidationException>()
            .WithMessage($"*{target}*", "'/' separates a target from a name in a key, and '+' joins a cluster's members");
        store.Should().Throw<ArgumentException>().WithMessage($"*{target}*");
    }
}
