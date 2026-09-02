using Microsoft.Extensions.DependencyInjection;

using Quartz.Core;

namespace Quartz.Tests.Unit.Core;

/// <summary>
/// Guards the refusal of a listener whose public method has a notification's name but not its signature.
/// </summary>
/// <remarks>
/// Every listener member has a default implementation, so a stale signature is not a compile error: the
/// method stops implementing anything, the default runs, and the listener is silently never called. The
/// cases below are the three migrations that produce that shape — a 3.x listener returning
/// <see cref="Task" />, a 4.0.0-alpha.1 listener whose callbacks do not lead with the scheduler, and a
/// listener whose <c>TriggerMisfired</c> still leads with the scheduler rather than the trigger — plus
/// the shapes that must keep working, since a check that refuses a sound listener is worse than no check.
/// </remarks>
public class ListenerShapeTest
{
    private ListenerManagerImpl manager;

    [SetUp]
    public void SetUp()
    {
        manager = new ListenerManagerImpl();
    }

    [Test]
    public void AJobListenerWithTheCurrentShapeIsAccepted()
    {
        Action act = () => manager.AddJobListener(new SoundJobListener());

        act.Should().NotThrow("a listener whose members implement the interface is what the check is for");
        manager.GetJobListeners().Should().ContainSingle();
    }

    [Test]
    public void ATriggerListenerWithTheCurrentShapeIsAccepted()
    {
        Action act = () => manager.AddTriggerListener(new SoundTriggerListener());

        act.Should().NotThrow(
            "TriggerMisfired takes the scheduler after the trigger and TriggerFired takes a context there, "
            + "and both are implemented here");
        manager.GetTriggerListeners().Should().ContainSingle();
    }

    [Test]
    public void ASchedulerListenerWithTheCurrentShapeIsAccepted()
    {
        Action act = () => manager.AddSchedulerListener(new SoundSchedulerListener());

        act.Should().NotThrow(
            "SchedulerError takes the scheduler and a SchedulerErrorContext here, which is the 4.0.0-alpha.2 shape");
        manager.GetSchedulerListeners().Should().ContainSingle();
    }

    [Test]
    public void AListenerThatLeavesEveryNotificationToItsDefaultIsAccepted()
    {
        Action act = () => manager.AddJobListener(new SilentJobListener());

        act.Should().NotThrow(
            "implementing only the notifications you care about is the whole point of the default members");
    }

    [Test]
    public void AMethodWhoseNameIsNotANotificationIsLeftAlone()
    {
        Action act = () => manager.AddJobListener(new HelpfulJobListener());

        act.Should().NotThrow("a listener's own methods are its own business");
    }

    [Test]
    public void AnExplicitImplementationIsAccepted()
    {
        Action act = () => manager.AddSchedulerListener(new ExplicitSchedulerListener());

        act.Should().NotThrow(
            "an explicit implementation is not a public method of the class, so there is nothing to mistake it for");
    }

    [Test]
    public void AListenerInheritingItsNotificationsIsAccepted()
    {
        Action act = () => manager.AddJobListener(new InheritingJobListener());

        act.Should().NotThrow(
            "a notification implemented on a base class implements it for every listener that derives from it");
    }

    [Test]
    public void AStaleMemberInheritedFromABaseIsRefused()
    {
        Action act = () => manager.AddJobListener(new InheritsAThreeXBase());

        act.Should().Throw<SchedulerConfigException>(
            "where the dead method was written does not change that it is dead")
            .Which.Message.Should().Contain(nameof(IJobListener.JobToBeExecuted));
    }

    [Test]
    public void AThreeXJobListenerIsRefusedAndToldAboutValueTask()
    {
        Action act = () => manager.AddJobListener(new ThreeXJobListener());

        act.Should().Throw<SchedulerConfigException>()
            .Which.Message.Should()
            .Contain(nameof(IJobListener.JobToBeExecuted), "the refusal is worth nothing unless it names the dead member")
            .And.Contain(nameof(ThreeXJobListener), "and the listener that carries it")
            .And.Contain("return ValueTask rather than Task", "which is what a 3.x listener has to change");
    }

    [Test]
    public void AThreeXTriggerListenerIsRefusedAndToldAboutValueTask()
    {
        Action act = () => manager.AddTriggerListener(new ThreeXTriggerListener());

        act.Should().Throw<SchedulerConfigException>()
            .Which.Message.Should()
            .Contain(nameof(ITriggerListener.VetoJobExecution))
            .And.Contain("return ValueTask rather than Task");
    }

    [Test]
    public void AThreeXSchedulerListenerIsRefusedAndToldAboutValueTask()
    {
        Action act = () => manager.AddSchedulerListener(new ThreeXSchedulerListener());

        act.Should().Throw<SchedulerConfigException>()
            .Which.Message.Should()
            .Contain(nameof(ISchedulerListener.SchedulerStarted))
            .And.Contain("return ValueTask rather than Task");
    }

