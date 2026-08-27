using System;
using System.Collections.Generic;
using System.Reflection;

using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

using Quartz.Converters;

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
/// <b>A property typed as a <see cref="TimeZoneInfo" /> travels as its id.</b> The default contract
/// writes a zone as its whole public surface, every member of which is read-only, so reading one back
/// populated the instance the getter already held and set nothing - and the owning trigger's getter
/// falls back to <see cref="TimeZoneInfo.Local" />, so a trigger stored under Tokyo fired on whichever
/// zone the reading machine was in. <see cref="TimeZoneInfoConverter" /> is attached here, per property,
/// rather than registered on the serializer, because the serializer's converter list is consulted for a
/// value's runtime type wherever it appears: a <see cref="TimeZoneInfo" /> sitting in a job data map
/// would be written as a bare string and read back as one, losing the <c>$type</c> that path carries.
/// Scoped to typed members, the change reaches every trigger's <c>TimeZone</c> and leaves
/// <see cref="object" />-typed slots exactly as they were.
/// </para>
/// <para>
/// This is a resolver rather than a <see cref="JsonConverter" /> on purpose: a converter registered
/// on the serializer is not consulted for a value whose type came from a <c>$type</c> property - a
/// trigger stored as a blob, say - and the rules here apply on every path.
/// </para>
/// </remarks>
internal sealed class QuartzContractResolver : DefaultContractResolver
{
    private static readonly TimeZoneInfoConverter timeZoneInfoConverter = new TimeZoneInfoConverter();

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

        if (property.PropertyType == typeof(TimeZoneInfo))
        {
            property.Converter = timeZoneInfoConverter;
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
