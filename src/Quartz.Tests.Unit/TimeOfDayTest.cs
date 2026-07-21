namespace Quartz.Tests.Unit;

public class TimeOfDayTest
{
    [Test]
    public void Constructor_WithHourMinuteAndSecond_SetsAllComponents()
    {
        var timeOfDay = new TimeOfDay(10, 20, 30);
        Assert.Multiple(() =>
        {
            Assert.That(timeOfDay.Hour, Is.EqualTo(10));
            Assert.That(timeOfDay.Minute, Is.EqualTo(20));
            Assert.That(timeOfDay.Second, Is.EqualTo(30));
        });
    }

    [Test]
    public void Constructor_WithHourAndMinute_DefaultsSecondToZero()
    {
        var timeOfDay = new TimeOfDay(10, 20);
        Assert.Multiple(() =>
        {
            Assert.That(timeOfDay.Hour, Is.EqualTo(10));
            Assert.That(timeOfDay.Minute, Is.EqualTo(20));
            Assert.That(timeOfDay.Second, Is.EqualTo(0));
        });
    }

    [Test]
    public void HourMinuteAndSecondOfDay_CreatesEquivalentInstance()
    {
        var timeOfDay = TimeOfDay.HourMinuteAndSecondOfDay(10, 20, 30);
        Assert.Multiple(() =>
        {
            Assert.That(timeOfDay.Hour, Is.EqualTo(10));
            Assert.That(timeOfDay.Minute, Is.EqualTo(20));
            Assert.That(timeOfDay.Second, Is.EqualTo(30));
        });
    }

    [Test]
    public void HourAndMinuteOfDay_CreatesInstanceWithZeroSecond()
    {
        var timeOfDay = TimeOfDay.HourAndMinuteOfDay(10, 20);
        Assert.Multiple(() =>
        {
            Assert.That(timeOfDay.Hour, Is.EqualTo(10));
            Assert.That(timeOfDay.Minute, Is.EqualTo(20));
            Assert.That(timeOfDay.Second, Is.EqualTo(0));
        });
    }

    [TestCase(0, 0, 0)]
    [TestCase(23, 59, 59)]
    public void Constructor_AcceptsBoundaryValues(int hour, int minute, int second)
    {
        var timeOfDay = new TimeOfDay(hour, minute, second);
        Assert.Multiple(() =>
        {
            Assert.That(timeOfDay.Hour, Is.EqualTo(hour));
            Assert.That(timeOfDay.Minute, Is.EqualTo(minute));
            Assert.That(timeOfDay.Second, Is.EqualTo(second));
        });
    }

    [TestCase(-1)]
    [TestCase(24)]
    public void Constructor_WithHourOutOfRange_ThrowsArgumentException(int hour)
    {
        var exception = Assert.Throws<ArgumentException>(() => new TimeOfDay(hour, 0, 0));
        Assert.That(exception.Message, Does.Contain("Hour"));
    }

    [TestCase(-1)]
    [TestCase(60)]
    public void Constructor_WithMinuteOutOfRange_ThrowsArgumentException(int minute)
    {
        var exception = Assert.Throws<ArgumentException>(() => new TimeOfDay(0, minute, 0));
        Assert.That(exception.Message, Does.Contain("Minute"));
    }

    [TestCase(-1)]
    [TestCase(60)]
    public void Constructor_WithSecondOutOfRange_ThrowsArgumentException(int second)
    {
        var exception = Assert.Throws<ArgumentException>(() => new TimeOfDay(0, 0, second));
        Assert.That(exception.Message, Does.Contain("Second"));
    }

    [Test]
    public void Constructor_WithHourAndMinute_ValidatesArguments()
    {
        var exception = Assert.Throws<ArgumentException>(() => new TimeOfDay(24, 0));
        Assert.That(exception.Message, Does.Contain("Hour"));
    }

