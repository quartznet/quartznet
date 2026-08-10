using AwesomeAssertions.Execution;

namespace Quartz.Tests.Unit;

public class SimpleScheduleBuilderTest
{
    [Test]
    public void TriggerBuilderShouldHandleIgnoreMisfirePolicy()
    {
        var trigger1 = TriggerBuilder.Create()
            .WithSimpleSchedule(x => x
                .WithMisfireInstruction(SimpleTriggerMisfireInstruction.IgnoreMisfires)
            )
            .Build();

        var trigger2 = trigger1
            .GetTriggerBuilder()
            .Build();
        using (new AssertionScope())
        {
            trigger1.MisfireInstructionCode.Should().Be(MisfireInstruction.IgnoreMisfirePolicy);
            trigger2.MisfireInstructionCode.Should().Be(MisfireInstruction.IgnoreMisfirePolicy);
        }
    }
}