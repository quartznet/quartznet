using Quartz.HttpApiContract;

namespace Quartz.Tests.Unit.HttpApi;

/// <summary>
/// What the schedule endpoint accepts beside a trigger: a conflict mode, where it means something and
/// says one thing.
/// </summary>
public class ScheduleJobRequestTest
{
    private static readonly ITrigger trigger = TriggerBuilder.Create().WithIdentity("t").ForJob("j").StartNow().Build();

    [Test]
    public void ValidateShouldAcceptATriggerWithAConflictMode()
    {
        new ScheduleJobRequest(trigger, Job: null) { OnConflict = TriggerConflict.KeepEarlier }.Validate().Should().BeEmpty();
    }

    [Test]
    public void ValidateShouldAcceptReplaceBesideTheReplaceMode()
    {
        new ScheduleJobRequest(trigger, Job: null, Replace: true) { OnConflict = TriggerConflict.Replace }.Validate().Should().BeEmpty(
            "HttpScheduler sends both, so that a host older than 4.3, which ignores the mode, still replaces");
    }

    [Test]
    public void ValidateShouldRejectReplaceBesideAnotherMode()
    {
        new ScheduleJobRequest(trigger, Job: null, Replace: true) { OnConflict = TriggerConflict.Keep }.Validate()
            .Should().ContainSingle().Which.Should().Contain("contradict");
    }

    [Test]
    public void ValidateShouldRejectAConflictModeBesideAJob()
    {
        JobDetailDto job = JobDetailDto.Create(JobBuilder.Create<NoOpJob>().WithIdentity("j").Build());

        new ScheduleJobRequest(trigger, job) { OnConflict = TriggerConflict.Keep }.Validate()
            .Should().ContainSingle().Which.Should().Contain("on its own",
                "keeping a trigger while its job is replaced beside it is not an operation the store has");
    }

    [Test]
    public void ValidateShouldRejectAModeThatIsNoneOfTheFour()
    {
        new ScheduleJobRequest(trigger, Job: null) { OnConflict = (TriggerConflict) 5 }.Validate()
            .Should().ContainSingle().Which.Should().Contain("must be Throw, Replace, Keep or KeepEarlier");
    }

    public sealed class NoOpJob : IJob
    {
        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
    }
}
