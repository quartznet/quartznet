using Quartz.Core;

namespace Quartz.Tests.Unit.Core;

[TestFixture]
public class QuartzRandomTest
{
    [Test]
    public void TestNextValidatesPositiveRange()
    {
        int result = QuartzRandom.Next(2, 6);

        result.Should().BeGreaterThanOrEqualTo(2).And.BeLessThan(6,
            "the upper bound is exclusive");
    }

    [Test]
    public void TestNextValidatesNegativeRange()
    {
        int result = QuartzRandom.Next(-6, -2);

        result.Should().BeGreaterThanOrEqualTo(-6).And.BeLessThan(-2,
            "the upper bound is exclusive");
    }

    [Test]
    public void TestNextValidatesPositiveNegativeRange()
    {
        int result = QuartzRandom.Next(-6, 6);

        result.Should().BeGreaterThanOrEqualTo(-6).And.BeLessThan(6,
            "the upper bound is exclusive");
    }

    [Test]
    public void TestNextDoesntIntegerOverflow()
    {
        int result = QuartzRandom.Next(-1, int.MaxValue);

        result.Should().BeGreaterThanOrEqualTo(-1).And.BeLessThan(int.MaxValue,
            "a range that spans almost the whole int domain must still be handled without overflow");
    }

    [Test]
    public void TestNextNeverReturnsTheExclusiveUpperBound()
    {
        // A single-value range is the deterministic way to observe exclusivity: the only value the
        // generator may return is the lower bound, no matter how many times it is asked.
        for (int i = 0; i < 1000; i++)
        {
            QuartzRandom.Next(0, 1).Should().Be(0, "maxValue is exclusive, so only minValue can be drawn");
        }
    }

    [Test]
    public void TestNextSingleArgumentIsBoundedByMaxValue()
    {
        for (int i = 0; i < 1000; i++)
        {
            QuartzRandom.Next(4).Should().BeInRange(0, 3, "the single-argument overload draws from [0, maxValue)");
        }
    }

    [Test]
    public void TestMinimumGreaterThanMaximum()
    {
        Action act = () => QuartzRandom.Next(3, 2);

        act.Should().Throw<ArgumentOutOfRangeException>("maxValue must be larger than minValue");
    }

    [Test]
    public void TestMinimumEqualToMaximum()
    {
        Action act = () => QuartzRandom.Next(2, 2);

        act.Should().Throw<ArgumentOutOfRangeException>("an empty range has no value to return");
    }
}
