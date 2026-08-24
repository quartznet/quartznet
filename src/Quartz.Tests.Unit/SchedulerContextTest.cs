namespace Quartz.Tests.Unit;

/// <summary>
/// The typed read accessors are declared for both <see cref="JobDataMap" /> and
/// <see cref="SchedulerContext" />, so what is pinned for one is pinned for the other.
/// </summary>
public class SchedulerContextTest
{
    [Test]
    public void Get_SaysWhichWayItFailed()
    {
        JobKey stored = new JobKey("job");
        SchedulerContext context = new SchedulerContext
        {
            ["key"] = stored,
            ["text"] = "42"
        };

        context.Get<JobKey>("key").Should().BeSameAs(stored);

        Action missing = () => context.Get<JobKey>("nope");
        missing.Should().Throw<KeyNotFoundException>().WithMessage("*nope*", "the message has to name the key");

        Action wrongType = () => context.Get<int>("text");
        wrongType.Should().Throw<InvalidCastException>()
            .WithMessage("*text*", "the message has to name the key")
            .And.Message.Should().Contain("System.String").And.Contain("System.Int32",
                "and both the stored type and the requested one, since neither is obvious from the call site");
    }

    [Test]
    public void GetValueOrDefault_FallsBackForAMissingOrMistypedEntry()
    {
        SchedulerContext context = new SchedulerContext
        {
            ["count"] = 7,
            ["text"] = "42"
        };

        int count = context.GetValueOrDefault("count", -1);
        count.Should().Be(7, "the overload taking a typed default must win over the dictionary extension, which would return object?");

        context.GetValueOrDefault("nope", -1).Should().Be(-1);
        context.GetValueOrDefault("text", -1).Should().Be(-1, "GetValueOrDefault<T> never parses, matching TryGet<T>");
    }

    [Test]
    public void Get_ReadsAValueTryGetWouldHaveFound()
    {
        SchedulerContext context = new SchedulerContext { ["environment"] = "staging" };

        context.TryGet("environment", out string read).Should().BeTrue();
        context.Get<string>("environment").Should().Be(read, "the two share one type test");
    }
}
