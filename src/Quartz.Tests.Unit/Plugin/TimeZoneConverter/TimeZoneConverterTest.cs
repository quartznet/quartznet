using System;
using System.Threading.Tasks;

using NUnit.Framework;

using Quartz.Plugin.TimeZoneConverter;
using Quartz.Util;

namespace Quartz.Tests.Unit.Plugin.TimeZoneConverter
{
    public class TimeZoneConverterTest
    {
        [Test]
        public async Task ResolveIanaTimeZone()
        {
            Assert.Throws<TimeZoneNotFoundException>(() => TimeZoneUtil.FindTimeZoneById("Canada/Saskatchewan"));

            var plugin = new TimeZoneConverterPlugin();
            await plugin.Start();

            Assert.That(TimeZoneUtil.FindTimeZoneById("Canada/Saskatchewan"), Is.Not.Null);

            await plugin.Shutdown();
            Assert.Throws<TimeZoneNotFoundException>(() => TimeZoneUtil.FindTimeZoneById("Canada/Saskatchewan"));
        }
    }
}