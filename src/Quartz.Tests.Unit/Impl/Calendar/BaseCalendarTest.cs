#if NET_35
using TimeZone = System.TimeZoneInfo;
#endif

using NUnit.Framework;

using Quartz.Impl.Calendar;

namespace Quartz.Tests.Unit.Impl.Calendar
{
    [TestFixture]
    public class BaseCalendarTest
    {
        [Test]
        public void TestClone() {
            BaseCalendar baseCalendar = new BaseCalendar();
            baseCalendar.Description = "My description";
#if NET_35
            baseCalendar.TimeZone = TimeZone.GetSystemTimeZones()[3];
#else
            baseCalendar.TimeZone = TimeZone.Utc;
#endif
            BaseCalendar clone = (BaseCalendar) baseCalendar.Clone();

            Assert.AreEqual(baseCalendar.Description, clone.Description);
            Assert.AreEqual(baseCalendar.GetBaseCalendar(), clone.GetBaseCalendar());
            Assert.AreEqual(baseCalendar.TimeZone, clone.TimeZone);
        }

    }
}