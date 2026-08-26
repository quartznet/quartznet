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
using System.Diagnostics.CodeAnalysis;
using System.Reflection;

using Quartz.Util;

namespace Quartz.Configuration;

/// <summary>
/// Writes a component's properties from configuration keys that name them.
/// </summary>
/// <remarks>
/// <para>
/// This is the seam the flat <c>quartz.*</c> keys cross, and the only place in Quartz that sets a
/// property whose name is a string. Four callers reach it, all of them while the container is being
/// built and none of them on the fire path: <see cref="QuartzPropertyBridge" /> for the leftover keys
/// of a component with no typed options, <see cref="SchedulerPluginFactory" /> for
/// <c>quartz.plugin.&lt;name&gt;.*</c>, <see cref="PropertyListenerFactory" /> for
/// <c>quartz.*.listener.&lt;name&gt;.*</c>, and <c>ConfigurationBasedDbMetadataFactory</c> for
/// <c>quartz.dbprovider.&lt;name&gt;.*</c>.
/// </para>
/// <para>
/// Every member says <see cref="RequiresUnreferencedCodeAttribute" /> by construction rather than
/// being waived in <c>TrimAnalysisBaseline.cs</c>, which is what lets the type carry no baseline entry
/// at all: an API that binds a property by name on a type Quartz is handed at run time cannot be
/// trim-safe, so it says so, and the four callers are where the trimmed application is told.
/// </para>
/// </remarks>
/// <author>Aleksandar Seovic</author>
/// <author>Marko Lahma</author>
internal static class PropertyBinder
{
    private const string BindsByName =
        "Component properties are set by name on a type Quartz is handed at run time; a component named by a quartz.* configuration key, and the properties that key sets, are not guaranteed to survive trimming.";

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

    private static readonly ConcurrentDictionary<(Type ObjectType, string PropertyName), PropertyInfo?> propertyResolutionCache = new();

    /// <summary>
    /// Sets the object properties using reflection.
    /// </summary>
    /// <param name="target">The object to set values to.</param>
    /// <param name="properties">The properties to set to object.</param>
    [RequiresUnreferencedCode(BindsByName)]
    public static void SetObjectProperties(object target, NameValueCollection properties)
    {
        // remove the type
        properties.Remove("type");

        foreach (string name in properties.Keys)
        {
            try
            {
                var value = properties[name];
                SetPropertyValue(target, name, value);
            }
            catch (Exception nfe)
            {
                Throw.SchedulerConfigException($"Could not parse property '{name}' into correct data type: {nfe.Message}", nfe);
            }
        }
    }

    /// <summary>
    /// Sets one named property, finding it on the type itself or on an interface the type implements.
    /// </summary>
    /// <remarks>
    /// The interfaces are searched because a type may implement its settable members explicitly, which
    /// leaves them off the type's own property list — <c>ObjectUtils.SetPropertyValue fails with
    /// explicitly implemented interface members</c> was a 2.0.1 bug fix, and the behaviour has been
    /// depended on since.
    /// </remarks>
    [RequiresUnreferencedCode(BindsByName)]
    private static void SetPropertyValue(object target, string propertyName, object? value)
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
            value = ValueConverter.GetTimeSpanValueForProperty(pi, value);
        }
        else
        {
            value = ValueConverter.ConvertValueIfNecessary(mi.GetParameters()[0].ParameterType, value);
        }

        mi.Invoke(target, [value]);
    }
}
