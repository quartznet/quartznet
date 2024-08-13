namespace Quartz.Tests.Unit;

[TestFixture]
public class DateBuilderTest
{
    [Test]
    public void NextGivenSecondDateShouldWork()
    {
        DateTimeOffset dto = new DateTimeOffset(new DateTime(2011, 11, 14, 21, 59, 0));
        var nextGivenSecondDate = DateBuilder.NextGivenSecondDate(dto, 0);
        Assert.Multiple(() =>
        {
            Assert.That(nextGivenSecondDate.Hour, Is.EqualTo(22));
            Assert.That(nextGivenSecondDate.Minute, Is.EqualTo(0));
            Assert.That(nextGivenSecondDate.Second, Is.EqualTo(0));
        });
    }

    [Test]
    public void NextGivenMinuteDateShouldWork()
    {
        DateTimeOffset dto = new DateTimeOffset(new DateTime(2011, 11, 14, 23, 59, 0));
        var nextGivenMinuteDate = DateBuilder.NextGivenMinuteDate(dto, 0);
        Assert.Multiple(() =>
        {
            Assert.That(nextGivenMinuteDate.Day, Is.EqualTo(15));
            Assert.That(nextGivenMinuteDate.Hour, Is.EqualTo(0));
            Assert.That(nextGivenMinuteDate.Minute, Is.EqualTo(0));
            Assert.That(nextGivenMinuteDate.Second, Is.EqualTo(0));
        });
    }
}