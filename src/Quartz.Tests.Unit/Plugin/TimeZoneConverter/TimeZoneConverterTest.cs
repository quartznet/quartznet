using Quartz.Plugin.TimeZoneConverter;
using Quartz.Util;

namespace Quartz.Tests.Unit.Plugin.TimeZoneConverter;

public class TimeZoneConverterTest
{
    [Test]
    public async Task ResolveIanaTimeZone()
    {
        try
        {
            var plugin = new TimeZoneConverterPlugin();
            await plugin.Initialize("", null);

            Assert.That(TimeZoneUtil.FindTimeZoneById("Canada/Saskatchewan"), Is.Not.Null);
        }
        finally
        {
            TimeZoneUtil.CustomResolver = _ => null;
        }
    }
}