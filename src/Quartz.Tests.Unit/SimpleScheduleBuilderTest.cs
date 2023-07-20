using FluentAssertions;

using NUnit.Framework;

namespace Quartz.Tests.Unit;

public class SimpleScheduleBuilderTest
{
    [Test]
    public void TriggerBuilderShouldHandleIgnoreMisfirePolicy()
    {
        var trigger1 = TriggerBuilder.Create()
            .WithSimpleSchedule(x => x
                .WithMisfireHandlingInstructionIgnoreMisfires()
            )
            .Build();

        var trigger2 = trigger1
            .GetTriggerBuilder()
            .Build();

        trigger1.MisfireInstruction.Should().Be(MisfireInstruction.IgnoreMisfirePolicy);
        trigger2.MisfireInstruction.Should().Be(MisfireInstruction.IgnoreMisfirePolicy);
    }
}