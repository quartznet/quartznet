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
    /// <param name="newValue">The proposed change value.</param>
    /// <param name="requiredType">
    /// The <see cref="System.Type"/> we must convert to.
    /// </param>
    /// <returns>The new value, possibly the result of type conversion.</returns>
    public static object? ConvertValueIfNecessary(Type requiredType, object? newValue)
    {
        if (newValue is not null)
        {
            // if it is assignable, return the value right away
            if (requiredType.IsInstanceOfType(newValue))
            {
                return newValue;
            }

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

            ThrowHelper.ThrowNotSupportedException($"{newValue} is no a supported value for a target of type {requiredType}");
        }

        if (requiredType.IsValueType)
        {
            return Activator.CreateInstance(requiredType);
        }

        // return default
        return null;
    }

    /// <summary>
    /// Instantiates an instance of the type specified.
    /// </summary>
    public static T InstantiateType<T>(Type? type)
    {
        ConstructorInfo ci = GetDefaultConstructor(type);
        return (T) ci.Invoke([]);
    }

    public static ConstructorInfo GetDefaultConstructor(Type? type)
    {
        if (type is null)
        {
            ThrowHelper.ThrowArgumentNullException(nameof(type), "Cannot instantiate null");
        }

        var ci = type.GetConstructor(Type.EmptyTypes);
        if (ci is null)
        {
            ThrowHelper.ThrowArgumentException("Cannot instantiate type which has no empty constructor", type.Name);
        }

        return ci;
    }

    /// <summary>
    /// Sets the object properties using reflection.
    /// </summary>
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
                ThrowHelper.ThrowSchedulerConfigException($"Could not parse property '{name}' into correct data type: {nfe.Message}", nfe);
            }
        }
    }

    /// <summary>
    /// Sets the object properties using reflection.
    /// </summary>
    /// <param name="obj">The object to set values to.</param>
    /// <param name="props">The properties to set to object.</param>
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
                ThrowHelper.ThrowSchedulerConfigException($"Could not parse property '{name}' into correct data type: {nfe.Message}", nfe);
            }
        }
    }

    private static readonly ConcurrentDictionary<(Type ObjectType, string PropertyName), PropertyInfo?> propertyResolutionCache = new();

    public static void SetPropertyValue(object target, string propertyName, object? value)
    {
        var pi = propertyResolutionCache.GetOrAdd((target.GetType(), propertyName), tuple =>
        {
            string name = char.IsLower(tuple.PropertyName[0])
                ? char.ToUpper(tuple.PropertyName[0]) + tuple.PropertyName.Substring(1)
                : tuple.PropertyName;

            Type t = tuple.ObjectType;
            var propertyInfo = t.GetProperty(name);

            if (propertyInfo is null || !propertyInfo.CanWrite)
            {
                // try to find from interfaces
                foreach (var interfaceType in target.GetType().GetInterfaces())
                {
                    propertyInfo = interfaceType.GetProperty(name);
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
            ThrowHelper.ThrowMemberAccessException($"No writable property '{propertyName}' found");
        }

        var mi = pi.GetSetMethod();

        if (mi is null)
        {
            ThrowHelper.ThrowMemberAccessException($"Property '{propertyName}' has no setter");
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
                ThrowHelper.ThrowArgumentOutOfRangeException();
                return default;
        }
    }

    public static bool IsAttributePresent(Type typeToExamine, Type attributeType)
    {
        return typeToExamine.GetCustomAttributes(attributeType, inherit: true).Length > 0;
    }

    public static bool IsAnyInterfaceAttributePresent(Type typeToExamine, Type attributeType)
    {
        if (IsAttributePresent(typeToExamine, attributeType))
        {
            return true;
        }

        foreach (var type in typeToExamine.GetInterfaces())
        {
            if (IsAnyInterfaceAttributePresent(type, attributeType))
            {
                return true;
            }
        }

        return false;
    }
}