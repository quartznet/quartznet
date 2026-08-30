using System.Reflection;

using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace Quartz.Serialization.Newtonsoft;

/// <summary>
/// The contract rules Quartz's types need on top of the default resolver's.
/// </summary>
/// <remarks>
/// <para>
/// <b>A <see cref="Key{T}" /> is built through its <c>(name, group)</c> constructor.</b> Keys are
/// immutable and have two public constructors, and the default resolver only binds a parameterized
/// constructor when a type has exactly one — so without this, reading a key fails with "unable to find a
/// constructor to use". Naming the constructor here rather than converting the type keeps the JSON exactly
/// as it was when keys were built by populating properties, so a payload written by an earlier version still
/// reads and one written now is still readable by it.
/// </para>
/// <para>
/// <b>A property typed as a read-only collection is replaced, not populated.</b> The default is to call
/// the getter and add the payload's items to whatever comes back, which is wrong twice over for a
/// property whose type says its contents cannot be assigned through it: the values it was holding
/// survive the read, and a getter that hands out a shared or lazily defaulted instance has that
/// instance mutated. <see cref="IDailyTimeIntervalTrigger.DaysOfWeek" /> is the case that showed it —
/// its getter defaults to all seven days, so a trigger stored for Monday and Wednesday came back
/// firing every day.
/// </para>
/// <para>
/// <b>A property typed as a <see cref="TimeZoneInfo" /> travels as its id.</b> The default contract writes
/// a zone as its whole public surface, every member of which is read-only, so reading one back set nothing
/// and the owning trigger's getter fell through to <see cref="TimeZoneInfo.Local" /> — a trigger stored under
/// Tokyo fired on whichever zone the reading machine was in. <see cref="TimeZoneInfoConverter" /> is attached
/// here, per property, rather than registered on the serializer, because the serializer's converter list is
/// consulted for a value's runtime type wherever it appears: a <see cref="TimeZoneInfo" /> sitting in a job
/// data map would be written as a bare string and read back as one, losing the <c>$type</c> that path
/// carries. Scoped to typed members, the change reaches every trigger's <c>TimeZone</c> and leaves
/// <see cref="object" />-typed slots exactly as they were.
/// </para>
/// <para>
/// <b>A property typed as a <see cref="RetryPolicy" /> travels as its stored string.</b> Same shape of
/// problem, one step worse: a policy has no public constructor, so the default contract could write one
/// out and then had nothing to read it back with, failing the whole trigger.
/// <see cref="RetryPolicyConverter" /> is attached here for the same reason and in the same way.
/// </para>
/// <para>
/// This is a resolver rather than a <c>JsonConverter</c> on purpose: a converter registered on the serializer
/// is not consulted for a value whose type came from a <c>$type</c> property — a key held in a job data map,
/// say — and the rules here apply on every path.
/// </para>
/// </remarks>
internal sealed class QuartzContractResolver : DefaultContractResolver
{
    private static readonly TimeZoneInfoConverter timeZoneInfoConverter = new();
    private static readonly RetryPolicyConverter retryPolicyConverter = new();

    public QuartzContractResolver()
    {
        IgnoreSerializableInterface = true;
    }

    protected override JsonObjectContract CreateObjectContract(Type objectType)
    {
        JsonObjectContract contract = base.CreateObjectContract(objectType);

        ConstructorInfo? constructor = FindKeyConstructor(objectType);
        if (constructor is not null)
        {
            contract.OverrideCreator = arguments => constructor.Invoke(arguments);
            contract.CreatorParameters.Clear();
            foreach (JsonProperty parameter in CreateConstructorParameters(constructor, contract.Properties))
            {
                contract.CreatorParameters.Add(parameter);
            }
        }

        return contract;
    }

    protected override JsonProperty CreateProperty(MemberInfo member, MemberSerialization memberSerialization)
    {
        JsonProperty property = base.CreateProperty(member, memberSerialization);

        if (property.Writable && IsReadOnlyCollection(property.PropertyType))
        {
            property.ObjectCreationHandling = ObjectCreationHandling.Replace;
        }

        if (property.PropertyType == typeof(TimeZoneInfo))
        {
            property.Converter = timeZoneInfoConverter;
        }

        if (property.PropertyType == typeof(RetryPolicy))
        {
            property.Converter = retryPolicyConverter;
        }

        return property;
    }

    private static bool IsReadOnlyCollection(Type? propertyType)
    {
        if (propertyType is null || !propertyType.IsGenericType)
        {
            return false;
        }

        Type definition = propertyType.GetGenericTypeDefinition();
        return definition == typeof(IReadOnlyCollection<>)
            || definition == typeof(IReadOnlyList<>)
            || definition == typeof(IReadOnlySet<>)
            || definition == typeof(IReadOnlyDictionary<,>);
    }

    private static ConstructorInfo? FindKeyConstructor(Type objectType)
    {
        for (Type? type = objectType.BaseType; type is not null; type = type.BaseType)
        {
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Key<>))
            {
                return objectType.GetConstructor([typeof(string), typeof(string)]);
            }
        }

        return null;
    }
}