    [TestCase(9, 0, 0, 10, 0, 0, true)]        // earlier hour
    [TestCase(10, 0, 0, 9, 0, 0, false)]       // later hour
    [TestCase(10, 15, 0, 10, 30, 0, true)]     // same hour, earlier minute
    [TestCase(10, 30, 0, 10, 15, 0, false)]    // same hour, later minute
    [TestCase(10, 30, 15, 10, 30, 45, true)]   // same hour and minute, earlier second
    [TestCase(10, 30, 45, 10, 30, 15, false)]  // same hour and minute, later second
    [TestCase(10, 20, 30, 10, 20, 30, false)]  // equal time is not "before"
    public void Before_ComparesChronologically(int hour, int minute, int second, int otherHour, int otherMinute, int otherSecond, bool expected)
    {
        var timeOfDay = new TimeOfDay(hour, minute, second);
        var other = new TimeOfDay(otherHour, otherMinute, otherSecond);
        Assert.That(timeOfDay.Before(other), Is.EqualTo(expected));
    }

    [Test]
    public void Equals_ReturnsTrue_ForSameComponents()
    {
        var timeOfDay = new TimeOfDay(10, 20, 30);
        var other = new TimeOfDay(10, 20, 30);
        Assert.That(timeOfDay.Equals(other), Is.True);
    }

    [TestCase(11, 20, 30)]
    [TestCase(10, 21, 30)]
    [TestCase(10, 20, 31)]
    public void Equals_ReturnsFalse_WhenAnyComponentDiffers(int hour, int minute, int second)
    {
        var timeOfDay = new TimeOfDay(10, 20, 30);
        var other = new TimeOfDay(hour, minute, second);
        Assert.That(timeOfDay.Equals(other), Is.False);
    }

    [Test]
    public void Equals_ReturnsFalse_ForNull()
    {
        var timeOfDay = new TimeOfDay(10, 20, 30);
        Assert.That(timeOfDay.Equals(null), Is.False);
    }

    [Test]
    public void Equals_ReturnsFalse_ForDifferentType()
    {
        var timeOfDay = new TimeOfDay(10, 20, 30);
        Assert.That(timeOfDay.Equals("10:20:30"), Is.False);
    }

    [Test]
    public void GetHashCode_IsEqual_ForEqualInstances()
    {
        var timeOfDay = new TimeOfDay(10, 20, 30);
        var other = new TimeOfDay(10, 20, 30);
        Assert.That(timeOfDay.GetHashCode(), Is.EqualTo(other.GetHashCode()));
    }

    [Test]
    public void GetTimeOfDayForDate_AppliesTimeAndResetsMilliseconds()
    {
        var offset = TimeSpan.FromHours(2);
        var date = new DateTimeOffset(2024, 1, 15, 8, 45, 12, 500, offset);

        var result = new TimeOfDay(10, 20, 30).GetTimeOfDayForDate(date);

        Assert.Multiple(() =>
        {
            Assert.That(result.Year, Is.EqualTo(2024));
            Assert.That(result.Month, Is.EqualTo(1));
            Assert.That(result.Day, Is.EqualTo(15));
            Assert.That(result.Hour, Is.EqualTo(10));
            Assert.That(result.Minute, Is.EqualTo(20));
            Assert.That(result.Second, Is.EqualTo(30));
            Assert.That(result.Millisecond, Is.EqualTo(0));
            Assert.That(result.Offset, Is.EqualTo(offset));
        });
    }

    [Test]
    public void GetTimeOfDayForDate_WithNullDate_ReturnsNull()
    {
        DateTimeOffset? nullDate = null;
        var result = new TimeOfDay(10, 20, 30).GetTimeOfDayForDate(nullDate);
        Assert.That(result, Is.Null);
    }

    [Test]
    public void ToString_ReturnsHourMinuteSecondFormat()
    {
        Assert.That(new TimeOfDay(1, 2, 3).ToString(), Is.EqualTo("TimeOfDay[1:2:3]"));
    }
}
