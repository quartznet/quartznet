#nullable enable

using Microsoft.Extensions.DependencyInjection;

using Quartz.Impl.Matchers;
using Quartz.Listener;

namespace Quartz.Tests.Unit.Configuration;

/// <summary>
/// Guards the pairing between a listener and what it was registered with.
/// </summary>
/// <remarks>
/// A listener used to be registered apart from its matchers and re-joined to them by listener type once a
/// scheduler existed. Type is not an identity: two registrations of one type, a factory returning a
/// subtype, or two schedulers each with their own listener all collapse under it. The registration now
/// carries the whole pairing and is held under the scheduler's key, and the cases below are the ones that
/// silently produced a scheduler listening to the wrong things.
/// </remarks>
public class ListenerRegistrationTest
{
    [Test]
    public async Task TwoRegistrationsOfOneListenerTypeEachKeepTheirOwnMatchers()
    {
        var services = new ServiceCollection();
        services.AddQuartz(q =>
        {
            q.ConfigureScheduler(options => options.InstanceName = "audit-pair");
            q.AddJobListener<AuditListener>(_ => new AuditListener { Name = "reports-audit" }, GroupMatcher<JobKey>.GroupEquals("reports"));
            q.AddJobListener<AuditListener>(_ => new AuditListener { Name = "ingest-audit" }, GroupMatcher<JobKey>.GroupEquals("ingest"));
        });

        using var provider = services.BuildServiceProvider();
        var scheduler = await provider.GetRequiredService<ISchedulerFactory>().GetScheduler();
        try
        {
            var manager = scheduler.ListenerManager;

            manager.GetJobListeners().Should().HaveCount(2,
                "each registration is its own listener, whatever type the two of them happen to share");

            var reports = manager.GetJobListenerMatchers("reports-audit").Should().ContainSingle().Which;
            reports.IsMatch(new JobKey("nightly", "reports")).Should().BeTrue();
            reports.IsMatch(new JobKey("nightly", "ingest")).Should().BeFalse(
                "the matchers a listener was registered with are the ones it must end up with");

            var ingest = manager.GetJobListenerMatchers("ingest-audit").Should().ContainSingle().Which;
            ingest.IsMatch(new JobKey("nightly", "ingest")).Should().BeTrue();
            ingest.IsMatch(new JobKey("nightly", "reports")).Should().BeFalse();
        }
        finally
        {
            await scheduler.Shutdown();
        }
    }

    [Test]
    public async Task AFactoryReturningASubtypeAttachesTheListenerOnce()
    {
        var services = new ServiceCollection();
        services.AddQuartz(q =>
        {
            q.ConfigureScheduler(options => options.InstanceName = "subtype-factory");
            q.AddJobListener<AuditListener>(_ => new DerivedAuditListener { Name = "audit" }, GroupMatcher<JobKey>.GroupEquals("reports"));
        });

        using var provider = services.BuildServiceProvider();
        var scheduler = await provider.GetRequiredService<ISchedulerFactory>().GetScheduler();
        try
        {
            var manager = scheduler.ListenerManager;

            manager.GetJobListeners().Should().ContainSingle(
                "a listener recognised by its registration rather than by its runtime type cannot be attached twice")
                .Which.Should().BeOfType<DerivedAuditListener>();

            manager.GetJobListenerMatchers("audit").Should().ContainSingle().Which
                .IsMatch(new JobKey("nightly", "reports")).Should().BeTrue();
        }
        finally
        {
            await scheduler.Shutdown();
        }
    }

    [Test]
    public async Task AListenerRegisteredAsAServiceReachesANamedScheduler()
    {
        var services = new ServiceCollection();
        services.AddQuartz("reporting", q => { });
        services.AddSingleton<IJobListener>(new AuditListener { Name = "audit" });
        services.AddSingleton<ITriggerListener>(new AuditTriggerListener { Name = "trigger-audit" });
        services.AddSingleton<ISchedulerListener>(new RecordingSchedulerListener());

        using var provider = services.BuildServiceProvider();
        var scheduler = await provider.GetRequiredKeyedService<ISchedulerFactory>("reporting").GetScheduler();
        try
        {
            scheduler.ListenerManager.GetJobListeners().Should().ContainSingle(
                "an unkeyed listener service belongs to the container, so a named scheduler is not a scheduler it skips")
                .Which.Name.Should().Be("audit");

            scheduler.ListenerManager.GetTriggerListeners().Should().ContainSingle()
                .Which.Name.Should().Be("trigger-audit");

            scheduler.ListenerManager.GetSchedulerListeners().Should().ContainSingle();
        }
        finally
        {
            await scheduler.Shutdown();
        }
    }

