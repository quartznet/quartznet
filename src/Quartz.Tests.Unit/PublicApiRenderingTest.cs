using System.Reflection;

using PublicApiGenerator;

namespace Quartz.Tests.Unit;

/// <summary>
/// The guard on the guard. <see cref="PublicApiTest" /> would keep passing if the annotations
/// silently stopped being produced — every baseline would simply lose them together, and the diff
/// would look like a deliberate simplification. These tests name one member of each kind, so a
/// rendering that stops distinguishing them fails here with the reason written out.
/// </summary>
public class PublicApiRenderingTest
{
    private static readonly Lazy<string> rendering = new(static () =>
    {
        Assembly assembly = typeof(IScheduler).Assembly;
        return PublicApiRendering.Annotate(assembly.GeneratePublicApi(new ApiGeneratorOptions { TreatRecordsAsClasses = false }), assembly)
            .Replace("\r\n", "\n");
    });

    private static string Rendering => rendering.Value;

    [Test]
    public void MemberWithDefaultImplementationIsMarked()
    {
        Rendering.Should().Contain(
            "System.TimeProvider TimeProvider { get; } // default implementation",
            "beta.1 promises that a member added to a public interface arrives as a default implementation, which the baseline can only keep if it says which members are one");

        Rendering.Should().Contain(
            "ResetTriggersFromErrorState(Quartz.GroupMatcher<Quartz.TriggerKey> matcher, System.Threading.CancellationToken cancellationToken = default); // default implementation",
            "the marker has to distinguish two overloads of one name, since that is the case a whole-line comparison cannot");
    }

    [Test]
    public void AbstractMemberIsNotMarked()
    {
        Rendering.Should().Contain(
            "\n        string SchedulerName { get; }\n",
            "an unmarked member is the promise that an implementor must write it, so marking everything would be the same as marking nothing");
    }

    [Test]
    public void RecordCarriesItsKeyword()
    {
        Rendering.Should().Contain(
            "public sealed record SchedulerIdentity",
            "a record brings value equality, a copy constructor and `with`, none of which a class has");

        Rendering.Should().Contain(
            "public readonly record struct AddJobOptions",
            "a record struct is the case the generator itself cannot see, because it recognises a record by the <Clone>$ member only record classes have");

        Rendering.Should().Contain(
            "public sealed class AdoJobStoreOptions",
            "a class that is not a record must keep saying so, or the keyword means nothing");
    }

    [Test]
    public void ExplicitInterfaceImplementationIsListed()
    {
        Rendering.Should().Contain(
            "// explicit interface implementation: System.IComparable.CompareTo(object)",
            "an explicit implementation is private in metadata and so invisible to the generator, but it is callable through the interface and removing it is a break");
    }
}
