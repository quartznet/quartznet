using System;

using NUnit.Framework;

namespace Quartz.Tests.Unit
{
    [TestFixture]
    public class DateBuilderTest
    {
        [Test]
        public void NextGivenSecondDateShouldWork()
        {
            DateTimeOffset dto = new DateTimeOffset(new DateTime(2011, 11, 14, 21, 59, 0));
            var nextGivenSecondDate = DateBuilder.NextGivenSecondDate(dto, 0);
            Assert.AreEqual(22, nextGivenSecondDate.Hour);
            Assert.AreEqual(0, nextGivenSecondDate.Minute);
            Assert.AreEqual(0, nextGivenSecondDate.Second);
        }

        [Test]
        public void NextGivenMinuteDateShouldWork()
        {
            DateTimeOffset dto = new DateTimeOffset(new DateTime(2011, 11, 14, 23, 59, 0));
            var nextGivenMinuteDate = DateBuilder.NextGivenMinuteDate(dto, 0);
            Assert.AreEqual(15, nextGivenMinuteDate.Day);
            Assert.AreEqual(0, nextGivenMinuteDate.Hour);
            Assert.AreEqual(0, nextGivenMinuteDate.Minute);
            Assert.AreEqual(0, nextGivenMinuteDate.Second);
        }
    }
}