    [Test]
    public void AnAlphaOneSchedulerErrorIsRefusedAndToldAboutTheScheduler()
    {
        Action act = () => manager.AddSchedulerListener(new AlphaOneSchedulerErrorListener());

        act.Should().Throw<SchedulerConfigException>()
            .Which.Message.Should()
            .Contain("SchedulerError(String, SchedulerException, CancellationToken)",
                "the signature that was written is how the reader finds the method")
            .And.Contain("SchedulerError(IScheduler, SchedulerErrorContext, CancellationToken)",
                "and the signature it has to become is the fix")
            .And.Contain("take IScheduler scheduler first in 4.0");
    }

    [Test]
    public void AnAlphaOneLifecycleCallbackIsRefusedAndToldAboutTheScheduler()
    {
        Action act = () => manager.AddSchedulerListener(new AlphaOneLifecycleListener());

        act.Should().Throw<SchedulerConfigException>()
            .Which.Message.Should()
            .Contain(nameof(ISchedulerListener.SchedulerShuttingDown))
            .And.Contain("take IScheduler scheduler first in 4.0");
    }

    [Test]
    public void AnAlphaOneTriggerMisfiredIsRefused()
    {
        Action act = () => manager.AddTriggerListener(new AlphaOneTriggerListener());

        act.Should().Throw<SchedulerConfigException>()
            .Which.Message.Should()
            .Contain(nameof(ITriggerListener.TriggerMisfired))
            .And.Contain("ValueTask TriggerMisfired(ITrigger, IScheduler, CancellationToken)",
                "a listener that never gained the scheduler is told the signature it has to reach");
    }

    [Test]
    public void AMisfireThatStillLeadsWithTheSchedulerIsRefusedAndToldTheOrderChanged()
    {
        Action act = () => manager.AddTriggerListener(new SchedulerFirstMisfireListener());

        act.Should().Throw<SchedulerConfigException>()
            .Which.Message.Should()
            .Contain(nameof(ITriggerListener.TriggerMisfired))
            .And.Contain("in a different order",
                "the two signatures hold the same types, so a reader comparing them finds everything they expect")
            .And.Contain("takes the trigger first in 4.0");
    }

    [Test]
    public void AThreeXListenerIsNotAccusedOfReorderingItsParameters()
    {
        Action act = () => manager.AddJobListener(new ThreeXJobListener());

        act.Should().Throw<SchedulerConfigException>()
            .Which.Message.Should()
            .NotContain("in a different order",
                "the parameters of a 3.x listener are in the right order and the return type is what is wrong");
    }

    [Test]
    public void ARefusalPointsAtTheMigrationGuide()
    {
        Action act = () => manager.AddSchedulerListener(new AlphaOneLifecycleListener());

        act.Should().Throw<SchedulerConfigException>()
            .Which.Message.Should()
            .Contain("migration-guide.html#listeners-are-told-which-scheduler-is-calling",
                "a reader who has never heard of this change needs somewhere to go");
    }

    [Test]
    public void ARefusalIsRepeatedForEveryRegistration()
    {
        Action act = () => manager.AddJobListener(new ThreeXJobListener());

        act.Should().Throw<SchedulerConfigException>();
        act.Should().Throw<SchedulerConfigException>(
            "the answer is remembered only for a listener that passed, so a second registration is refused too");
    }

    [Test]
    public void AListenerRegisteredByTypeIsRefusedWhereItIsWritten()
    {
        Action act = () => new ServiceCollection().AddQuartz(q =>
        {
            q.ConfigureScheduler(options => options.InstanceName = "listener-shape-by-type");
            q.AddJobListener<ThreeXJobListener>();
        });

        act.Should().Throw<SchedulerConfigException>(
            "the type is known while the application is still writing its configuration, which is the earliest anyone can be told")
            .Which.Message.Should().Contain(nameof(IJobListener.JobToBeExecuted));
    }

    [Test]
    public void AListenerRegisteredByInstanceIsRefusedWhereItIsWritten()
    {
        Action act = () => new ServiceCollection().AddQuartz(q =>
        {
            q.ConfigureScheduler(options => options.InstanceName = "listener-shape-by-instance");
            q.AddSchedulerListener(new AlphaOneSchedulerErrorListener());
        });

        act.Should().Throw<SchedulerConfigException>()
            .Which.Message.Should().Contain("take IScheduler scheduler first in 4.0");
    }

    [Test]
    public async Task AListenerRegisteredByFactoryIsRefusedWhenTheSchedulerIsBuilt()
    {
        ServiceCollection services = new();
        services.AddQuartz(q =>
        {
            q.ConfigureScheduler(options => options.InstanceName = "listener-shape-by-factory");

            // Declared as the interface, so the registration names no methods to check: what the factory
            // produces is not known until there is a scheduler to attach it to.
            q.AddJobListener<IJobListener>(_ => new ThreeXJobListener());
        });

        await using ServiceProvider provider = services.BuildServiceProvider();

        Func<Task> act = async () => await provider.GetRequiredService<ISchedulerFactory>().GetScheduler();

        (await act.Should().ThrowAsync<SchedulerConfigException>(
            "a listener whose type only the factory knows is still refused, at the moment it is attached"))
            .Which.Message.Should().Contain(nameof(IJobListener.JobToBeExecuted));
    }

