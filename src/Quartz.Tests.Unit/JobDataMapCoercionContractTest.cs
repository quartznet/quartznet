#nullable enable

using System.Globalization;

namespace Quartz.Tests.Unit;

/// <summary>
/// How a job data map and a scheduler context answer a read that does not go cleanly — a key that is
/// not there, a value of the wrong type, a number written as a string.
/// </summary>
/// <remarks>
/// <para>
/// Each case here is one bullet of the migration guide's "calls that still compile and now do something
/// different" list, which is the set a 3.x upgrader is told to check before the first run. Every one of
/// them is a silent change: the call compiles, returns, and means something else than it did.
/// </para>
/// <para>
/// The accessors are extension members declared for <see cref="JobDataMap" /> and for
/// <see cref="SchedulerContext" /> separately, so what is true of one is only true of the other by
/// construction. Every case therefore runs against both, through the small adapter below — the two
/// declarations are one-line bridges over a shared coercion core, and this is what says they still are.
/// </para>
/// </remarks>
public sealed class JobDataMapCoercionContractTest
{
    public static IEnumerable<TestCaseData> Receivers()
    {
        yield return new TestCaseData(new Func<Receiver>(static () => new JobDataMapReceiver()))
            .SetArgDisplayNames(nameof(JobDataMap));
        yield return new TestCaseData(new Func<Receiver>(static () => new SchedulerContextReceiver()))
            .SetArgDisplayNames(nameof(SchedulerContext));
    }

    [TestCaseSource(nameof(Receivers))]
    public void TheIndexer_ThrowsForAMissingKey(Func<Receiver> build)
    {
        Receiver receiver = build();

        Action read = () => receiver.Read("absent");

        read.Should().Throw<KeyNotFoundException>(
            "the indexer goes straight to the backing dictionary, where 3.x's DirtyFlagMap answered null — "
            + "so the 'read an optional entry' idiom has to become TryGetValue");
    }

    [TestCaseSource(nameof(Receivers))]
    public void GetString_IsNullForAMissingKeyAndForAValueThatIsNotAString(Func<Receiver> build)
    {
        Receiver receiver = build();
        receiver.Put("count", 42);

        receiver.GetString("absent").Should().BeNull(
            "3.x threw KeyNotFoundException here; 4.x reports the absence as a null");
        receiver.GetString("count").Should().BeNull(
            "3.x threw InvalidCastException here, so a mistyped key and a wrongly typed value are now "
            + "indistinguishable from 'no value' — which is the whole hazard the guide names");

        receiver.TryGetString("count", out string? value).Should().BeFalse(
            "the Try form is what still tells the two apart, so it has to refuse what Get answers null for");
        value.Should().BeNull();
    }

    [TestCaseSource(nameof(Receivers))]
    public void ATypedAccessor_ThrowsInvalidCastForAMissingKey(Func<Receiver> build)
    {
        Receiver receiver = build();

        Action readInt = () => receiver.GetInt("absent");
        Action readBoolean = () => receiver.GetBoolean("absent");
        Action readDouble = () => receiver.GetDouble("absent");

        readInt.Should().Throw<InvalidCastException>(
            "an absent key and an unreadable value take the same exit on 4.x, so a catch (KeyNotFoundException) "
            + "written against 3.x stops catching");
        readBoolean.Should().Throw<InvalidCastException>();
        readDouble.Should().Throw<InvalidCastException>();
    }

    [TestCaseSource(nameof(Receivers))]
    public void GetBoolean_ReadsAnythingThatIsNotTrueAsFalse(Func<Receiver> build)
    {
        Receiver receiver = build();
        receiver.Put("one", "1");
        receiver.Put("yes", "yes");
        receiver.Put("upper", "TRUE");

        receiver.GetBoolean("one").Should().BeFalse(
            "3.x used Convert.ToBoolean, which threw for \"1\"; 4.x compares against \"true\" and succeeds, "
            + "so a flag stored as \"1\" used to fail loudly and now quietly reads as off");
        receiver.GetBoolean("yes").Should().BeFalse();
        receiver.GetBoolean("upper").Should().BeTrue("the comparison against \"true\" ignores case");

        receiver.TryGetBoolean("one", out bool value).Should().BeTrue(
            "the Try form reports success for it too, so it is no help in telling a real \"false\" from a "
            + "value this map does not understand");
        value.Should().BeFalse();
    }

