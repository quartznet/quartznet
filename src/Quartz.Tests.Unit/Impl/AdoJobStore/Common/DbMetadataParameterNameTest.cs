using Quartz.Impl.AdoJobStore.Common;

namespace Quartz.Tests.Unit.Impl.AdoJobStore.Common;

/// <summary>
/// The prefixed spelling of a parameter name is worked out once per name, not once per parameter
/// bound.
/// </summary>
public class DbMetadataParameterNameTest
{
    private static DbMetadata Metadata(string prefix) => new() { ParameterNamePrefix = prefix, BindByName = true };

    [Test]
    public void PrefixesTheName()
    {
        Metadata("@").GetParameterName("schedulerName").Should().Be("@schedulerName");
        Metadata(":").GetParameterName("schedulerName").Should().Be(":schedulerName");
    }

    [Test]
    public void HandsBackTheSameStringForTheSameName()
    {
        DbMetadata metadata = Metadata("@");

        metadata.GetParameterName("triggerName").Should().BeSameAs(metadata.GetParameterName("triggerName"),
            "a name asked for twice was spelled once; the second call has nothing to build");
    }

    [Test]
    public void ADescriptionWithNoPrefixHandsBackWhatItWasGiven()
    {
        // Nothing to prepend, so nothing to build and nothing to remember.
        string name = "schedulerName";

        Metadata(null).GetParameterName(name).Should().BeSameAs(name);
        Metadata("").GetParameterName(name).Should().BeSameAs(name);
    }

    [Test]
    public void RememberingOneNameDoesNotMakeTwoDescriptionsDiffer()
    {
        DbMetadata used = Metadata("@");
        DbMetadata fresh = Metadata("@");

        used.GetParameterName("schedulerName");

        used.Should().Be(fresh,
            "the cache hangs off the description rather than sitting in a field on it, because a record compares every field it has");
    }

    [Test]
    public void BindingTheSameNamesAgainAllocatesNothing()
    {
        // The seven parameters a trigger acquisition binds, which is the smallest of the hot paths.
        string[] names = ["schedulerName", "state", "noLaterThan", "noEarlierThan", "instanceId", "autoPinSentinel", "liveNodeCutoff"];
        DbMetadata metadata = Metadata("@");

        // Warm the cache, then measure the round every acquisition after the first one pays.
        Bind(metadata, names);

        long before = GC.GetAllocatedBytesForCurrentThread();
        Bind(metadata, names);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        allocated.Should().Be(0,
            "binding a parameter used to concatenate its name with the driver's prefix, which put an allocation under every parameter of every statement the store issues");
    }

    private static void Bind(DbMetadata metadata, string[] names)
    {
        foreach (string name in names)
        {
            _ = metadata.GetParameterName(name);
        }
    }
}
