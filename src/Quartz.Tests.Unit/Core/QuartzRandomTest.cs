using Quartz.Core;

namespace Quartz.Tests.Unit.Core;

[TestFixture]
public class QuartzRandomTest
{
    [Test]
    public void TestNextValidatesPositiveRange()
    {
        var result = QuartzRandom.Next(2, 6);

        Assert.That(result, Is.GreaterThanOrEqualTo(2));
        Assert.That(result, Is.LessThanOrEqualTo(6));
    }

    [Test]
    public void TestNextValidatesNegativeRange()
    {
        var result = QuartzRandom.Next(-6, -2);

        Assert.That(result, Is.GreaterThanOrEqualTo(-6));
        Assert.That(result, Is.LessThanOrEqualTo(-2));
    }

    [Test]
    public void TestNextValidatesPositiveNegativeRange()
    {
        var result = QuartzRandom.Next(-6, 6);

        Assert.That(result, Is.GreaterThanOrEqualTo(-6));
        Assert.That(result, Is.LessThanOrEqualTo(6));
    }

    [Test]
    public void TestNextDoesntIntegerOverflow()
    {
        var result = QuartzRandom.Next(-1, int.MaxValue);

        Assert.That(result, Is.GreaterThanOrEqualTo(-1));
        Assert.That(result, Is.LessThanOrEqualTo(int.MaxValue));
    }

    [Test]
    public void TestMinimumGreaterThanMaximum()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => QuartzRandom.Next(3, 2));
    }
}