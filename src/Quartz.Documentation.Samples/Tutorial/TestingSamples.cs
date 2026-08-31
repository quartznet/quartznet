using Microsoft.Extensions.DependencyInjection;

using Quartz.Extensibility;
using Quartz.Impl;

namespace Quartz.Documentation.Samples.Tutorial;

/// <summary>
/// Samples for docs/documentation/quartz-4.x/tutorial/testing.md.
/// </summary>
/// <remarks>
/// The blocks that assert, or that drive a <c>FakeTimeProvider</c>, stay hand-written fences on the
/// page: compiling them would mean a test framework, an assertion library and
/// <c>Microsoft.Extensions.TimeProvider.Testing</c> as dependencies of a documentation project.
/// </remarks>
public static class TestingSamples
{
    public static async ValueTask AnInMemorySchedulerPerTest()
    {
        #region sample_testing_in_memory_scheduler

        await using StandaloneSchedulerFactory factory = QuartzSchedulerBuilder
            .Create(q => q
                .UseInMemoryStore()
                .ConfigureScheduler(o => o.InstanceName = $"test-{Guid.NewGuid():N}"))
            .Build();

        IScheduler scheduler = await factory.GetScheduler();
        await scheduler.Start();

        #endregion
    }

    public static void ShorteningTheIdleWait()
    {
        QuartzSchedulerBuilder.Create(q => q
            #region sample_testing_idle_wait_time

            .ConfigureScheduler(o => o.IdleWaitTime = TimeSpan.FromSeconds(1))

            #endregion
            .UseInMemoryStore());
    }

    public static void MisfireThreshold(TimeProvider clock)
    {
        QuartzSchedulerBuilder builder =
            #region sample_testing_misfire_threshold

            QuartzSchedulerBuilder.Create(q => q
                .UseInMemoryStore(o => o.MisfireThreshold = TimeSpan.FromMilliseconds(50))
                .UseTimeProvider(clock))

            #endregion
            ;
    }

    public static void InjectingAFault()
    {
        QuartzSchedulerBuilder builder =
            #region sample_testing_fault_injection_registration

            QuartzSchedulerBuilder.Create(q => q
                .UseJobStore(sp => new FlakyJobStore(ActivatorUtilities.CreateInstance<RAMJobStore>(sp))))

            #endregion
            ;
    }
}

#region sample_testing_completion_listener

internal sealed class CompletionListener : IJobListener
{
    private readonly TaskCompletionSource<JobExecutionException?> completed =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Task<JobExecutionException?> Completed => completed.Task;

    public ValueTask JobWasExecuted(
        IJobExecutionContext context,
        JobExecutionException? jobException,
        CancellationToken cancellationToken = default)
    {
        completed.TrySetResult(jobException);
        return default;
    }
}

#endregion

#region sample_testing_flaky_job_store

internal sealed class FlakyJobStore(IJobStore inner) : DelegatingJobStore(inner)
{
    public int AcquireCalls { get; private set; }

    public override ValueTask<List<IOperableTrigger>> AcquireNextTriggers(
        TriggerAcquisitionRequest request,
        CancellationToken cancellationToken = default)
    {
        AcquireCalls++;
        if (AcquireCalls == 1)
        {
            throw new JobPersistenceException("simulated outage");
        }

        return base.AcquireNextTriggers(request, cancellationToken);
    }
}

#endregion
