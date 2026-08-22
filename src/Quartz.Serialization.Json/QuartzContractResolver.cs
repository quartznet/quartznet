using System;
using System.Collections.Generic;
using System.Reflection;

using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace Quartz;

/// <summary>
/// The contract rules Quartz's types need on top of the default resolver's.
/// </summary>
/// <remarks>
/// <para>
/// <b>A property typed as a read-only collection is replaced, not populated.</b> The default is to
/// call the getter and add the payload's items to whatever comes back, which is wrong twice over for
/// a property whose type says its contents cannot be assigned through it: the values it was holding
/// survive the read, and a getter that hands out a shared or lazily defaulted instance has that
/// instance mutated. <see cref="IDailyTimeIntervalTrigger.DaysOfWeek" /> is the case that showed it -
/// its getter defaults to all seven days, so a trigger stored for Monday and Wednesday came back
/// firing every day.
/// </para>
/// <para>
/// This is a resolver rather than a <see cref="JsonConverter" /> on purpose: a converter registered
/// on the serializer is not consulted for a value whose type came from a <c>$type</c> property - a
/// trigger stored as a blob, say - and the rule here applies on every path.
/// </para>
/// </remarks>
internal sealed class QuartzContractResolver : DefaultContractResolver
{
    public QuartzContractResolver()
    {
        IgnoreSerializableInterface = true;
    }

    protected override JsonProperty CreateProperty(MemberInfo member, MemberSerialization memberSerialization)
    {
        JsonProperty property = base.CreateProperty(member, memberSerialization);

        if (property.Writable && IsReadOnlyCollection(property.PropertyType))
        {
            property.ObjectCreationHandling = ObjectCreationHandling.Replace;
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
               || definition == typeof(IReadOnlyDictionary<,>);
    }
}
