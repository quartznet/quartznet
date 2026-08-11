using Quartz.Plugins.TimeZoneConverter;

namespace Quartz.Tests.Unit.Plugins.TimeZoneConverter;

public class TimeZoneConverterTest
{
    [Test]
    public async Task ResolveIanaTimeZone()
    {
        TimeZoneConverterPlugin plugin = new TimeZoneConverterPlugin();
        await plugin.Initialize("timeZoneConverter", scheduler: null!);
        try
        {
            TimeZoneUtil.FindTimeZoneById("Canada/Saskatchewan").Should().NotBeNull();
        }
        finally
        {
            await plugin.Shutdown();
        }
    }

    [Test]
    public async Task ShutdownDisposesOnlyItsOwnRegistration()
    {
        // On Windows "EET" is not a system id and is rescued by neither the BCL conversions nor
        // the alias table, while TimeZoneConverter's own data resolves it - so only a plugin
        // registration can produce it there. On platforms whose tzdata ships the zone the direct
        // lookup succeeds, and the after-shutdown half below self-skips.
        const string id = "EET";

        TimeZoneConverterPlugin schedulerAPlugin = new TimeZoneConverterPlugin();
        TimeZoneConverterPlugin schedulerBPlugin = new TimeZoneConverterPlugin();
        await schedulerAPlugin.Initialize("timeZoneConverter", scheduler: null!);
        await schedulerBPlugin.Initialize("timeZoneConverter", scheduler: null!);
        try
        {
            await schedulerAPlugin.Shutdown();

            TimeZoneUtil.FindTimeZoneById(id).Should().NotBeNull(
                "the second scheduler's registration must survive the first scheduler's shutdown");
        }
        finally
        {
            await schedulerBPlugin.Shutdown();

            // shutting down twice must be harmless
            await schedulerAPlugin.Shutdown();
        }

        if (!IsSystemResolvable(id))
        {
            Func<TimeZoneInfo> act = () => TimeZoneUtil.FindTimeZoneById(id);
            act.Should().Throw<TimeZoneNotFoundException>(
                "after every scheduler's plugin has shut down, no resolver registration may linger");
        }
    }

    private static bool IsSystemResolvable(string id)
    {
        try
        {
            TimeZoneInfo.FindSystemTimeZoneById(id);
            return true;
        }
        catch (TimeZoneNotFoundException)
        {
            return false;
        }
    }
}
