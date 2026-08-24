using Microsoft.Extensions.DependencyInjection;

using Quartz.Extensibility;

namespace Quartz.Tests.Unit.Configuration;

/// <summary>
/// A scheduler name is compared two different ways, and knowing which is which saves an afternoon: the
/// repository — and so every lookup, including the HTTP API's route — compares ignoring case, while
/// keyed resolution out of the container compares ordinally, because a service key is compared by
/// equality and string equality is ordinal.
/// </summary>
/// <remarks>
/// Neither comparison is wrong for what it does, and neither is going to change: the repository's is what
/// makes the duplicate-name check and the API route forgiving, and the container's is Microsoft's. What
/// was missing is a test that says so, so that a future reader meeting one of the two halves does not
/// conclude the other is a bug. <c>TenantSchedulerRoutingTest</c> is the same registration seen from the
/// HTTP side.
/// </remarks>
[NonParallelizable]
public sealed class SchedulerNameComparisonTest
{
    private const string RegisteredName = "Acme";
    private const string OtherCasing = "acme";

    [Test]
    public async Task KeyedResolutionComparesTheNameOrdinally()
    {
        ServiceCollection services = new();
        services.AddQuartz(RegisteredName, _ => { });

        // Awaited disposal: resolving IScheduler hands back a DeferredScheduler, which the container can
        // only dispose asynchronously.
        await using ServiceProvider provider = services.BuildServiceProvider();

        provider.GetRequiredKeyedService<IScheduler>(RegisteredName).Should().NotBeNull(
            "the spelling the registration used is the service key, and resolving by it is the supported way in");

        Action mismatched = () => provider.GetRequiredKeyedService<IScheduler>(OtherCasing);
        mismatched.Should().Throw<InvalidOperationException>(
            "the container compares service keys by equality and string equality is ordinal, so a name spelled "
            + "with different casing is a different key rather than the same one leniently matched");
    }

    [Test]
    public async Task TheRepositoryFindsTheSchedulerUnderEitherCasing()
    {
        ServiceCollection services = new();
        services.AddQuartz(RegisteredName, _ => { });

        using ServiceProvider provider = services.BuildServiceProvider();

        IScheduler scheduler = await provider.GetRequiredKeyedService<ISchedulerFactory>(RegisteredName).GetScheduler();

        try
        {
            ISchedulerRepository repository = provider.GetRequiredService<ISchedulerRepository>();

            repository.Lookup(OtherCasing).Should().BeSameAs(scheduler,
                "the repository indexes names ignoring case, which is what makes the duplicate-name check and "
                + "the HTTP API's route forgiving about casing - and is the opposite of the container's rule");
            repository.Lookup(RegisteredName).Should().BeSameAs(scheduler);

            scheduler.SchedulerName.Should().Be(RegisteredName,
                "the scheduler carries the spelling it was registered with, whichever spelling found it");
        }
        finally
        {
            await scheduler.Shutdown();
        }
    }
}
