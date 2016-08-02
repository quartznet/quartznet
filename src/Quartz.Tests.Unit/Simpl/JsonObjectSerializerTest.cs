using NUnit.Framework;

using Quartz.Impl.Calendar;
using Quartz.Simpl;

namespace Quartz.Tests.Unit.Simpl
{
    [TestFixture]
    public class JsonObjectSerializerTest
    {
        [Test]
        public void SerializeHolidayCalendar()
        {
            var serializer = new JsonObjectSerializer();
            var original = new HolidayCalendar();
            var bytes = serializer.Serialize(original);
            var deserialized = serializer.DeSerialize<ICalendar>(bytes);

            Assert.That(deserialized, Is.EqualTo(original));
        }
    }
}