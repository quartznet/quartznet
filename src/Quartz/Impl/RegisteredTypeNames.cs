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

using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;

using Quartz.Util;

namespace Quartz.Impl;

/// <summary>
/// The types this process has named in code, keyed by the name Quartz stores them under, so that a
/// name read back out of a job store, a schedule file, a request body or a configuration key can be
/// answered without asking the runtime to find the type by its name.
/// </summary>
/// <remarks>
/// <para>
/// Process-wide, like the <see cref="Type.GetType(string)" /> it stands in front of, and consulted
/// before it everywhere: <see cref="SimpleTypeLoader" />, the name-constructed <see cref="JobType" />,
/// and the store's fallback for a name its loader refuses. Untrimmed, the runtime still gets its say
/// when the table has no answer; trimmed, with <see cref="FeatureSwitches.IsTypeNameResolutionSupported" />
/// off, the table is the whole answer, and a name it does not hold is refused with
/// <see cref="NotRegistered" />.
/// </para>
/// <para>
/// It is fed by every API that takes a job type as a type: the <see cref="JobType(Type)" /> constructor
/// — behind <c>JobBuilder.Create&lt;T&gt;()</c>, <c>OfType&lt;T&gt;()</c>, <c>AddJob&lt;T&gt;()</c>,
/// <c>ScheduleJob&lt;T&gt;()</c> and the implicit conversion — <c>AddJobType&lt;T&gt;()</c>,
/// <c>TypeLoaderOptions.Map</c>, and Quartz's own <see cref="DelegateJob" />, which every delegate job
/// is stored as. A <c>typeof</c> is what each of those takes, and a <c>typeof</c> is what the trimmer
/// follows, so an entry here is a type that survived.
/// </para>
/// <para>
/// The key is <see cref="ObjectExtensions.AssemblyQualifiedNameWithoutVersion" />, which is the spelling
/// <see cref="JobType.FullName" /> writes to <c>JOB_CLASS_NAME</c>. A lookup tolerates the spellings a
/// configuration file may carry instead: a fully qualified name with its version, culture and public key
/// token, and the 3.x namespace and assembly spellings <see cref="SimpleTypeLoader" /> knows the renames
/// for. A name with no assembly part is a miss, as it is for the runtime outside CoreLib. When one key
/// names two loaded types — two assemblies of one simple name, through plugin load contexts — the last
/// registration wins, since it is the one the application made most recently.
/// </para>
/// </remarks>
internal static class RegisteredTypeNames
{
    private static readonly ConcurrentDictionary<string, Type> table = new(StringComparer.Ordinal);

    static RegisteredTypeNames()
    {
        // Stored on every node that ever scheduled a delegate job, and read back on nodes that never did.
        Register(typeof(DelegateJob));
    }

    /// <summary>
    /// Records a type under the name Quartz stores it by.
    /// </summary>
    /// <remarks>
    /// The annotation is the admission the table rests on: a type arrives here through a parameter or a
    /// type argument that declared what Quartz reflects over on a job, so what comes back out of
    /// <see cref="Find(string)" /> was kept for the reflection the fire path does.
    /// </remarks>
    /// <param name="type">The type, as a <c>typeof</c> or an annotated type parameter.</param>
    public static void Register([DynamicallyAccessedMembers(JobTypeMembers.Required)] Type type)
    {
        ArgumentNullException.ThrowIfNull(type);
        table[type.AssemblyQualifiedNameWithoutVersion()] = type;
    }

    /// <summary>
    /// Records a job type the container constructs from its own registration rather than Quartz from
    /// the type's constructors.
    /// </summary>
    /// <remarks>
    /// <c>AddJobType&lt;TJob, TImplementation&gt;()</c> and the factory overloads name a service type
    /// the job factory resolves from the container, which builds the implementation. Quartz constructs
    /// nothing of the service type and binds nothing onto it, so there is no member requirement to
    /// declare — only the type itself, which the <c>typeof</c> the registration takes keeps.
    /// </remarks>
    /// <param name="serviceType">The job type as named on the job detail.</param>
    public static void RegisterServiceType(Type serviceType)
    {
        ArgumentNullException.ThrowIfNull(serviceType);
        table[serviceType.AssemblyQualifiedNameWithoutVersion()] = serviceType;
    }

    /// <summary>
    /// The registered type a name means, or <see langword="null" /> when nothing registered it.
    /// </summary>
    /// <param name="name">The name as stored or configured.</param>
    public static Type? Find(string name) => Find(name, out _);

    /// <summary>
    /// The registered type a name means, and the spelling it was found under, or <see langword="null" />
    /// when nothing registered it.
    /// </summary>
    /// <param name="name">The name as stored or configured.</param>
    /// <param name="foundAs">
    /// The spelling that matched: <paramref name="name" /> itself, or the 4.0 spelling of a 3.x name,
    /// which a loader reports so the configuration can be fixed.
    /// </param>
    public static Type? Find(string name, out string foundAs)
    {
        foundAs = name;

        if (string.IsNullOrEmpty(name))
        {
            return null;
        }

        if (FindExact(name) is { } exact)
        {
            return exact;
        }

        foreach (string candidate in SimpleTypeLoader.LegacyNameCandidates(name))
        {
            if (FindExact(candidate) is { } renamed)
            {
                foundAs = candidate;
                return renamed;
            }
        }

        return null;
    }

    /// <summary>
    /// What a name resolves to when the switch is off and nothing registered it. The one message every
    /// refusal carries, so the way out reads the same from a loader, a <see cref="JobType" /> and the
    /// options validator.
    /// </summary>
    /// <param name="name">The name that was asked for.</param>
    public static string NotRegistered(string name) =>
        $"Type '{name}' is not registered, and this publish does not resolve type names by reflection "
        + $"({FeatureSwitches.TypeNameResolutionSwitch} is false). Register the job type with AddJob<T>() or "
        + "AddJobType<T>(), reference it from JobBuilder.Create<T>(), or map the stored name with "
        + $"UseTypeLoader(o => o.Map(\"{name}\", typeof(T))). Set "
        + $"<{FeatureSwitches.TypeNameResolutionProperty}>true</{FeatureSwitches.TypeNameResolutionProperty}> "
        + "to resolve by reflection again.";

    /// <summary>
    /// One spelling, looked up as given and then without the version, culture and public key token a
    /// fully qualified name carries and the stored spelling does not.
    /// </summary>
    private static Type? FindExact(string name)
    {
        if (table.TryGetValue(name, out Type? type))
        {
            return type;
        }

        string unversioned = ObjectExtensions.WithoutAssemblyVersion(name);
        return !ReferenceEquals(unversioned, name) && table.TryGetValue(unversioned, out type) ? type : null;
    }
}
