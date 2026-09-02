using FakeItEasy;

using Microsoft.Extensions.DependencyInjection;

using Quartz.Extensibility;
using Quartz.Impl;
using Quartz.Impl.Triggers;
using Quartz.Util;

namespace Quartz.Tests.Unit.Impl.JobType;

/// <summary>
/// A job type that arrived as a name is checked against <see cref="IJob" /> before anything constructs
/// it.
/// </summary>
/// <remarks>
/// Only the <see cref="Quartz.JobType" /> constructor that takes a <see cref="Type" /> and
/// configuration-time <c>AddJob(Type)</c> ever checked. The name path had none, so at fire time the
/// container was asked for the type — <c>GetService</c> hands back, and constructs, any registered
/// service of that type — and <c>ActivatorUtilities</c> was asked after it, with the cast to
/// <see cref="IJob" /> happening only once the object existed. The static constructor, the module
/// initializer and the instance constructor of any public type on the probing path therefore ran, with
/// the scheduler scope's services injected, before anything refused.
/// </remarks>
public class JobTypeConstructionGuardTest
{
    [SetUp]
    public void SetUp()
    {
        NotAJob.Constructed = 0;
    }

    [Test]
    public async Task AContainerRegisteredTypeThatIsNotAJobIsNeverConstructed()
    {
        ServiceCollection services = [];

        // Registered, so that GetService would hand back an instance — and construct one — for anyone who
        // asks the container for this type.
        services.AddTransient<NotAJob>();
        await using ServiceProvider serviceProvider = services.BuildServiceProvider();

        MicrosoftDependencyInjectionJobFactory factory = new(serviceProvider);

        Func<Task> act = async () => await factory.CreateJob(BundleForTypeNamed(typeof(NotAJob)), NewScheduler());

        await act.Should().ThrowAsync<SchedulerException>()
            .WithMessage($"*{typeof(NotAJob).FullName}*",
                "the refusal names the type, since a job type that is not a job is a configuration mistake "
                + "somebody has to find");
        NotAJob.Constructed.Should().Be(0,
            "nothing of a caller-named type runs before it has been established to be an IJob");
    }

    [Test]
    public void ActivationOfATypeThatIsNotAJobNeverRunsItsConstructor()
    {
        // No registration this time, so the factory falls through to ActivatorUtilities.
        ServiceCollection services = [];
        using ServiceProvider serviceProvider = services.BuildServiceProvider();
        JobActivatorCache cache = new();

        Action act = () => cache.CreateInstance(serviceProvider, typeof(NotAJob));

        act.Should().Throw<SchedulerException>().WithMessage($"*{typeof(NotAJob).FullName}*");
        NotAJob.Constructed.Should().Be(0);
    }

    [Test]
    public void TypeActivatorRefusesATypeThatIsNotWhatTheCallerAskedFor()
    {
        // The other construction path, which SimpleJobFactory and the configuration's type-name settings
        // both go through.
        Action act = () => TypeActivator.Instantiate<IJob>(typeof(NotAJob));

        act.Should().Throw<ArgumentException>().WithMessage($"*{typeof(NotAJob).FullName}*");
        NotAJob.Constructed.Should().Be(0);
    }

    private static TriggerFiredBundle BundleForTypeNamed(Type type)
    {
        // Through the name, because JobType's Type constructor is one of the two places that did check.
        IJobDetail jobDetail = JobBuilder.Create()
            .OfType((Quartz.JobType) type.AssemblyQualifiedName!)
            .WithIdentity(new JobKey("jobName", "jobGroup"))
            .Build();

        return new TriggerFiredBundle
        {
            JobDetail = jobDetail,
            Trigger = new SimpleTriggerImpl { Key = new TriggerKey("triggerName", "triggerGroup"), StartTimeUtc = TimeProvider.System.GetUtcNow() },
            Recovering = false,
            FireTimeUtc = DateTimeOffset.UtcNow,
            ScheduledFireTimeUtc = null,
            PreviousFireTimeUtc = null,
            NextFireTimeUtc = null
        };
    }

    private static IScheduler NewScheduler()
    {
        IScheduler scheduler = FakeItEasy.A.Fake<IScheduler>();
        FakeItEasy.A.CallTo(() => scheduler.Context).Returns(new SchedulerContext());
        return scheduler;
    }

    /// <summary>
    /// A public type that is not an <see cref="IJob" /> and records that it was built.
    /// </summary>
    public sealed class NotAJob
    {
        public static int Constructed;

        public NotAJob()
        {
            Interlocked.Increment(ref Constructed);
        }
    }
}
