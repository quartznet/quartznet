#region License

/*
 * Copyright 2009- Marko Lahma
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

using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Reflection;

namespace Quartz.Util;

/// <summary>
/// Coerces a value into the type a property takes.
/// </summary>
/// <remarks>
/// <para>
/// Three callers, on two different clocks. <see cref="Quartz.Impl.PropertySettingJobFactory" /> uses it
/// on the fire path, to put a <see cref="JobDataMap" /> value onto a job's property;
/// <see cref="JobDataExpression" /> uses it at configuration time, twice per value, to prove a
/// conversion is lossless before the map ever stores one; and
/// <c>Quartz.Configuration.PropertyBinder</c> uses it while the container is being built. The
/// semantics are shared deliberately — a value that binds at configuration time has to bind the same
/// way when the job runs, which is the whole of what <see cref="JobDataExpression" /> promises.
/// </para>
/// <para>
/// Only <see cref="ConvertUsingTypeConverter" /> is reflective, and it says so. The answers that need
/// no <see cref="TypeConverter" /> — a value that already is what the target takes, and the target's
/// own default for a missing one — are given in front of it, which is where most of what a
/// <see cref="JobDataMap" /> carries is answered.
/// </para>
/// </remarks>
/// <author>Aleksandar Seovic</author>
/// <author>Marko Lahma</author>
internal static class ValueConverter
{
    /// <summary>
    /// Convert the value to the required <see cref="System.Type"/> (if necessary from a string).
    /// </summary>
    /// <param name="requiredType">
    /// The <see cref="System.Type"/> we must convert to.
    /// </param>
    /// <param name="newValue">The proposed change value.</param>
    /// <returns>The new value, possibly the result of type conversion.</returns>
    public static object? ConvertValueIfNecessary(Type requiredType, object? newValue)
    {
        if (newValue is null)
        {
            return DefaultValue(requiredType);
        }

        // if it is assignable, return the value right away
        if (requiredType.IsInstanceOfType(newValue))
        {
            return newValue;
        }

        return ConvertUsingTypeConverter(requiredType, newValue);
    }

    /// <summary>
    /// The value a target of this type holds when there is nothing to put in it.
    /// </summary>
    private static object? DefaultValue(Type requiredType)
    {
        if (requiredType.IsValueType)
        {
            return Activator.CreateInstance(requiredType);
        }

        // return default
        return null;
    }

    /// <summary>
    /// Converts through <see cref="TypeDescriptor" />, in the order this has always tried things.
    /// </summary>
    /// <remarks>
    /// Nothing in here can be annotated instead. A converter is found by reflecting over the target
    /// type, which arrives as <see cref="PropertyInfo.PropertyType" /> or as a setter's parameter type —
    /// neither of which the framework annotates, or could. Splitting it out at least keeps the
    /// requirement off the values that never need converting.
    /// </remarks>
    [RequiresUnreferencedCode("A value whose type does not match the target's is converted through TypeDescriptor, which finds the converter by reflecting over the target type; neither that type nor its converter is guaranteed to survive trimming.")]
    private static object? ConvertUsingTypeConverter(Type requiredType, object newValue)
    {
        // try to convert using type converter
        TypeConverter typeConverter = TypeDescriptor.GetConverter(requiredType);
        if (typeConverter.CanConvertFrom(newValue.GetType()))
        {
            return typeConverter.ConvertFrom(null, CultureInfo.InvariantCulture, newValue);
        }

        typeConverter = TypeDescriptor.GetConverter(newValue.GetType());
        if (typeConverter.CanConvertTo(requiredType))
        {
            return typeConverter.ConvertTo(null, CultureInfo.InvariantCulture, newValue, requiredType);
        }

        if (requiredType == typeof(Type))
        {
            return Type.GetType(newValue.ToString()!, throwOnError: true);
        }

        if (newValue.GetType().IsEnum)
        {
            // If we couldn't convert the type, but it's an enum type, try convert it as an int
            return ConvertValueIfNecessary(requiredType, Convert.ChangeType(newValue, Convert.GetTypeCode(newValue), null));
        }

        if (requiredType.IsEnum)
        {
            // if JSON serializer creates numbers from enums, be prepared for that
            try
            {
                return Enum.ToObject(requiredType, newValue);
            }
            catch
            {
            }
        }

        Throw.NotSupportedException($"{newValue} is no a supported value for a target of type {requiredType}");
        return null;
    }

    /// <summary>
    /// The <see cref="TimeSpan" /> a property takes, read the way its
    /// <see cref="TimeSpanParseRuleAttribute" /> says to read it.
    /// </summary>
    /// <remarks>
    /// A property with a parse rule takes a bare number and means milliseconds, seconds, minutes or
    /// hours by it — <c>quartz.threadPool.idleWaitTime = 30000</c> — and a property without one takes
    /// whatever <see cref="TimeSpan" /> parses. Both the fire path and the configuration binder read
    /// durations this way, so they read them the same way; nothing here reflects over the property's
    /// type, only over its own attributes, which survive trimming with the property.
    /// </remarks>
    public static TimeSpan GetTimeSpanValueForProperty(PropertyInfo pi, object? value)
    {
        object[] attributes = pi.GetCustomAttributes(typeof(TimeSpanParseRuleAttribute), false).ToArray();

        if (attributes.Length == 0)
        {
            return (TimeSpan) ConvertValueIfNecessary(typeof(TimeSpan), value)!;
        }

        TimeSpanParseRuleAttribute attribute = (TimeSpanParseRuleAttribute) attributes[0];
        long longValue = Convert.ToInt64(value);
        switch (attribute.Rule)
        {
            case TimeSpanParseRule.Milliseconds:
                return TimeSpan.FromMilliseconds(longValue);
            case TimeSpanParseRule.Seconds:
                return TimeSpan.FromSeconds(longValue);
            case TimeSpanParseRule.Minutes:
                return TimeSpan.FromMinutes(longValue);
            case TimeSpanParseRule.Hours:
                return TimeSpan.FromHours(longValue);
            default:
                Throw.ArgumentOutOfRangeException();
                return default;
        }
    }
}
