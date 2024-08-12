using FluentAssertions;

namespace Quartz.Tests.Unit;

public class CronFieldTest
{
    [Test]
    public void BasicOperationsShouldWork()
    {
        var field = new CronField();
        field.Should().BeEmpty();

        field.Add(1);
        field.Should().ContainSingle();

        field.Should().Contain(1);
        field.Should().NotContain(2);

        field.Add(2);
        field.Should().HaveCount(2);
        field.Should().Contain(1);
        field.Should().Contain(2);

        field.Clear();
        field.Should().BeEmpty();
        field.Should().NotContain(1);
        field.Should().NotContain(2);
    }

    [Test]
    public void ShouldSupportAllSpec()
    {
        var field = new CronField();
        field.Add(CronExpressionConstants.AllSpec);
        field.Contains(1).Should().BeTrue();
        field.Contains(2023).Should().BeTrue();
        field.Contains(CronExpressionConstants.AllSpec).Should().BeTrue();
        field.Contains(CronExpressionConstants.NoSpec).Should().BeFalse();

        field.Clear();

        field.Add(1);
        field.Add(CronExpressionConstants.AllSpec);
        field.Contains(1).Should().BeTrue();
        field.Contains(2023).Should().BeTrue();
        field.Contains(CronExpressionConstants.AllSpec).Should().BeTrue();
        field.Contains(CronExpressionConstants.NoSpec).Should().BeFalse();
    }

    [Test]
    public void ShouldSupportNoSpec()
    {
        var field = new CronField();
        field.Add(CronExpressionConstants.NoSpec);
        field.Contains(CronExpressionConstants.NoSpec).Should().BeTrue();
        field.Contains(CronExpressionConstants.AllSpec).Should().BeFalse();

        field.Clear();

        field.Add(1);
        field.Add(CronExpressionConstants.AllSpec);
        field.Contains(1).Should().BeTrue();
        field.Contains(2023).Should().BeTrue();
        field.Contains(CronExpressionConstants.AllSpec).Should().BeTrue();
    }

    [Test]
    public void ShouldSupportFoo()
    {
        var field = new CronField();

        field.TryGetMinValueStartingFrom(0, out var min).Should().BeFalse();

        field.Add(0);
        field.TryGetMinValueStartingFrom(0, out min).Should().BeTrue();
        min.Should().Be(0);

        field.Clear();
        field.Add(15);
        field.TryGetMinValueStartingFrom(0, out min).Should().BeTrue();
        min.Should().Be(15);
        field.TryGetMinValueStartingFrom(30, out min).Should().BeFalse();

        field.Clear();
        field.Add(CronExpressionConstants.AllSpec);
        field.TryGetMinValueStartingFrom(0, out min).Should().BeTrue();
        min.Should().Be(0);
        field.TryGetMinValueStartingFrom(2023, out min).Should().BeTrue();
        min.Should().Be(2023);
    }
}