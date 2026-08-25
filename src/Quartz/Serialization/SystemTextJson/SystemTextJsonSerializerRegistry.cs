using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

using Quartz.Impl.Calendar;
using Quartz.Impl.Triggers;
using Quartz.Serialization.SystemTextJson.Calendars;
using Quartz.Serialization.SystemTextJson.Triggers;
using Quartz.Util;

namespace Quartz.Serialization.SystemTextJson;

/// <summary>
/// The trigger and calendar serializers a <see cref="Quartz.Impl.SystemTextJsonObjectSerializer"/>
/// knows about.
/// </summary>
/// <remarks>
/// <para>
/// A new instance already knows every trigger and calendar type Quartz ships with, so registering a
/// custom one adds to that set rather than replacing it.
/// </para>
/// <para>
/// These registrations used to live in process-global dictionaries inside the converters, which meant
/// two schedulers in one process could not serialize different custom types — whichever registered last
/// won, silently. Owning them per instance is what makes the choice belong to the scheduler that made it.
/// Registrations are expected to be complete before the owning serializer is initialized; the maps are
/// not synchronized for concurrent writes, exactly as the statics they replace were not.
/// </para>
/// <para>
/// A registry is also where the serializer's <see cref="JsonSerializerOptions" /> get the metadata a
/// trimmed or native-AOT application has instead of reflection. It answers for every trigger and
/// calendar type registered with it, built-in or custom, because
/// <see cref="AddTriggerSerializer{TTrigger}" /> and <see cref="AddCalendarSerializer{TCalendar}" />
/// know the type statically; anything else the application puts in a <see cref="JobDataMap" /> is what
/// <see cref="AddTypeInfoResolver" /> is for.
/// </para>
/// </remarks>
public sealed class SystemTextJsonSerializerRegistry
{
    private readonly SerializerMap<ITriggerSerializer> triggerSerializers = new(StringComparer.OrdinalIgnoreCase);
    private readonly SerializerMap<ICalendarSerializer> calendarSerializers = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<Type, Func<JsonSerializerOptions, JsonConverter, JsonTypeInfo>> registeredTypeInfos = [];
    private readonly List<IJsonTypeInfoResolver> resolvers;

    /// <summary>
    /// Creates a registry holding the serializers for the built-in trigger and calendar types.
    /// </summary>
    public SystemTextJsonSerializerRegistry()
    {
        resolvers = [new RegisteredTypeResolver(registeredTypeInfos)];

        AddTriggerSerializer<CalendarIntervalTriggerImpl>(new CalendarIntervalTriggerSerializer());
        AddTriggerSerializer<CronTriggerImpl>(new CronTriggerSerializer());
        AddTriggerSerializer<DailyTimeIntervalTriggerImpl>(new DailyTimeIntervalTriggerSerializer());
        AddTriggerSerializer<SimpleTriggerImpl>(new SimpleTriggerSerializer());
        AddTriggerSerializer<RecurrenceTriggerImpl>(new RecurrenceTriggerSerializer());

        AddCalendarSerializer<BaseCalendar>(new BaseCalendarSerializer());
        AddCalendarSerializer<AnnualCalendar>(new AnnualCalendarSerializer());
        AddCalendarSerializer<CronCalendar>(new CronCalendarSerializer());
        AddCalendarSerializer<DailyCalendar>(new DailyCalendarSerializer());
        AddCalendarSerializer<HolidayCalendar>(new HolidayCalendarSerializer());
        AddCalendarSerializer<MonthlyCalendar>(new MonthlyCalendarSerializer());
        AddCalendarSerializer<WeeklyCalendar>(new WeeklyCalendarSerializer());
    }

    /// <summary>
    /// Adds a serializer for a custom trigger type.
    /// </summary>
    public SystemTextJsonSerializerRegistry AddTriggerSerializer<TTrigger>(ITriggerSerializer serializer) where TTrigger : ITrigger
    {
        ArgumentNullException.ThrowIfNull(serializer);

        // Found by its JSON discriminator, and also by its type name.
        triggerSerializers.Add(serializer, serializer.TriggerTypeName, typeof(TTrigger).AssemblyQualifiedNameWithoutVersion());
        RegisterTypeInfo<TTrigger>();
        return this;
    }

    /// <summary>
    /// Adds a serializer for a custom calendar type.
    /// </summary>
    /// <remarks>
    /// The serializer is the typed <see cref="CalendarSerializer{TCalendar}" /> for this very calendar,
    /// so a mismatched pairing is a compile error rather than an <see cref="InvalidCastException" /> on
    /// the first calendar that round-trips. <typeparamref name="TCalendar" /> is inferred from the
    /// argument, so the type argument can be left off.
    /// </remarks>
    public SystemTextJsonSerializerRegistry AddCalendarSerializer<TCalendar>(CalendarSerializer<TCalendar> serializer) where TCalendar : ICalendar
    {
        ArgumentNullException.ThrowIfNull(serializer);

        calendarSerializers.Add(serializer, typeof(TCalendar).AssemblyQualifiedNameWithoutVersion(), serializer.CalendarTypeName);
        RegisterTypeInfo<TCalendar>();
        return this;
    }

