using Microsoft.Extensions.DependencyInjection;

using Quartz.Plugins.TimeZoneConverter;

namespace Quartz.Tests.Unit.Plugins.TimeZoneConverter;

/// <summary>
/// What <c>UseTimeZoneConverter</c> installs, now that it is a resolver registration rather than a
/// plugin.
/// </summary>
/// <remarks>
/// The registration is process-wide and never removed, so these tests deliberately assert only about
/// what is true afterwards — which is every assertion worth making about a registration that cannot be
/// taken back.
/// </remarks>
public class TimeZoneConverterTest
{
    [Test]
    public void ConfiguringASchedulerResolvesAnIdTheSystemDoesNotKnow()
    {
        // On Windows "EET" is not a system id and is rescued by neither the BCL conversions nor Quartz's
        // alias table, while TimeZoneConverter's own data resolves it - so only the registration can
        // produce it there. On a platform whose tzdata ships the zone the direct lookup succeeds, and
        // this says only that the registration did no harm.
        ServiceCollection services = new ServiceCollection();
        services.AddQuartz(q => q.UseTimeZoneConverter());

        TimeZones.FindById("EET").Should().NotBeNull(
            "the registration happens while the scheduler is being configured rather than when it is "
            + "built, which is the point: a trigger's time zone is resolved wherever the trigger is "
            + "built, and that can be before any scheduler exists");
    }

    [Test]
    public void RegisteringASecondTimeAddsNothing()
    {
        TimeZoneConverterResolver.Register();

        TimeZoneConverterResolver.Register().Should().BeFalse(
            "the resolver is process-wide, so a second scheduler asking for it would otherwise pile a "
            + "duplicate onto the list every failed lookup walks, with nothing ever removing either");
    }

    [Test]
    public void UseTimeZoneConverterRefusesANullBuilder()
    {
        Action act = () => TimeZonePluginConfigurationExtensions.UseTimeZoneConverter(builder: null!);

        act.Should().Throw<ArgumentNullException>();
    }
}
