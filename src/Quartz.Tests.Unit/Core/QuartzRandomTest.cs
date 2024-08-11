using Quartz.Core;

namespace Quartz.Tests.Unit.Core;

[TestFixture]
public class QuartzRandomTest
{
    [Test]
    public void TestNextValidatesPositiveRange()
    {
        var rand = new QuartzRandom();
        var result = rand.Next(2, 6);

        Assert.That(result, Is.GreaterThanOrEqualTo(2));
        Assert.That(result, Is.LessThanOrEqualTo(6));
    }

    [Test]
    public void TestNextValidatesNegativeRange()
    {
        var rand = new QuartzRandom();
        var result = rand.Next(-6, -2);

        Assert.That(result, Is.GreaterThanOrEqualTo(-6));
        Assert.That(result, Is.LessThanOrEqualTo(-2));
    }

    [Test]
    public void TestNextValidatesPositiveNegativeRange()
    {
        var rand = new QuartzRandom();
        var result = rand.Next(-6, 6);

        Assert.That(result, Is.GreaterThanOrEqualTo(-6));
        Assert.That(result, Is.LessThanOrEqualTo(6));
    }

    [Test]
    public void TestNextDoesntIntegerOverflow()
    {
        var rand = new QuartzRandom();
        var result = rand.Next(-1, int.MaxValue);

        Assert.That(result, Is.GreaterThanOrEqualTo(-1));
        Assert.That(result, Is.LessThanOrEqualTo(int.MaxValue));
    }

    [Test]
    public void TestMinimumGreaterThanMaximum()
    {
        var rand = new QuartzRandom();

        Assert.Throws<ArgumentOutOfRangeException>(() => rand.Next(3, 2));
    }
}