    [Test]
    public async Task TwoJobListenersWithTheSameNameAreRejected()
    {
        var services = new ServiceCollection();
        services.AddQuartz(q =>
        {
            q.ConfigureScheduler(options => options.InstanceName = "duplicate-job-listeners");
            q.AddJobListener<AuditListener>(GroupMatcher<JobKey>.GroupEquals("reports"));
            q.AddJobListener<AuditListener>(_ => new DerivedAuditListener(), GroupMatcher<JobKey>.GroupEquals("ingest"));
        });

        using var provider = services.BuildServiceProvider();
        var create = async () => await provider.GetRequiredService<ISchedulerFactory>().GetScheduler();

        (await create.Should().ThrowAsync<SchedulerConfigException>(
                "a scheduler holds one listener per name, so the second would replace the first and drop its matchers"))
            .Which.Message.Should()
            .Contain("duplicate-job-listeners").And
            .Contain(nameof(AuditListener)).And
            .Contain(nameof(DerivedAuditListener));
    }

    [Test]
    public async Task TwoTriggerListenersWithTheSameNameAreRejected()
    {
        var services = new ServiceCollection();
        services.AddQuartz(q =>
        {
            q.ConfigureScheduler(options => options.InstanceName = "duplicate-trigger-listeners");
            q.AddTriggerListener(new AuditTriggerListener { Name = "audit" });
            q.AddTriggerListener(new AuditTriggerListener { Name = "audit" });
        });

        using var provider = services.BuildServiceProvider();
        var create = async () => await provider.GetRequiredService<ISchedulerFactory>().GetScheduler();

        (await create.Should().ThrowAsync<SchedulerConfigException>()).Which.Message.Should()
            .Contain("duplicate-trigger-listeners").And
            .Contain("audit");
    }

    [Test]
    public async Task OneSchedulersConfiguredListenersDoNotReachAnother()
    {
        var services = new ServiceCollection();
        services.AddQuartz("reporting", q => q.AddJobListener(new AuditListener { Name = "reporting-audit" }));
        services.AddQuartz("ingest", q => q.AddJobListener(new AuditListener { Name = "ingest-audit" }));

        using var provider = services.BuildServiceProvider();
        var reporting = await provider.GetRequiredKeyedService<ISchedulerFactory>("reporting").GetScheduler();
        var ingest = await provider.GetRequiredKeyedService<ISchedulerFactory>("ingest").GetScheduler();
        try
        {
            reporting.ListenerManager.GetJobListeners().Should().ContainSingle()
                .Which.Name.Should().Be("reporting-audit");
            ingest.ListenerManager.GetJobListeners().Should().ContainSingle()
                .Which.Name.Should().Be("ingest-audit");
        }
        finally
        {
            await reporting.Shutdown();
            await ingest.Shutdown();
        }
    }

    private class AuditListener : IJobListener
    {
        public string Name { get; set; } = nameof(AuditListener);

        public ValueTask JobToBeExecuted(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;

        public ValueTask JobExecutionVetoed(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;

        public ValueTask JobWasExecuted(IJobExecutionContext context, JobExecutionException? jobException, CancellationToken cancellationToken = default) => default;
    }

    private sealed class DerivedAuditListener : AuditListener;

    private sealed class AuditTriggerListener : ITriggerListener
    {
        public string Name { get; set; } = nameof(AuditTriggerListener);

        public ValueTask TriggerFired(ITrigger trigger, IJobExecutionContext context, CancellationToken cancellationToken = default) => default;

        public ValueTask<bool> VetoJobExecution(ITrigger trigger, IJobExecutionContext context, CancellationToken cancellationToken = default) => new(false);

        public ValueTask TriggerMisfired(ITrigger trigger, CancellationToken cancellationToken = default) => default;

        public ValueTask TriggerComplete(ITrigger trigger, IJobExecutionContext context, SchedulerInstruction triggerInstructionCode, CancellationToken cancellationToken = default) => default;
    }

    private sealed class RecordingSchedulerListener : SchedulerListenerSupport;
}
