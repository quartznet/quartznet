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

using System.Collections.Concurrent;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Reflection;

namespace Quartz.Util;

/// <summary>
/// Utility methods that are used to convert objects from one type into another.
/// </summary>
/// <author>Aleksandar Seovic</author>
/// <author>Marko Lahma</author>
internal static class ObjectUtils
{
    /// <summary>
    /// Convert the value to the required <see cref="System.Type"/> (if necessary from a string).
    /// </summary>
    /// <remarks>
    /// The two answers that need no <see cref="TypeConverter" /> are given here — a value that is
    /// already what the target takes, and the target's own default for a missing one — and only the
    /// conversion itself sits behind <see cref="ConvertUsingTypeConverter" />, which is
    /// <see cref="RequiresUnreferencedCodeAttribute" /> because <see cref="TypeDescriptor" /> finds a
    /// converter by reflecting over the target type. Most of what a <see cref="JobDataMap" /> carries
    /// is answered by the first of the two and never reaches it.
    /// </remarks>
    /// <param name="newValue">The proposed change value.</param>
    /// <param name="requiredType">
    /// The <see cref="System.Type"/> we must convert to.
    /// </param>
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
    /// Instantiates an instance of the type specified.
    /// </summary>
    public static T InstantiateType<T>([DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] Type? type)
    {
        ConstructorInfo ci = GetDefaultConstructor(type);
        return (T) ci.Invoke([]);
    }

    public static ConstructorInfo GetDefaultConstructor([DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] Type? type)
    {
        if (type is null)
        {
            Throw.ArgumentNullException(nameof(type), "Cannot instantiate null");
        }

        var ci = type.GetConstructor(Type.EmptyTypes);
        if (ci is null)
        {
            Throw.ArgumentException("Cannot instantiate type which has no empty constructor", type.Name);
        }

        return ci;
    }

    /// <summary>
    /// Sets the object properties using reflection.
    /// </summary>
    [RequiresUnreferencedCode("Component properties are set by name on a type Quartz is handed at run time; a component named by a quartz.* configuration key, and the properties that key sets, are not guaranteed to survive trimming.")]
    public static void SetObjectProperties(object obj, string[] propertyNames, object[] propertyValues)
    {
        for (int i = 0; i < propertyNames.Length; i++)
        {
            string name = propertyNames[i];
            try
            {
                SetPropertyValue(obj, name, propertyValues[i]);
            }
            catch (Exception nfe)
            {
                Throw.SchedulerConfigException($"Could not parse property '{name}' into correct data type: {nfe.Message}", nfe);
            }
        }
    }

    /// <summary>
    /// Sets the object properties using reflection.
    /// </summary>
    /// <param name="obj">The object to set values to.</param>
    /// <param name="props">The properties to set to object.</param>
    [RequiresUnreferencedCode("Component properties are set by name on a type Quartz is handed at run time; a component named by a quartz.* configuration key, and the properties that key sets, are not guaranteed to survive trimming.")]
    public static void SetObjectProperties(object obj, NameValueCollection props)
    {
        // remove the type
        props.Remove("type");

        foreach (string name in props.Keys)
        {
            try
            {
                var value = props[name];
                SetPropertyValue(obj, name, value);
            }
            catch (Exception nfe)
            {
                Throw.SchedulerConfigException($"Could not parse property '{name}' into correct data type: {nfe.Message}", nfe);
            }
        }
    }

    private static readonly ConcurrentDictionary<(Type ObjectType, string PropertyName), PropertyInfo?> propertyResolutionCache = new();

    /// <summary>
    /// Non-public setters are bound too.
    /// </summary>
    /// <remarks>
    /// A shipped component's settings are public on its options type and internal on the component
    /// itself, so that the options type is the one way to configure it in code. The flat
    /// <c>quartz.plugin.&lt;name&gt;.*</c> and <c>quartz.jobStore.lockHandler.*</c> keys write the
    /// component directly, and they have to keep working — so this binder sees what a caller cannot.
    /// </remarks>
    private const BindingFlags Bindings =
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

    [RequiresUnreferencedCode("Component properties are set by name on a type Quartz is handed at run time; a component named by a quartz.* configuration key, and the properties that key sets, are not guaranteed to survive trimming.")]
    public static void SetPropertyValue(object target, string propertyName, object? value)
    {
        var pi = propertyResolutionCache.GetOrAdd((target.GetType(), propertyName), static tuple =>
        {
            string name = char.IsLower(tuple.PropertyName[0])
                ? char.ToUpper(tuple.PropertyName[0]) + tuple.PropertyName.Substring(1)
                : tuple.PropertyName;

            Type t = tuple.ObjectType;
            var propertyInfo = t.GetProperty(name, Bindings);

            if (propertyInfo is null || !propertyInfo.CanWrite)
            {
                // try to find from interfaces
                foreach (var interfaceType in t.GetInterfaces())
                {
                    propertyInfo = interfaceType.GetProperty(name, Bindings);
                    if (propertyInfo is not null && propertyInfo.CanWrite)
                    {
                        // found suitable
                        break;
                    }
                }
            }

            return propertyInfo;
        });

        if (pi is null)
        {
            // not match from anywhere
            Throw.MemberAccessException($"No writable property '{propertyName}' found");
        }

        var mi = pi.GetSetMethod(nonPublic: true);

        if (mi is null)
        {
            Throw.MemberAccessException($"Property '{propertyName}' has no setter");
        }

        if (mi.GetParameters()[0].ParameterType == typeof(TimeSpan))
        {
            // special handling
            value = GetTimeSpanValueForProperty(pi, value);
        }
        else
        {
            value = ConvertValueIfNecessary(mi.GetParameters()[0].ParameterType, value);
        }

        mi.Invoke(target, [value]);
    }

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

    /// <summary>
    /// Whether the type, or anything it inherits from, carries the attribute.
    /// </summary>
    /// <remarks>
    /// No annotation is needed on <paramref name="typeToExamine" />: an attribute is part of the
    /// metadata of a type that survives trimming at all, so the trimmer has nothing to preserve on its
    /// account.
    /// </remarks>
    public static bool IsAttributePresent(Type typeToExamine, Type attributeType)
    {
        return typeToExamine.GetCustomAttributes(attributeType, inherit: true).Length > 0;
    }

    /// <summary>
    /// Whether the type, anything it inherits from, or any interface it implements carries the attribute.
    /// </summary>
    /// <remarks>
    /// The interfaces are walked flat rather than recursively, because <see cref="Type.GetInterfaces" />
    /// already reports the ones an interface itself inherits — the recursion asked the same question
    /// twice, and asking it once is what lets the requirement stop at
    /// <see cref="DynamicallyAccessedMemberTypes.Interfaces" /> instead of travelling.
    /// </remarks>
    public static bool IsAnyInterfaceAttributePresent(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.Interfaces)] Type typeToExamine,
        Type attributeType)
    {
        if (IsAttributePresent(typeToExamine, attributeType))
        {
            return true;
        }

        foreach (var type in typeToExamine.GetInterfaces())
        {
            if (IsAttributePresent(type, attributeType))
            {
                return true;
            }
        }

        return false;
    }
}