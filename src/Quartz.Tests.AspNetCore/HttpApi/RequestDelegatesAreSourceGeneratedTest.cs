using System.Reflection;
using System.Text;

using Quartz.AspNetCore.HttpApi.Endpoints;

namespace Quartz.Tests.AspNetCore.HttpApi;

/// <summary>
/// Holds the one thing <c>EnableRequestDelegateGenerator</c> cannot say for itself: that the generator
/// actually intercepted every <c>MapGet</c>, <c>MapPost</c> and <c>MapDelete</c> the API is built from.
/// </summary>
/// <remarks>
/// <para>
/// The property is a request, not a result. An interceptor that declines to intercept leaves
/// <c>RequestDelegateFactory</c> to bind each handler's parameters and write its result by reflecting
/// over the delegate at run time, and every other test in this project passes either way — reflection is
/// perfectly correct in a process that has a JIT. What it is not is trim- or ahead-of-time-safe, and it
/// is where 114 of this package's 116 trim warnings came from before the generator was turned on.
/// </para>
/// <para>
/// Losing the property would be loud today, because nothing suppresses those warnings and
/// <c>TreatWarningsAsErrors</c> is on. This test is for the case that is not loud: the generator
/// declining one call site, or a future one it cannot express, which leaves the other 56 intercepted and
/// says nothing about the one it dropped.
/// </para>
/// </remarks>
public class RequestDelegatesAreSourceGeneratedTest
{
    /// <summary>
    /// The Map* calls in each endpoint file, which together are every route the API serves. A route added
    /// or removed changes the number here as well, and the point of writing it down is that the two have
    /// to be changed together.
    /// </summary>
    private static readonly Dictionary<string, int> mappedRoutes = new(StringComparer.Ordinal)
    {
        ["CalendarEndpoints.cs"] = 4,
        ["JobEndpoints.cs"] = 21,
        ["SchedulerEndpoints.cs"] = 13,
        ["TriggerEndpoints.cs"] = 21
    };

    [Test]
    public void EveryMapCallInTheEndpointsIsIntercepted()
    {
        List<CustomAttributeData> interceptions = Interceptions();

        interceptions.Should().HaveCount(mappedRoutes.Values.Sum(),
            "every Map* call in the endpoint classes has to be intercepted — one that is not is bound by "
            + "RequestDelegateFactory instead, which reflects over the handler and is neither trimmable "
            + "nor ahead-of-time-safe");
    }

    [Test]
    public void EveryEndpointClassHasItsRoutesIntercepted()
    {
        Dictionary<string, int> intercepted = Interceptions()
            .GroupBy(FileOf)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);

        intercepted.Should().BeEquivalentTo(mappedRoutes,
            "the generator only intercepts call sites in the project it runs in, so counting them per "
            + "file is what tells a whole endpoint class going unintercepted apart from the total "
            + "happening to add up");
    }

    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// The generated route builder, which the compiler emits into Quartz.AspNetCore itself as a
    /// file-local type — so its metadata name carries a hash of the generated file's own name and cannot
    /// be written down.
    /// </summary>
    private static Type GeneratedRouteBuilder()
    {
        Type[] candidates = typeof(CalendarEndpoints).Assembly
            .GetTypes()
            .Where(type => type.Namespace == "Microsoft.AspNetCore.Http.Generated")
            .Where(type => type.Name.EndsWith("GeneratedRouteBuilderExtensionsCore", StringComparison.Ordinal))
            .ToArray();

        candidates.Should().ContainSingle(
            "the request delegate generator emits exactly one route builder per compilation, and none at "
            + "all when EnableRequestDelegateGenerator is off in src/Quartz.AspNetCore/Quartz.AspNetCore.csproj");

        return candidates[0];
    }

    private static List<CustomAttributeData> Interceptions() => GeneratedRouteBuilder()
        .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
        .SelectMany(method => method.GetCustomAttributesData())
        .Where(attribute => attribute.AttributeType.Name.EndsWith("InterceptsLocationAttribute", StringComparison.Ordinal))
        .ToList();

    /// <summary>
    /// The source file an interception points at.
    /// </summary>
    /// <remarks>
    /// Roslyn's version 1 location is base64 of a 16-byte content checksum, a 4-byte position and the
    /// display file name. Nothing else decodes it, so a version this does not know about fails loudly
    /// rather than being read as if it were version 1.
    /// </remarks>
    private static string FileOf(CustomAttributeData interception)
    {
        interception.ConstructorArguments[0].Value.Should().Be(1,
            "this decodes Roslyn's version 1 interceptable location; a new version needs a new decoder here");

        byte[] data = Convert.FromBase64String((string) interception.ConstructorArguments[1].Value!);
        data.Length.Should().BeGreaterThan(20, "a version 1 location is a checksum, a position and a file name");

        return Encoding.UTF8.GetString(data, 20, data.Length - 20);
    }
}
