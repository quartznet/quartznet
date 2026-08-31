using FakeItEasy;

using Quartz.Core;
using Quartz.Impl.Triggers;

namespace Quartz.Tests.Unit.Core;

/// <summary>
/// The scheduler's own record of the firings it is running.
/// </summary>
/// <remarks>
/// It used to be registered as an <see cref="IJobListener" /> and told through the notification loop.
/// The scheduler calls it directly now, so <see cref="ExecutingJobsManager.FiringStarted" /> and
/// <see cref="ExecutingJobsManager.FiringEnded" /> are the whole of its surface.
/// </remarks>
[TestFixture]
public class ExecutingJobsManagerTest
{
    private ExecutingJobsManager manager;

    [SetUp]
    public void SetUp()
    {
        manager = new ExecutingJobsManager();
    }

    [Test]
    public void AFiringIsListedAndCountedOnceItHasStarted()
    {
        IJobExecutionContext context = Firing("fire-1");

        manager.FiringStarted(context);

        manager.GetExecutingJobs.Should().Equal([context]);
        manager.NumJobsCurrentlyExecuting.Should().Be(1);
        manager.NumJobsFired.Should().Be(1);
    }

    [Test]
    public void AFiringThatHasEndedIsNoLongerListedButIsStillCounted()
    {
        IJobExecutionContext context = Firing("fire-1");

        manager.FiringStarted(context);
        manager.FiringEnded(context);

        manager.GetExecutingJobs.Should().BeEmpty();
        manager.NumJobsCurrentlyExecuting.Should().Be(0);
        manager.NumJobsFired.Should().Be(1,
            "the count is of the firings this scheduler dispatched, and one that has finished still is one");
    }

    [Test]
    public void AFiringIsFoundByTheFireInstanceIdItWasRecordedUnder()
    {
        IJobExecutionContext context = Firing("fire-1");
        manager.FiringStarted(context);

        manager.TryGetExecutingJob("fire-1", out IJobExecutionContext found).Should().BeTrue();
        found.Should().BeSameAs(context,
            "interrupting one execution must not have to materialize every other one to find it");
    }

    [Test]
    public void AFireInstanceIdThatIsNotRunningIsNotFound()
    {
        manager.FiringStarted(Firing("fire-1"));

        manager.TryGetExecutingJob("fire-2", out IJobExecutionContext found).Should().BeFalse();
        found.Should().BeNull();
    }

    private static IJobExecutionContext Firing(string fireInstanceId)
    {
        SimpleTriggerImpl trigger = new()
        {
            Key = new TriggerKey("trigger", "group"),
            FireInstanceId = fireInstanceId
        };

        IJobExecutionContext context = A.Fake<IJobExecutionContext>();
        A.CallTo(() => context.Trigger).Returns(trigger);
        return context;
    }
}
