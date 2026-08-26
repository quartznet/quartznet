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

using System.Diagnostics.CodeAnalysis;
using System.Reflection;

namespace Quartz.Util;

/// <summary>
/// Makes an instance of a type that has a public parameterless constructor.
/// </summary>
/// <remarks>
/// The three callers are the ones that hold a <see cref="Type" /> and nothing else: the job factory
/// building the job a trigger fired, the JSON configuration reading
/// <c>quartz.scheduler.typeLoaderType</c>, and <c>DbProvider</c> constructing an ADO.NET driver's own
/// connection and command. Every one of them takes an annotated <see cref="Type" /> and hands the
/// requirement to whoever supplied it, so nothing here is baselined — the requirement is written down
/// rather than waived.
/// </remarks>
internal static class TypeActivator
{
    /// <summary>
    /// Instantiates an instance of the type specified.
    /// </summary>
    public static T Instantiate<T>([DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] Type? type)
    {
        ConstructorInfo ci = GetDefaultConstructor(type);
        return (T) ci.Invoke([]);
    }

    /// <summary>
    /// The type's public parameterless constructor, or a message naming the type that has none.
    /// </summary>
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
}
