using System.Reflection;

using AwesomeAssertions.Execution;

using Quartz.AspNetCore.HttpApi.Endpoints;

// NUnit has a [Description] of its own, and this file is about the other one.
using DescriptionAttribute = System.ComponentModel.DescriptionAttribute;

namespace Quartz.Tests.AspNetCore.HttpApi;

/// <summary>
/// Every listing endpoint's <c>take</c> says in the generated OpenAPI document what its type cannot.
/// </summary>
/// <remarks>
/// <para>
/// <c>take</c> binds as a <see langword="string" /> because <c>?take=all</c> is one of the two things it
/// accepts, so the published document says <c>string</c> where a reader expects <c>integer</c>. Quartz
/// generates no document — the host's generator does, and both NSwag and
/// <c>Microsoft.AspNetCore.OpenApi</c> read <see cref="DescriptionAttribute" /> — so a description is the
/// one thing that reaches every reader without a schema filter written per generator
/// (<see href="https://github.com/quartznet/quartznet/issues/3682">#3682</see>).
/// </para>
/// <para>
/// A description is easy to add to five of six parameters, and the sixth is the one a caller happens to
/// be reading. So the check is over the parameters rather than over a list of endpoints: a listing added
/// later is caught by the same test, without anyone remembering to add it here.
/// </para>
/// </remarks>
public class TakeParameterDescriptionTest
{
    /// <summary>
    /// Written out rather than read from the constant the endpoints use. The text is what a caller sees,
    /// so changing it should be a decision rather than a rename that no test noticed.
    /// </summary>
    private const string ExpectedDescription = """A page size, or "all" for everything up to MaxPageSize""";

    private static readonly Type[] endpointClasses =
    [
        typeof(CalendarEndpoints),
        typeof(JobEndpoints),
        typeof(TriggerEndpoints)
    ];

    [Test]
    public void EveryTakeParameterCarriesTheDescription()
    {
        ParameterInfo[] takeParameters = TakeParameters();

        takeParameters.Should().HaveCount(6,
            "there are six listing endpoints — jobs, job groups, fire instances, triggers, trigger groups "
            + "and calendars — and a seventh appearing here means a listing was added whose take needs "
            + "the same description");

        using (new AssertionScope())
        {
            foreach (ParameterInfo take in takeParameters)
            {
                take.GetCustomAttribute<DescriptionAttribute>()?.Description.Should().Be(
                    ExpectedDescription,
                    $"{take.Member.DeclaringType!.Name}.{take.Member.Name} publishes a string parameter, and "
                    + "the description is the only place the document can say that \"all\" is one of the "
                    + "values it takes");
            }
        }
    }

    /// <summary>
    /// The parameter is a string on every one of them. If one were ever bound as an <see cref="int" />,
    /// that endpoint would silently stop accepting <c>?take=all</c> while its description kept promising
    /// it — and its callers are the 3.x-compatible listings, which ask for exactly that.
    /// </summary>
    [Test]
    public void EveryTakeParameterIsAStringSoThatAllIsAcceptable()
    {
        using (new AssertionScope())
        {
            foreach (ParameterInfo take in TakeParameters())
            {
                take.ParameterType.Should().Be<string>(
                    $"{take.Member.DeclaringType!.Name}.{take.Member.Name} accepts a number or the "
                    + "\"all\" sentinel, and only a string accepts both");
            }
        }
    }

    private static ParameterInfo[] TakeParameters() => endpointClasses
        .SelectMany(type => type.GetMethods(BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static))
        .SelectMany(method => method.GetParameters())
        .Where(parameter => parameter.Name == "take")
        .ToArray();
}
