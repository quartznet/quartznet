using System.Reflection;

using AwesomeAssertions.Execution;

using Quartz.HttpApiContract;

using OpenApiRequest = Quartz.AspNetCore.HttpApi.OpenApi.UpdateTriggerDetailsRequest;

namespace Quartz.Tests.AspNetCore.HttpApi;

/// <summary>
/// Holds the published OpenAPI schema of the trigger-details patch to the three things it has to agree
/// with: the body the wire contract reads, the update object it becomes, and the request the client
/// writes.
/// </summary>
/// <remarks>
/// The body has a converter of its own — absent and <c>null</c> mean different things, which no
/// generated reader can express — so the members a reader of the document sees are the ones written down
/// in <c>Quartz.AspNetCore.HttpApi.OpenApi.UpdateTriggerDetailsRequest</c>, and nothing compiles against
/// that interface. Left unchecked it would keep describing the older body the first time a field was
/// added, exactly as the trigger schema did before <see cref="OpenApiTriggerSchemaTest" /> existed.
/// </remarks>
public class OpenApiUpdateTriggerDetailsSchemaTest
{
    /// <summary>
    /// The wire members, which are the request record's properties minus the presence flags: those say
    /// whether a member is in the body at all, and are exactly what does not appear in it.
    /// </summary>
    private static string[] WireMembers() => typeof(UpdateTriggerDetailsRequest)
        .GetProperties(BindingFlags.Public | BindingFlags.Instance)
        .Select(static property => property.Name)
        .Where(static name => !name.StartsWith("Has", StringComparison.Ordinal))
        .Order(StringComparer.Ordinal)
        .ToArray();

    [Test]
    public void TheShadowInterfaceMatchesTheBodyTheApiReads()
    {
        string[] documented = typeof(OpenApiRequest).GetProperties()
            .Select(static property => property.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();

        using (new AssertionScope())
        {
            WireMembers().Except(documented, StringComparer.Ordinal).Should().BeEmpty(
                "a member the endpoint reads and the schema omits is a change a generated client cannot ask for — add it to Quartz.AspNetCore.HttpApi.OpenApi.UpdateTriggerDetailsRequest");

            documented.Except(WireMembers(), StringComparer.Ordinal).Should().BeEmpty(
                "a schema promising a member the endpoint ignores is the same bug in the other direction — remove it from Quartz.AspNetCore.HttpApi.OpenApi.UpdateTriggerDetailsRequest");
        }
    }

    /// <summary>
    /// Every property a <see cref="TriggerDetailsUpdate" /> carries reaches the wire.
    /// </summary>
    /// <remarks>
    /// The update object is where a new trigger setting is added, and it is a different assembly's file
    /// from the body that has to carry it. Without this, adding one would leave the HTTP API silently
    /// unable to set it — the endpoint would still compile, still answer, and still drop the field.
    /// </remarks>
    [Test]
    public void EveryUpdateFieldReachesTheWire()
    {
        string[] update = typeof(TriggerDetailsUpdate)
            .GetProperties(BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance)
            .Select(static property => property.Name)
            .Where(static name => name.StartsWith("Has", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .ToArray();

        string[] carried = typeof(UpdateTriggerDetailsRequest)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(static property => property.Name)
            .Where(static name => name.StartsWith("Has", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .ToArray();

        carried.Should().Equal(update,
            "the wire body mirrors TriggerDetailsUpdate field for field, so a setting added to the update "
            + "without a member on the body is one the HTTP API quietly cannot change");
    }
}
