#nullable enable

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using Quartz.Extensibility;

namespace Quartz.Tests.Unit.Impl;

/// <summary>
/// What <c>[FromKeyedServices("acme")] IScheduler</c> points at once the scheduler behind it has been
/// replaced.
/// </summary>
/// <remarks>
/// The handle a container hands out is a proxy that resolves its scheduler on first use and remembers
/// it, and every constructor in the application holds one. A restart builds a second generation under
/// the same name, so a handle that never looked again would leave the whole application injecting the
/// dead one while the repository, the dashboard and the HTTP API all showed the live one. The
/// registration is the identity, not the instance.
/// </remarks>
[NonParallelizable]
public sealed class DeferredSchedulerGenerationTest
{
    [Test]
    public async Task AHandleRepointsAtTheGenerationARestartBuilt()
    {
        await using ServiceProvider provider = Container();

        IScheduler handle = provider.GetRequiredKeyedService<IScheduler>("acme");
        await handle.Start();

        string firstId = handle.SchedulerInstanceId;
        firstId.Should().NotBeNull();

        IScheduler next = await provider.GetRequiredService<ISchedulerRuntime>().Restart("acme");

        handle.Status.Should().Be(SchedulerStatus.Running,
            "the handle a constructor was injected with has to answer for the scheduler that is alive, not "
            + "for the one it happened to resolve first");

        (await handle.GetMetadata()).SchedulerInstanceId.Should().Be(next.SchedulerInstanceId);
        (await handle.GetJobDetail(new JobKey("nothing"))).Should().BeNull(
            "an asynchronous member re-points too, and does it without a second build");

        await next.Shutdown();
    }

    [Test]
    public async Task AHandleWithNoLiveSchedulerLeftStillRefuses()
    {
        await using ServiceProvider provider = Container();

        IScheduler handle = provider.GetRequiredKeyedService<IScheduler>("acme");
        await handle.Start();
        await handle.Shutdown();

        Func<Task> act = async () => await handle.GetJobDetail(new JobKey("nothing"));

        await act.Should().ThrowAsync<SchedulerException>(
                "asking again finds nothing in the repository and falls through to the container's own "
                + "scheduler, which is shut down - re-pointing must not become rebuilding")
            .WithMessage("*has been shut down*");
    }

    private static ServiceProvider Container()
    {
        ServiceCollection services = new();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddLogging();
        services.AddQuartz("acme", q => q.UseInMemoryStore());
        return services.BuildServiceProvider();
    }
}
