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

    [Test]
    public void ValidateShouldRejectPausedBesideAConflictMode()
    {
        new ScheduleJobRequest(trigger, Job: null) { OnConflict = TriggerConflict.Keep, PauseReason = "hold" }.Validate()
            .Should().ContainSingle().Which.Should().Contain("cannot be stored paused",
                "ScheduleTrigger takes no options, so a pause beside a conflict mode would be dropped without a word");
        new ScheduleJobRequest(trigger, Job: null, Replace: true) { Paused = true }.Validate().Should().BeEmpty();
    }

    [Test]
    public void APausedBodyWithAReasonIsTheCallersWhenItNamesNoRequester()
    {
        ScheduleJobOptions options = new ScheduleJobRequest(trigger, Job: null) { PauseReason = "awaiting approval" }.AsOptions("ops@example.com");

        options.Paused.Should().BeTrue();
        options.PauseReason.Should().Be("awaiting approval");
        options.PauseRequestedBy.Should().Be("ops@example.com", "the requester is the authenticated caller, as on the pause routes");

        new ScheduleJobsRequest([], Replace: false) { PauseReason = "hold", PauseRequestedBy = "alice" }.AsOptions("ops@example.com")
            .PauseRequestedBy.Should().Be("alice", "a requester the body names wins");
    }

    [Test]
    public void APausedBodyWithNeitherTextIsTheReasonlessPauseEvenForAnAuthenticatedCaller()
    {
        ScheduleJobOptions options = new ScheduleJobRequest(trigger, Job: null) { Paused = true }.AsOptions("ops@example.com");

        options.Paused.Should().BeTrue();
        options.PauseReason.Should().BeNull();
        options.PauseRequestedBy.Should().BeNull(
            "filling the requester in would record a pause the caller made without a word, as a key-set pause body does not");

        new ScheduleJobRequest(trigger, Job: null).AsOptions("ops@example.com").Paused.Should().BeFalse(
            "a body without the members is the schedule it always was");
    }

    public sealed class NoOpJob : IJob
    {
        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
    }
}