    [TestCaseSource(nameof(Receivers))]
    public void ANumberStoredAsAString_ReadsWithTheInvariantCulture(Func<Receiver> build)
    {
        CultureInfo original = CultureInfo.CurrentCulture;
        try
        {
            // de-DE writes a decimal comma, so on 3.x - which parsed with the current culture - "3.14"
            // read as 314 here. A job's numbers must not depend on the culture of the machine that
            // happens to run it.
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");

            Receiver receiver = build();
            receiver.Put("pi", "3.14");
            receiver.Put("count", "1234");

            receiver.GetDouble("pi").Should().Be(3.14,
                "the stored string is parsed with the invariant culture, whatever the ambient one is");
            receiver.GetFloat("pi").Should().Be(3.14f);
            receiver.GetInt("count").Should().Be(1234);
            receiver.GetLong("count").Should().Be(1234L);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    /// <summary>
    /// The other half of the invariant-culture change: a number a comma-decimal culture wrote is
    /// rejected by every numeric accessor, never read as a different number.
    /// </summary>
    /// <remarks>
    /// The invariant parser's default styles allow a group separator, which would read the comma in
    /// "3,14" as one and answer 314 — a hundredfold of the value, silently. The accessors therefore
    /// parse with styles that allow no group separator at all, so a string a comma-decimal culture
    /// wrote is unreadable as every numeric type alike, and an upgrade that carried such data fails
    /// loudly rather than in one place loudly and in the other not at all.
    /// </remarks>
    [TestCaseSource(nameof(Receivers))]
    public void ACommaDecimalNumberIsRejectedByEveryNumericAccessor(Func<Receiver> build)
    {
        Receiver receiver = build();
        receiver.Put("pi", "3,14");

        Action readDouble = () => receiver.GetDouble("pi");
        readDouble.Should().Throw<InvalidCastException>(
            "no numeric accessor allows a group separator, so the comma a de-DE machine wrote as a "
            + "decimal point makes the string unreadable rather than a hundredfold of itself");
        Action readFloat = () => receiver.GetFloat("pi");
        readFloat.Should().Throw<InvalidCastException>();

        Action readInt = () => receiver.GetInt("pi");
        readInt.Should().Throw<InvalidCastException>(
            "the integer styles allow no group separator, so the same string is simply unreadable");
    }

    /// <summary>
    /// The accessors under test, in the one shape both receivers can be asked in.
    /// </summary>
    public abstract class Receiver
    {
        public abstract void Put(string key, object? value);

        public abstract object? Read(string key);

        public abstract string? GetString(string key);

        public abstract bool TryGetString(string key, out string? value);

        public abstract int GetInt(string key);

        public abstract long GetLong(string key);

        public abstract double GetDouble(string key);

        public abstract float GetFloat(string key);

        public abstract bool GetBoolean(string key);

        public abstract bool TryGetBoolean(string key, out bool value);
    }

    private sealed class JobDataMapReceiver : Receiver
    {
        private readonly JobDataMap map = [];

        public override void Put(string key, object? value) => map[key] = value;

        public override object? Read(string key) => map[key];

        public override string? GetString(string key) => map.GetString(key);

        public override bool TryGetString(string key, out string? value) => map.TryGetString(key, out value);

        public override int GetInt(string key) => map.GetInt(key);

        public override long GetLong(string key) => map.GetLong(key);

        public override double GetDouble(string key) => map.GetDouble(key);

        public override float GetFloat(string key) => map.GetFloat(key);

        public override bool GetBoolean(string key) => map.GetBoolean(key);

        public override bool TryGetBoolean(string key, out bool value) => map.TryGetBoolean(key, out value);
    }

    private sealed class SchedulerContextReceiver : Receiver
    {
        private readonly SchedulerContext context = [];

        public override void Put(string key, object? value) => context[key] = value;

        public override object? Read(string key) => context[key];

        public override string? GetString(string key) => context.GetString(key);

        public override bool TryGetString(string key, out string? value) => context.TryGetString(key, out value);

        public override int GetInt(string key) => context.GetInt(key);

        public override long GetLong(string key) => context.GetLong(key);

        public override double GetDouble(string key) => context.GetDouble(key);

        public override float GetFloat(string key) => context.GetFloat(key);

        public override bool GetBoolean(string key) => context.GetBoolean(key);

        public override bool TryGetBoolean(string key, out bool value) => context.TryGetBoolean(key, out value);
    }
}
