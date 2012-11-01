using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using NUnit.Framework;
using Quartz.Impl.Triggers;

namespace Quartz.Tests.Unit.Impl.Triggers
{
    [TestFixture]
    public class CalendarIntervalTriggerImplTest
    {
        [Test]
        public void TestPreserveHourOfDayAcrossDaylightSavings()
        {
            TimeZoneInfo timeZone = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");

            CalendarIntervalTriggerImpl trigger = new CalendarIntervalTriggerImpl("trigger", IntervalUnit.Day, 1);
            trigger.PreserveHourOfDayAcrossDaylightSavings = true;
            trigger.TimeZone = timeZone;
            trigger.StartTimeUtc = new DateTimeOffset(2012, 11, 2, 12, 0, 0, TimeSpan.FromHours(-4));

            var fireTime = trigger.ComputeFirstFireTimeUtc(null);
            var expected = new DateTimeOffset(2012, 11, 2, 12, 0, 0, TimeSpan.FromHours(-4));
            Assert.AreEqual(expected, fireTime);

            fireTime = trigger.GetFireTimeAfter(fireTime);
            expected = new DateTimeOffset(2012, 11, 3, 12, 0, 0, TimeSpan.FromHours(-4));
            Assert.AreEqual(expected, fireTime);

            //this next day should be a new daylight savings change, notice the change in offset
            fireTime = trigger.GetFireTimeAfter(fireTime);
            expected = new DateTimeOffset(2012, 11, 4, 12, 0, 0, TimeSpan.FromHours(-5));
            Assert.AreEqual(expected, fireTime);

            fireTime = trigger.GetFireTimeAfter(fireTime);
            expected = new DateTimeOffset(2012, 11, 5, 12, 0, 0, TimeSpan.FromHours(-5));
            Assert.AreEqual(expected, fireTime);
        }
    }
}