    /// <summary>
    /// Adds the metadata for the application's own job-data value types, needed only where
    /// reflection-based serialization is off.
    /// </summary>
    /// <param name="resolver">
    /// A <see cref="JsonSerializerContext" /> the application generated — a partial class deriving from
    /// it with a <c>[JsonSerializable]</c> attribute per type — or any other
    /// <see cref="IJsonTypeInfoResolver" />. Its <c>Default</c> instance is what to hand in.
    /// </param>
    /// <remarks>
    /// <para>
    /// A <see cref="JobDataMap" /> holds whatever the application put in it, so the store format has an
    /// open half no contract of Quartz's can close. With reflection on — every test host, every
    /// untrimmed application — that half is answered by reflection and this method is unnecessary. A
    /// <c>PublishTrimmed</c> or <c>PublishAot</c> application has no reflection to fall back to, and a
    /// value type Quartz cannot name reaches the writer as a <see cref="NotSupportedException" /> naming
    /// it. This is how the application answers for it.
    /// </para>
    /// <para>
    /// Custom trigger and calendar types need nothing here:
    /// <see cref="AddTriggerSerializer{TTrigger}" /> and <see cref="AddCalendarSerializer{TCalendar}" />
    /// know the type statically, so the registry already answers for them.
    /// </para>
    /// <para>
    /// Resolvers are asked in the order they were added, behind Quartz's own contract and in front of
    /// reflection. A resolver that does not know a type returns nothing and the next one is asked, so
    /// handing in a context that names a type Quartz already covers changes nothing.
    /// </para>
    /// </remarks>
    public SystemTextJsonSerializerRegistry AddTypeInfoResolver(IJsonTypeInfoResolver resolver)
    {
        ArgumentNullException.ThrowIfNull(resolver);

        resolvers.Add(resolver);
        return this;
    }

    /// <summary>
    /// The resolvers this registry contributes to a serializer's options, in the order they are asked:
    /// the types registered here first, then whatever the application handed to
    /// <see cref="AddTypeInfoResolver" />.
    /// </summary>
    internal IReadOnlyList<IJsonTypeInfoResolver> TypeInfoResolvers => resolvers;

    internal ITriggerSerializer GetTriggerSerializer(string? typeName)
    {
        return triggerSerializers.Get(typeName, "Don't know how to handle");
    }

    internal ICalendarSerializer GetCalendarSerializer(string? typeName)
    {
        return calendarSerializers.Get(typeName, "Don't know how to handle");
    }

    /// <summary>
    /// Remembers how to build the metadata for a registered type, while the type is still a type
    /// argument.
    /// </summary>
    /// <remarks>
    /// This is the whole reason the registration methods are generic in the type rather than taking a
    /// <see cref="Type" />. A trigger is written through <c>TriggerConverter</c> and a calendar through
    /// <c>CalendarConverter</c>, so the metadata for either is
    /// <see cref="JsonMetadataServices.CreateValueInfo{T}" /> over that converter — but that call needs
    /// <typeparamref name="T" /> at compile time, and reaching it from a <see cref="Type" /> would mean
    /// <c>MakeGenericMethod</c>, which is the very thing an AOT publish cannot do. Captured here, the
    /// closure is compiled like any other generic instantiation.
    /// </remarks>
    private void RegisterTypeInfo<T>()
    {
        registeredTypeInfos[typeof(T)] = static (options, converter) => JsonMetadataServices.CreateValueInfo<T>(options, converter);
    }

    /// <summary>
    /// Answers for the trigger and calendar types registered with a registry, out of the converter the
    /// options carry for each.
    /// </summary>
    /// <remarks>
    /// The converter is looked up by walking <see cref="JsonSerializerOptions.Converters" /> rather than
    /// by calling <c>GetConverter</c>, which is <c>RequiresUnreferencedCode</c> and
    /// <c>RequiresDynamicCode</c> both. It is the same walk a generated
    /// <see cref="JsonSerializerContext" /> does before it reaches for the metadata it wrote, which is
    /// what makes a runtime-registered converter win over generated metadata; this resolver has no
    /// metadata of its own to fall back to, so options without Quartz's converters get nothing from it
    /// and the chain moves on.
    /// </remarks>
    private sealed class RegisteredTypeResolver(
        Dictionary<Type, Func<JsonSerializerOptions, JsonConverter, JsonTypeInfo>> registeredTypeInfos) : IJsonTypeInfoResolver
    {
        public JsonTypeInfo? GetTypeInfo(Type type, JsonSerializerOptions options)
        {
            if (!registeredTypeInfos.TryGetValue(type, out Func<JsonSerializerOptions, JsonConverter, JsonTypeInfo>? create))
            {
                return null;
            }

            JsonConverter? converter = FindConverter(type, options);
            if (converter is null)
            {
                return null;
            }

            JsonTypeInfo typeInfo = create(options, converter);
            typeInfo.OriginatingResolver = this;
            return typeInfo;
        }

        private static JsonConverter? FindConverter(Type type, JsonSerializerOptions options)
        {
            IList<JsonConverter> converters = options.Converters;
            for (int i = 0; i < converters.Count; i++)
            {
                JsonConverter converter = converters[i];
                if (!converter.CanConvert(type))
                {
                    continue;
                }

                // A factory is asked for the converter it would make; Quartz registers none, but an
                // application is free to, and handing the factory itself to CreateValueInfo would throw.
                return converter is JsonConverterFactory factory ? factory.CreateConverter(type, options) : converter;
            }

            return null;
        }
    }
}
