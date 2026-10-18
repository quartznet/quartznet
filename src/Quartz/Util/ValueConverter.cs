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
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

using Quartz.Impl;

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
/// Nothing on the common path reflects. A value that already is what the target takes, a missing
/// value, a type name, and the conversions job data needs in practice — strings to primitives, enums,
/// <see cref="Guid" />, <see cref="TimeSpan" />, dates, times of day, <see cref="Half" />, the 128-bit
/// integers and <see cref="Uri" />, numbers to numbers and
/// enums, and the way back — are answered in front of <see cref="TypeConverter" />, each the way the
/// converter <see cref="TypeDescriptor" /> would have picked answers it, quirks included
/// (<c>ValueConverterDifferentialTest</c> holds them to that). What none of them answers reaches
/// <see cref="ConvertUsingTypeConverter" /> only while
/// <see cref="FeatureSwitches.IsTypeConverterCoercionSupported" /> is on, and is refused otherwise, so a
/// trimmed or native publish carries no <see cref="TypeDescriptor" /> at all.
/// </para>
/// </remarks>
/// <author>Aleksandar Seovic</author>
/// <author>Marko Lahma</author>
internal static partial class ValueConverter
{
    /// <summary>
    /// What the reflective half says for itself. Every call to it is guarded by the switch, so an
    /// application that reads the warning is one that turned the switch back on.
    /// </summary>
    private const string ConvertsByReflection =
        "A value no built-in conversion handles is converted through TypeDescriptor, which finds the converter by reflecting over the target type; "
        + FeatureSwitches.TypeConverterCoercionSwitch + " guards every call.";

    /// <summary>
    /// Convert the value to the required <see cref="System.Type"/> (if necessary from a string).
    /// </summary>
    /// <param name="requiredType">
    /// The <see cref="System.Type"/> we must convert to.
    /// </param>
    /// <param name="newValue">The proposed change value.</param>
    /// <returns>The new value, possibly the result of type conversion.</returns>
    /// <exception cref="NotSupportedException">No conversion exists.</exception>
    /// <exception cref="CoercionNotSupportedException">
    /// Only a <see cref="TypeConverter" /> could make the conversion, and
    /// <see cref="FeatureSwitches.IsTypeConverterCoercionSupported" /> is off.
    /// </exception>
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

        if (requiredType == typeof(Type) && newValue is string typeName)
        {
            return ResolveType(typeName);
        }

        if (TryConvertWithoutTypeConverter(requiredType, newValue, out object? converted))
        {
            return converted;
        }

        if (FeatureSwitches.IsTypeConverterCoercionSupported)
        {
            return ConvertUsingTypeConverter(requiredType, newValue);
        }

        // The callers put the property's name in front of the message.
        throw new CoercionNotSupportedException(requiredType, newValue.GetType());
    }

    /// <summary>
    /// The value a target of this type holds when there is nothing to put in it.
    /// </summary>
    /// <remarks>
    /// <see cref="Activator.CreateInstance(Type)" /> while the switch is on, which is what this has
    /// always done: <see langword="null" /> for a <see cref="Nullable{T}" />, and for any other struct
    /// what its parameterless constructor makes. Off, <see cref="ZeroedDefault" /> gives the same answer
    /// with no reflection, except that a struct declaring a parameterless constructor of its own gets
    /// <c>default</c> without running it.
    /// </remarks>
    private static object? DefaultValue(Type requiredType)
    {
        if (!requiredType.IsValueType)
        {
            return null;
        }

        return FeatureSwitches.IsTypeConverterCoercionSupported
            ? CreateDefaultByReflection(requiredType)
            : ZeroedDefault(requiredType);
    }

    /// <summary>
    /// The default the switch-off path gives: <see langword="null" /> for a <see cref="Nullable{T}" />,
    /// and a boxed all-zero instance of any other value type.
    /// </summary>
    /// <remarks>
    /// <see cref="RuntimeHelpers.Box(ref byte, RuntimeTypeHandle)" /> asks nothing of the type's members,
    /// so it carries no annotation, where <see cref="Activator.CreateInstance(Type)" /> and
    /// <see cref="RuntimeHelpers.GetUninitializedObject" /> both want the target's constructors kept, and
    /// <c>Array.CreateInstance</c> wants runtime code generation. All-zero is what <c>default</c> is for a
    /// struct, and what <see cref="Activator.CreateInstance(Type)" /> gives for every value type that
    /// declares no parameterless constructor — every primitive, enum and framework struct among them.
    /// </remarks>
    /// <param name="valueType">A value type.</param>
    internal static object? ZeroedDefault(Type valueType)
    {
        if (Nullable.GetUnderlyingType(valueType) is not null)
        {
            return null;
        }

        byte[] zeroed = new byte[RuntimeHelpers.SizeOf(valueType.TypeHandle)];
        return RuntimeHelpers.Box(ref MemoryMarshal.GetArrayDataReference(zeroed), valueType.TypeHandle);
    }

    [RequiresUnreferencedCode("A struct's default is made by its parameterless constructor, which the trimmer may have removed; " + FeatureSwitches.TypeConverterCoercionSwitch + " guards every call.")]
    private static object? CreateDefaultByReflection(Type valueType) => Activator.CreateInstance(valueType);

    /// <summary>
    /// A type named by a string: the registered types first, as <see cref="SimpleTypeLoader" /> asks
    /// them, and the runtime after them while <see cref="FeatureSwitches.IsTypeNameResolutionSupported" />
    /// allows it.
    /// </summary>
    private static Type ResolveType(string typeName)
    {
        if (RegisteredTypeNames.Find(typeName) is { } registered)
        {
            return registered;
        }

        if (FeatureSwitches.IsTypeNameResolutionSupported)
        {
            return ResolveTypeByReflection(typeName);
        }

        Throw.TypeLoadException(RegisteredTypeNames.NotRegistered(typeName));
        return null!;
    }

    [RequiresUnreferencedCode("A type name nothing registered is resolved with Type.GetType, which a trimmed application cannot rely on; " + FeatureSwitches.TypeNameResolutionSwitch + " guards every call.")]
    private static Type ResolveTypeByReflection(string typeName) => Type.GetType(typeName, throwOnError: true)!;

    /// <summary>
    /// Converts through <see cref="TypeDescriptor" />, in the order this has always tried things.
    /// </summary>
    /// <remarks>
    /// Nothing in here can be annotated instead. A converter is found by reflecting over the target
    /// type, which arrives as <see cref="PropertyInfo.PropertyType" /> or as a setter's parameter type —
    /// neither of which the framework annotates, or could. So it says
    /// <see cref="RequiresUnreferencedCodeAttribute" />, and its one caller asks
    /// <see cref="FeatureSwitches.IsTypeConverterCoercionSupported" /> first.
    /// </remarks>
    [RequiresUnreferencedCode(ConvertsByReflection)]
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
            return ResolveType(newValue.ToString()!);
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

        Throw.NotSupportedException($"{newValue} is not a supported value for a target of type {requiredType}");
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