    [Test]
    public async Task ASoundListenerRegistersThroughTheContainer()
    {
        ServiceCollection services = new();
        services.AddQuartz(q =>
        {
            q.ConfigureScheduler(options => options.InstanceName = "listener-shape-sound");
            q.AddJobListener<SoundJobListener>();
            q.AddTriggerListener<SoundTriggerListener>();
            q.AddSchedulerListener<SoundSchedulerListener>();
        });

        await using ServiceProvider provider = services.BuildServiceProvider();
        IScheduler scheduler = await provider.GetRequiredService<ISchedulerFactory>().GetScheduler();
        try
        {
            scheduler.ListenerManager.GetJobListeners().Should().ContainSingle();
            scheduler.ListenerManager.GetTriggerListeners().Should().ContainSingle();
            scheduler.ListenerManager.GetSchedulerListeners().Should().ContainSingle(
                "the check must let through every listener that does implement the interface");
        }
        finally
        {
            await scheduler.Shutdown();
        }
    }

    private sealed class SoundJobListener : IJobListener
    {
        public ValueTask JobToBeExecuted(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;

        public ValueTask JobWasExecuted(IJobExecutionContext context, JobExecutionException jobException, CancellationToken cancellationToken = default) => default;
    }

    private sealed class SilentJobListener : IJobListener;

    private sealed class HelpfulJobListener : IJobListener
    {
        public ValueTask JobToBeExecuted(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;

        public Task Flush() => Task.CompletedTask;
    }

    private sealed class InheritingJobListener : SoundJobListenerBase;

    private class SoundJobListenerBase : IJobListener
    {
        public ValueTask JobToBeExecuted(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
    }

    /// <summary>
    /// A 3.x job listener: it implemented the interface directly, and the members returned <see cref="Task" />.
    /// </summary>
    private sealed class ThreeXJobListener : IJobListener
    {
        public Task JobToBeExecuted(IJobExecutionContext context, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class InheritsAThreeXBase : ThreeXJobListenerBase;

    private class ThreeXJobListenerBase : IJobListener
    {
        public Task JobToBeExecuted(IJobExecutionContext context, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class SoundTriggerListener : ITriggerListener
    {
        public ValueTask TriggerFired(ITrigger trigger, IJobExecutionContext context, CancellationToken cancellationToken = default) => default;

        public ValueTask TriggerMisfired(ITrigger trigger, IScheduler scheduler, CancellationToken cancellationToken = default) => default;
    }

    /// <summary>
    /// A 4.0.0-alpha.2 trigger listener: <c>TriggerMisfired</c> led with the scheduler until the trigger
    /// took that place, which leaves a method with exactly the right parameters in the wrong order.
    /// </summary>
    private sealed class SchedulerFirstMisfireListener : ITriggerListener
    {
        public ValueTask TriggerMisfired(IScheduler scheduler, ITrigger trigger, CancellationToken cancellationToken = default) => default;
    }

    private sealed class ThreeXTriggerListener : ITriggerListener
    {
        public Task<bool> VetoJobExecution(ITrigger trigger, IJobExecutionContext context, CancellationToken cancellationToken = default) => Task.FromResult(false);
    }

    /// <summary>
    /// A 4.0.0-alpha.1 trigger listener: <c>TriggerMisfired</c> is the one trigger callback that gained a
    /// scheduler parameter, because a misfire is noticed rather than executed and so carries no context.
    /// </summary>
    private sealed class AlphaOneTriggerListener : ITriggerListener
    {
        public ValueTask TriggerMisfired(ITrigger trigger, CancellationToken cancellationToken = default) => default;
    }

    private sealed class SoundSchedulerListener : ISchedulerListener
    {
        public ValueTask SchedulerStarted(IScheduler scheduler, CancellationToken cancellationToken = default) => default;

        public ValueTask SchedulerError(IScheduler scheduler, SchedulerErrorContext errorContext, CancellationToken cancellationToken = default) => default;
    }

    private sealed class ExplicitSchedulerListener : ISchedulerListener
    {
        ValueTask ISchedulerListener.SchedulerStarted(IScheduler scheduler, CancellationToken cancellationToken) => default;
    }

    private sealed class ThreeXSchedulerListener : ISchedulerListener
    {
        public Task SchedulerStarted(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    /// <summary>
    /// A 4.0.0-alpha.1 scheduler listener: the error arrived as a message and an exception rather than as a
    /// <see cref="SchedulerErrorContext" />, and nothing said which scheduler had failed.
    /// </summary>
    private sealed class AlphaOneSchedulerErrorListener : ISchedulerListener
    {
        public ValueTask SchedulerError(string message, SchedulerException cause, CancellationToken cancellationToken = default) => default;
    }

    private sealed class AlphaOneLifecycleListener : ISchedulerListener
    {
        public ValueTask SchedulerShuttingDown(CancellationToken cancellationToken = default) => default;
    }
}
