#nullable enable

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using Quartz.Extensibility;

namespace Quartz.Tests.Unit.Configuration;

/// <summary>
/// Guards <see cref="IQuartzBuilder.ConfigureOptions{TOptions}" />, the bridge that makes a component's
/// own options follow the scheduler it was registered for.
/// </summary>
/// <remarks>
/// The mechanism existed before, reachable only through <c>AddPlugin&lt;T, TOptions&gt;</c>. Anything
/// else the container built — a thread pool, a job store, a lock handler — asked for
/// <see cref="IOptions{TOptions}" /> and was handed the unnamed instance, so under
/// <c>AddQuartz("name", …)</c> it saw defaults, silently.
/// </remarks>
public class ConfigureOptionsTest
{
    /// <summary>
    /// The unnamed scheduler bypasses the scheduler-scoped provider entirely, so it works only because
    /// its <c>SchedulerName</c> and <see cref="Options.DefaultName" /> are the same string. If that
    /// ever stops being true, a callback registered here is written and never read — which is the
    /// silent drop this member exists to remove.
    /// </summary>
    [Test]
    public void TheUnnamedSchedulersNameIsTheDefaultOptionsName()
    {
        IQuartzBuilder? captured = null;
        new ServiceCollection().AddQuartz(q => captured = q);

        captured.Should().NotBeNull();
        captured!.SchedulerName.Should().Be(Options.DefaultName);
    }

    [Test]
    public void AComponentOnANamedSchedulerSeesTheOptionsConfiguredForIt()
    {
        var services = new ServiceCollection();
        services.AddQuartz("reporting", q =>
        {
            q.ConfigureOptions<CountingThreadPoolOptions>(options => options.Ceiling = 7);
            q.UseThreadPool<CountingThreadPool>();
        });

        using var provider = services.BuildServiceProvider();

        provider.GetRequiredKeyedService<IThreadPool>("reporting").Should().BeOfType<CountingThreadPool>()
            .Which.Ceiling.Should().Be(7, "the options were configured under this scheduler's name");
    }

    [Test]
    public void AComponentOnTheDefaultSchedulerSeesTheOptionsConfiguredForIt()
    {
        var services = new ServiceCollection();
        services.AddQuartz(q =>
        {
            q.ConfigureOptions<CountingThreadPoolOptions>(options => options.Ceiling = 3);
            q.UseThreadPool<CountingThreadPool>();
        });

        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IThreadPool>().Should().BeOfType<CountingThreadPool>()
            .Which.Ceiling.Should().Be(3);
    }

    [Test]
    public void TwoSchedulersSharingAnOptionsTypeKeepTheirOwnConfiguration()
    {
        var services = new ServiceCollection();
        services.AddQuartz("reporting", q =>
        {
            q.ConfigureOptions<CountingThreadPoolOptions>(options => options.Ceiling = 7);
            q.UseThreadPool<CountingThreadPool>();
        });
        services.AddQuartz("ingest", q =>
        {
            q.ConfigureOptions<CountingThreadPoolOptions>(options => options.Ceiling = 11);
            q.UseThreadPool<CountingThreadPool>();
        });

        using var provider = services.BuildServiceProvider();

        ((CountingThreadPool) provider.GetRequiredKeyedService<IThreadPool>("reporting")).Ceiling.Should().Be(7);
        ((CountingThreadPool) provider.GetRequiredKeyedService<IThreadPool>("ingest")).Ceiling.Should().Be(11);
    }

    /// <summary>
    /// Declaring the type is the half that matters; the callback is optional, because where the options
    /// come from is not something adding one should change.
    /// </summary>
    [Test]
    public void DeclaringWithoutACallbackStillRoutesToTheSchedulersOwnOptions()
    {
        var services = new ServiceCollection();
        services.Configure<CountingThreadPoolOptions>("reporting", options => options.Ceiling = 5);
        services.AddQuartz("reporting", q =>
        {
            q.ConfigureOptions<CountingThreadPoolOptions>();
            q.UseThreadPool<CountingThreadPool>();
        });

        using var provider = services.BuildServiceProvider();

        ((CountingThreadPool) provider.GetRequiredKeyedService<IThreadPool>("reporting")).Ceiling.Should().Be(5);
    }

    [Test]
    public void RepeatedCallsCompose()
    {
        var services = new ServiceCollection();
        services.AddQuartz("reporting", q =>
        {
            q.ConfigureOptions<CountingThreadPoolOptions>(options => options.Ceiling = 7);
            q.ConfigureOptions<CountingThreadPoolOptions>(options => options.Label = "second");
            q.UseThreadPool<CountingThreadPool>();
        });

        using var provider = services.BuildServiceProvider();

        var pool = (CountingThreadPool) provider.GetRequiredKeyedService<IThreadPool>("reporting");
        pool.Ceiling.Should().Be(7);
        pool.Label.Should().Be("second", "callbacks compose; the declaration is what is deduplicated");
    }

    /// <summary>
    /// The standalone builder is the same builder, so the same call has to work there — and return the
    /// concrete type, so the chain still reaches <c>BuildScheduler</c>.
    /// </summary>
    [Test]
    public async Task TheStandaloneBuilderConfiguresOptionsTheSameWay()
    {
        IScheduler scheduler = await QuartzSchedulerBuilder.Create()
            .ConfigureOptions<CountingThreadPoolOptions>(options => options.Ceiling = 4)
            .UseThreadPool<CountingThreadPool>()
            .UseInMemoryStore()
            .BuildScheduler();

        try
        {
            SchedulerMetadata metadata = await scheduler.GetMetadata();
            metadata.ThreadPoolTypeName.Should().Contain(nameof(CountingThreadPool));
            metadata.ThreadPoolSize.Should().Be(4, "the pool read its own options, configured for this scheduler");
        }
        finally
        {
            await scheduler.Shutdown(waitForJobsToComplete: false);
        }
    }

    public sealed class CountingThreadPoolOptions
    {
        public int Ceiling { get; set; } = 1;

        public string Label { get; set; } = "";
    }

    public sealed class CountingThreadPool : IThreadPool
    {
        public CountingThreadPool(IOptions<CountingThreadPoolOptions> options)
        {
            Ceiling = options.Value.Ceiling;
            Label = options.Value.Label;
        }

        public int Ceiling { get; }

        public string Label { get; }

        public int PoolSize => Ceiling;

        public ValueTask<bool> TryRun(Func<ValueTask> runnable, CancellationToken cancellationToken = default)
        {
            return new ValueTask<bool>(false);
        }

        public ValueTask<int> WaitForAvailableThreads(CancellationToken cancellationToken = default)
        {
            return new ValueTask<int>(Ceiling);
        }

        public ValueTask Initialize(CancellationToken cancellationToken = default) => default;

        public ValueTask Shutdown(bool waitForJobsToComplete = true, CancellationToken cancellationToken = default) => default;
    }
}
