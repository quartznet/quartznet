#nullable enable

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Quartz.Tests.Unit.Configuration;

/// <summary>
/// Guards that a configuration mistake is reported when the host starts, by one mechanism, rather than
/// whenever the component that reads it happens to be built.
/// </summary>
/// <remarks>
/// <see cref="IStartupValidator"/> is what a host resolves and runs at the end of <c>Build()</c>, so
/// resolving it here is the same check the application gets — without needing a host.
/// </remarks>
public class OptionsValidationTest
{
    [Test]
    public void ABadSchedulerSettingFailsAtStartupRatherThanAtFirstUse()
    {
        var services = new ServiceCollection();
        services.AddQuartz(q => q.ConfigureScheduler(options => options.IdleWaitTime = TimeSpan.FromMilliseconds(5)));

        using var provider = services.BuildServiceProvider();

        var act = () => provider.GetRequiredService<IStartupValidator>().Validate();

        act.Should().Throw<OptionsValidationException>().WithMessage("*IdleWaitTime*",
            "nothing had to resolve QuartzSchedulerOptions for the mistake to be reported");
    }

    [Test]
    public void ABadThreadPoolSettingFailsAtStartup()
    {
        var services = new ServiceCollection();
        services.AddQuartz(q => q.UseDefaultThreadPool(options => options.MaxConcurrency = 0));

        using var provider = services.BuildServiceProvider();

        var act = () => provider.GetRequiredService<IStartupValidator>().Validate();

        act.Should().Throw<OptionsValidationException>().WithMessage("*MaxConcurrency*");
    }

    /// <summary>
    /// The batch size and the pool size are configured through different builder methods and different
    /// configuration sections, so the pair is only ever wrong by accident — which is what makes it worth
    /// checking.
    /// </summary>
    [Test]
    public void ABatchLargerThanTheThreadPoolFailsAtStartup()
    {
        var services = new ServiceCollection();
        services.AddQuartz(q =>
        {
            q.UseDefaultThreadPool(maxConcurrency: 4);
            q.ConfigureScheduler(options => options.MaxBatchSize = 10);
        });

        using var provider = services.BuildServiceProvider();

        var act = () => provider.GetRequiredService<IStartupValidator>().Validate();

        act.Should().Throw<OptionsValidationException>().WithMessage("*MaxBatchSize*MaxConcurrency*");
    }

    [Test]
    public void ABatchThatFitsTheThreadPoolIsFine()
    {
        var services = new ServiceCollection();
        services.AddQuartz(q =>
        {
            q.UseDefaultThreadPool(maxConcurrency: 10);
            q.ConfigureScheduler(options => options.MaxBatchSize = 10);
        });

        using var provider = services.BuildServiceProvider();

        var act = () => provider.GetRequiredService<IStartupValidator>().Validate();

        act.Should().NotThrow();
    }

    /// <summary>
    /// The ADO.NET options are only checked for a scheduler that chose a persistent store. Validating
    /// them everywhere would make an unset <c>DataSource</c> a startup failure for every in-memory
    /// scheduler, which is a configuration nobody wrote.
    /// </summary>
    [Test]
    public void TheJobStoreOptionsOfAStoreNobodyChoseAreNotChecked()
    {
        var services = new ServiceCollection();
        services.AddQuartz(q => q.UseInMemoryStore());

        using var provider = services.BuildServiceProvider();

        var act = () => provider.GetRequiredService<IStartupValidator>().Validate();

        act.Should().NotThrow("AdoJobStoreOptions.DataSource is unset, and this scheduler never reads it");
    }

    [Test]
    public void AMissingDataSourceOnAPersistentStoreFailsAtStartup()
    {
        var services = new ServiceCollection();
        services.AddQuartz(q => q.UsePersistentStore(store => store.Configure(options => options.DataSource = "")));

        using var provider = services.BuildServiceProvider();

        var act = () => provider.GetRequiredService<IStartupValidator>().Validate();

        act.Should().Throw<OptionsValidationException>().WithMessage("*DataSource*");
    }

    /// <summary>
    /// Asking for clustering and then switching it off leaves database locking on, no cluster manager
    /// and no check-in row — a shape nobody means to configure. Not clustering is spelled by not calling
    /// <c>UseClustering</c>.
    /// </summary>
    [Test]
    public void ClusteringThatWasAskedForAndThenTurnedOffIsAConfigurationError()
    {
        var services = new ServiceCollection();
        services.AddQuartz(q => q.UsePersistentStore(store =>
        {
            store.Configure(options => options.DataSource = "test");
            store.UseClustering(clustering => clustering.Enabled = false);
        }));

        using var provider = services.BuildServiceProvider();

        var act = () => provider.GetRequiredService<IStartupValidator>().Validate();

        act.Should().Throw<OptionsValidationException>().WithMessage("*UseClustering*");
    }

    [Test]
    public void ClusteringLeftEnabledIsFine()
    {
        var services = new ServiceCollection();
        services.AddQuartz(q => q.UsePersistentStore(store =>
        {
            store.Configure(options => options.DataSource = "test");
            store.UseClustering(clustering => clustering.CheckinInterval = TimeSpan.FromSeconds(20));
        }));

        using var provider = services.BuildServiceProvider();

        var act = () => provider.GetRequiredService<IStartupValidator>().Validate();

        act.Should().NotThrow();
    }

    /// <summary>
    /// The rule is per scheduler: one clustered and one not is an ordinary deployment.
    /// </summary>
    [Test]
    public void ASchedulerWithoutClusteringIsUnaffectedByAnotherThatHasIt()
    {
        var services = new ServiceCollection();
        services.AddQuartz("clustered", q => q.UsePersistentStore(store =>
        {
            store.Configure(options => options.DataSource = "test");
            store.UseClustering();
        }));
        services.AddQuartz("solo", q => q.UsePersistentStore(store =>
            store.Configure(options => options.DataSource = "test")));

        using var provider = services.BuildServiceProvider();

        var act = () => provider.GetRequiredService<IStartupValidator>().Validate();

        act.Should().NotThrow();
        provider.GetRequiredService<IOptionsMonitor<ClusteringOptions>>().Get("solo").Enabled.Should().BeFalse();
    }
}
