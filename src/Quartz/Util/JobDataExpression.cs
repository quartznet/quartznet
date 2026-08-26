#region License

/*
 * All content copyright Marko Lahma, unless otherwise indicated. All rights reserved.
 *
 * Licensed under the Apache License, Version 2.0 (the "License"); you may not
 * use this file except in compliance with the License. You may obtain a copy
 * of the License at
 *
 *   http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS, WITHOUT
 * WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the
 * License for the specific language governing permissions and limitations
 * under the License.
 *
 */

#endregion

using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using System.Reflection;

namespace Quartz.Util;

/// <summary>
/// Turns a property expression such as <c>job => job.Parameter</c> into the <see cref="JobDataMap" /> entry
/// the job factory will bind back onto that property.
/// </summary>
/// <remarks>
/// The expression is only ever inspected, never compiled or invoked, so nothing here needs the job type to
/// be constructible and nothing is lost to trimming beyond the property the caller named.
/// </remarks>
internal static class JobDataExpression
{
    /// <summary>
    /// Returns the property the expression reads, after checking that job data can actually be bound to it.
    /// </summary>
    internal static PropertyInfo GetProperty<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties)] TJob, TValue>(Expression<Func<TJob, TValue>> jobProperty)
    {
        if (jobProperty is null)
        {
            Throw.ArgumentNullException(nameof(jobProperty));
        }

        // A property whose type differs from TValue - an enum read as an object, say - reaches us wrapped
        // in the conversion the compiler inserted.
        var body = Unwrap(jobProperty.Body);

        if (body is MemberExpression member && member.Member is PropertyInfo propertyInfo)
        {
            // Only a property read off the lambda's own parameter can be bound: the job factory sets
            // properties on the job instance itself, so neither a path through another property nor a
            // static has anywhere to land. The read is unwrapped too, because a TJob that is itself a type
            // parameter is converted to its constraint before the property is reached.
            if (member.Expression is null || Unwrap(member.Expression) != jobProperty.Parameters[0])
            {
                Throw.ArgumentException($"Job data can only be bound to a property read directly off the job, and '{jobProperty.Body}' reads something else.", nameof(jobProperty));
            }

            // Unwrapping the receiver also strips a downcast, which would otherwise let a property of some
            // derived job through - and the job being built is a TJob, which has no such property.
            if (!propertyInfo.DeclaringType!.IsAssignableFrom(typeof(TJob)))
            {
                Throw.ArgumentException($"Job data can only be bound to a property of {typeof(TJob)}, and '{propertyInfo.Name}' is declared on {propertyInfo.DeclaringType}.", nameof(jobProperty));
            }

            if (propertyInfo.GetSetMethod() is null)
            {
                Throw.ArgumentException($"Job data cannot be bound to property '{propertyInfo.Name}' of {typeof(TJob)}, because it has no public setter.", nameof(jobProperty));
            }

            VerifyTheJobFactoryFindsIt(typeof(TJob), propertyInfo, nameof(jobProperty));

            return propertyInfo;
        }

        return Throw.ArgumentException<PropertyInfo>($"Job data can only be bound to a property of {typeof(TJob)}, and '{jobProperty.Body}' does not name one.", nameof(jobProperty));
    }

    /// <summary>
    /// Checks that the key this property's name becomes leads the job factory back to this same property.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Only a name reaches the <see cref="JobDataMap" />, and the job factory turns that name back into a
    /// property by its own rules. Rather than restate those rules and risk them drifting apart, this runs
    /// the factory's own lookup - <see cref="Impl.PropertySettingJobFactory.SetObjectProperty" /> - and
    /// insists it arrives back where the caller started.
    /// </para>
    /// <para>
    /// That one question answers several at once: a property whose name starts with a lower-case letter is
    /// looked up under a name it does not have, an explicitly implemented interface property is not public
    /// on the job at all, and two properties of one name are either ambiguous to reflection or resolve to
    /// whichever the job's own code would see - never to the other one.
    /// </para>
    /// <para>
    /// The lookup is done against the job type, which is as much as configuration time
    /// knows. A job built for a base type and then pointed at a derived one through <c>OfType</c> can still
    /// disagree, but that disagreement surfaces loudly as an <see cref="AmbiguousMatchException" /> when the
    /// job runs rather than silently binding the wrong thing.
    /// </para>
    /// </remarks>
    private static void VerifyTheJobFactoryFindsIt([DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties)] Type jobType, PropertyInfo property, string paramName)
    {
        // Exactly what PropertySettingJobFactory.SetObjectProperty does to a key before looking it up.
        var name = property.Name;
        var lookupName = char.IsUpper(name[0]) ? name : char.ToUpper(name[0]) + name.Substring(1);

        PropertyInfo? found;
        try
        {
            found = jobType.GetProperty(lookupName);
        }
        catch (AmbiguousMatchException)
        {
            Throw.ArgumentException($"Job data cannot be bound to property '{name}' of {jobType}, because more than one property answers to that name and job data is bound by name alone.", paramName);
            return;
        }

        if (found is null)
        {
            Throw.ArgumentException($"Job data cannot be bound to property '{name}' of {jobType}, because the job factory looks a key up as '{lookupName}' and {jobType} has no such public property. A property whose name starts with a lower-case letter, or one that implements an interface explicitly, cannot receive job data.", paramName);
            return;
        }

        // Resolving to a different declaration is fine when it is the same property seen from further down
        // - an override, or the class side of an interface property. It is only a different property when
        // the type differs, and then the value would be converted for one and set on the other.
        if (found.PropertyType != property.PropertyType)
        {
            Throw.ArgumentException($"Job data cannot be bound to property '{name}' of {property.DeclaringType}, because the job factory resolves that name to the {found.PropertyType} declared on {found.DeclaringType}.", paramName);
        }
    }

    private static Expression Unwrap(Expression expression)
    {
        while (expression is UnaryExpression { NodeType: ExpressionType.Convert or ExpressionType.ConvertChecked } unary)
        {
            expression = unary.Operand;
        }

        return expression;
    }

    /// <summary>
    /// Returns the value in the shape the map should hold it in for the given property.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The shape is decided by the property's own type rather than by the value's, because that is what the
    /// job factory converts back to. An enum property takes the enum's name: left as a number it would be
    /// written as one by the JSON serializer and read back as one, which binds onto the property but leaves
    /// anything reading the map looking at an integer.
    /// </para>
    /// <para>
    /// A value of a different type than the property's - the <c>int</c> literal an implicit widening turns a
    /// <c>byte</c> property's value into, say - is converted here, so a value that does not fit is rejected
    /// at the call that wrote it instead of being dropped when the job runs.
    /// </para>
    /// </remarks>
    internal static object? NormalizeValue(PropertyInfo property, object? value)
    {
        var underlying = Nullable.GetUnderlyingType(property.PropertyType);
        var propertyType = underlying ?? property.PropertyType;

        if (value is null)
        {
            // Type inference widens TValue to the nullable form when the argument is one, so a null can
            // reach a property that has no way to hold it - where the job factory would quietly substitute
            // the type's default instead.
            if (property.PropertyType.IsValueType && underlying is null)
            {
                Throw.ArgumentException($"Null cannot be bound to property '{property.Name}' of {property.PropertyType}, which has no null value.", nameof(value));
            }

            return null;
        }

        // A duration the job factory will read through a parse rule has to be stored as the number that
        // rule expects, or it only binds until the map has been through a serializer.
        if (propertyType == typeof(TimeSpan) && value is TimeSpan duration && property.GetCustomAttribute<TimeSpanParseRuleAttribute>() is { } parseRule)
        {
            return AsParseRuleUnits(property, duration, parseRule.Rule);
        }

        // The job factory refuses a string that is not exactly one character for a char property, and it is
        // right to: converting turns the empty string into NUL, which round-trips back to the empty string
        // and so looks lossless to the check below.
        if (propertyType == typeof(char) && value is string text && text.Length != 1)
        {
            Throw.ArgumentException($"Value '{text}' cannot be bound to property '{property.Name}' of {property.PropertyType}, because only a string of exactly one character can be.", nameof(value));
        }

        var converted = propertyType.IsInstanceOfType(value) ? value : Convert(property, propertyType, value);

        // The name, not the number: left as a number the JSON serializer would write one and read one back,
        // which binds onto the property but leaves anything reading the map looking at an integer.
        return propertyType.IsEnum ? converted.ToString() : converted;
    }

    private static long AsParseRuleUnits(PropertyInfo property, TimeSpan duration, TimeSpanParseRule rule)
    {
        var units = rule switch
        {
            TimeSpanParseRule.Milliseconds => duration.TotalMilliseconds,
            TimeSpanParseRule.Seconds => duration.TotalSeconds,
            TimeSpanParseRule.Minutes => duration.TotalMinutes,
            TimeSpanParseRule.Hours => duration.TotalHours,
            _ => Throw.ArgumentException<double>($"Property '{property.Name}' carries an unknown {nameof(TimeSpanParseRule)}.", nameof(property)),
        };

        if (units != Math.Floor(units))
        {
            Throw.ArgumentException($"Value '{duration}' cannot be bound to property '{property.Name}', because its [{nameof(TimeSpanParseRuleAttribute)}] reads whole {rule} and this duration is not a whole number of them.", nameof(duration));
        }

        return (long) units;
    }

    private static object Convert(PropertyInfo property, Type propertyType, object value)
    {
        object? converted;
        try
        {
            converted = ValueConverter.ConvertValueIfNecessary(propertyType, value);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            // The conversion's own exception is kept: a failing custom TypeConverter, or a type name that
            // will not load, is the interesting part, and only it says why.
            return Throw.ArgumentException<object>($"Value '{value}' cannot be bound to property '{property.Name}' of type {property.PropertyType}: {e.Message}", nameof(value), e);
        }

        // A TypeConverter may answer null for an input it recognises but cannot represent - the empty
        // string, most often - which would otherwise slip past the null check above.
        if (converted is null)
        {
            return Throw.ArgumentException<object>($"Value '{value}' cannot be bound to property '{property.Name}' of type {property.PropertyType}, because converting it produces nothing.", nameof(value));
        }

        // Converting is allowed to change the representation, not the value. Narrowing a double to an int
        // rounds, to a float saturates, and an empty string to a char yields NUL - all silently, and all
        // of them the coercion this API exists to stop.
        if (!RoundTrips(value, converted))
        {
            return Throw.ArgumentException<object>($"Value '{value}' cannot be bound to property '{property.Name}' of type {property.PropertyType} without losing information; it would be stored as '{converted}'.", nameof(value));
        }

        return converted;
    }

    private static bool RoundTrips(object value, object converted)
    {
        try
        {
            return Equals(ValueConverter.ConvertValueIfNecessary(value.GetType(), converted), value);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            return false;
        }
    }
}
