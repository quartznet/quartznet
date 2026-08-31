namespace Quartz.Serialization.Newtonsoft;

/// <summary>
/// What a job data value may be when this serializer writes one, which is what it may be when the
/// built-in one does.
/// </summary>
/// <remarks>
/// <para>
/// The write side used to accept whatever Json.NET could reflect over, and reflection being able to
/// write a value is not the same as anything being able to read it back. A <see cref="TimeZoneInfo" />
/// in a job data map was the case that showed it: written as
/// <c>{"$type":"System.TimeZoneInfo, …","Id":"Tokyo Standard Time", …}</c> and then unreadable, because
/// <see cref="QuartzContractResolver" /> sets <c>IgnoreSerializableInterface</c> and every public member
/// of a zone is read-only, so there was nothing to rebuild it from. The blob was in the column by then,
/// and the failure belonged to whoever next ran the job.
/// </para>
/// <para>
/// The accepted set is not a copy of the System.Text.Json one, it <em>is</em> that one — the same
/// <c>JobDataValues.Accepted</c> the built-in serializer refuses against, read out of the core package
/// this one already depends on. Two lists meaning to say the same thing is how the write and read sides
/// came to disagree in the first place, and the same trap is open across the two serializers: both are
/// documented store formats and an application is free to switch between them, so a value only one of
/// them accepts is a one-way door nobody is warned about. <c>JobDataMapPortabilityTest</c> is what holds
/// the pair to it.
/// </para>
/// <para>
/// Past that set, a value is the application's own choice, and
/// <see cref="NewtonsoftJsonSerializerRegistry.AddJobDataValueType{T}" /> is how it declares one. That is
/// this package's counterpart to
/// <c>SystemTextJsonSerializerRegistry.AddTypeInfoResolver</c>: Json.NET needs no metadata handed to it,
/// so the declaration carries nothing but the application's word that the type reads back.
/// </para>
/// </remarks>
internal static class JobDataValues
{
    /// <summary>
    /// Throws unless <paramref name="value" /> is one a reader will accept back.
    /// </summary>
    /// <remarks>
    /// The check is by runtime type rather than by trying the write and inspecting what came out,
    /// because refusing has to happen before a single byte reaches the column.
    /// </remarks>
    /// <exception cref="Quartz.JsonSerializationException">
    /// The value is of a type no reader can turn back into it, and the application has not declared one.
    /// </exception>
    public static void Refuse(string key, object? value, NewtonsoftJsonSerializerRegistry registry)
    {
        if (value is null)
        {
            return;
        }

        Type type = value.GetType();

        // Enums are accepted by rule rather than by name, because they are written as their number and
        // read back as one, and no list can enumerate an application's own.
        if (SystemTextJson.JobDataValues.Accepted.Contains(type) || type.IsEnum || registry.DeclaresJobDataValueType(type))
        {
            return;
        }

        throw new Quartz.JsonSerializationException(
            $"Job data entry '{key}' holds a {type.FullName}, which Quartz's JSON format cannot read back. " +
            "A job data value has to be one of the types JobDataMap declares an accessor for " +
            "(string, bool, char, int, long, float, double, decimal, DateTime, DateTimeOffset, TimeSpan, Guid, DateOnly, TimeOnly or an enum), " +
            "a Dictionary<string, string>, or a type the application declares through NewtonsoftJsonSerializerRegistry.AddJobDataValueType. " +
            "Anything with structure of its own has to be serialized by the job and stored as a string.");
    }

    /// <summary>
    /// Refuses on the first unreadable entry of a map, before any of them is written, so a map with one
    /// such value in it puts nothing at all in the column.
    /// </summary>
    public static void Refuse(JobDataMap jobDataMap, NewtonsoftJsonSerializerRegistry registry)
    {
        foreach (KeyValuePair<string, object?> pair in jobDataMap)
        {
            Refuse(pair.Key, pair.Value, registry);
        }
    }
}
