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

using Microsoft.Extensions.Logging;

using Quartz.Diagnostics;
using Quartz.Extensibility;

namespace Quartz.Impl;

/// <summary>
/// A <see cref="ITypeLoadHelper" /> that simply calls <see cref="Type.GetType(string)" />.
/// </summary>
/// <seealso cref="ITypeLoadHelper" />
/// <author>James House</author>
/// <author>Marko Lahma (.NET)</author>
internal sealed class SimpleTypeLoadHelper : ITypeLoadHelper
{
    private const string QuartzAssemblyTypePostfix = ", Quartz";
    private const string QuartzJobsAssemblyTypePostfix = ", Quartz.Jobs";

    /// <summary>
    /// Namespaces that were renamed in 4.0. Configuration names types by string, so a rename that
    /// the compiler cannot see would otherwise fail at startup with nothing to point at.
    /// </summary>
    private static readonly (string Old, string New)[] renamedNamespaces =
    [
        ("Quartz.Spi.", "Quartz.Extensibility."),
        ("Quartz.Simpl.", "Quartz.Impl."),
        ("Quartz.Job.", "Quartz.Jobs."),
        ("Quartz.Plugin.", "Quartz.Plugins."),
        ("Quartz.Listener.", "Quartz.Listeners."),
    ];

    /// <summary>
    /// Types that were renamed in 4.0. The job stores are here because <c>quartz.jobStore.type</c> is
    /// the one type name almost every persistent configuration spells out.
    /// </summary>
    private static readonly (string Old, string New)[] renamedTypes =
    [
        ("Quartz.Impl.AdoJobStore.JobStoreTX", "Quartz.Impl.AdoJobStore.LocalTransactionJobStore"),
        ("Quartz.Impl.AdoJobStore.JobStoreCMT", "Quartz.Impl.AdoJobStore.ExternalTransactionJobStore"),
        ("Quartz.Impl.HostnameInstanceIdGenerator", "Quartz.Impl.HostNameInstanceIdGenerator"),
    ];

    /// <summary>
    /// Assemblies that were merged into the core Quartz package in 4.0. A configuration string that
    /// still names one of them has to be retried against the core assembly, on top of any namespace
    /// rename, or the fallback would rewrite the namespace and then fail on the dead assembly.
    /// </summary>
    private static readonly string[] mergedAssemblyPostfixes =
    [
        ", Quartz.Extensions.DependencyInjection",
        ", Quartz.Extensions.Hosting",
        ", Quartz.Serialization.SystemTextJson",
    ];

    private readonly ILogger<SimpleTypeLoadHelper> logger = LogProvider.CreateLogger<SimpleTypeLoadHelper>();

    /// <inheritdoc />
    public Type? LoadType(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return null;
        }
        var type = Type.GetType(name, false);
        if (type is null)
        {
            type = LoadLegacyName(name);
        }
        if (type is null)
        {
            Throw.TypeLoadException($"Could not load type '{name}'");
        }
        return type;
    }

    /// <summary>
    /// Resolves a type whose namespace or assembly changed, warning so the configuration can be fixed.
    /// </summary>
    private Type? LoadLegacyName(string name)
    {
        foreach (string candidate in LegacyNameCandidates(name))
        {
            var type = Type.GetType(candidate, false);
            if (type is not null)
            {
                logger.LogWarning(
                    "Type '{OldName}' was found as '{NewName}'; the type moved in Quartz 4.0. " +
                    "Update the configuration, as this fallback will not last forever.",
                    name, candidate);
                return type;
            }
        }

        return null;
    }

    /// <summary>
    /// Every name the configured string could mean today: the assembly moves (jobs split out of the
    /// core assembly; satellite assemblies merged into it) composed with the namespace and type
    /// renames.
    /// </summary>
    private static List<string> LegacyNameCandidates(string name)
    {
        List<string> candidates = [name];

        if (name.EndsWith(QuartzAssemblyTypePostfix, StringComparison.Ordinal))
        {
            // we've moved jobs to new assembly, try that too
            candidates.Add(string.Concat(name.AsSpan(0, name.Length - QuartzAssemblyTypePostfix.Length), QuartzJobsAssemblyTypePostfix));
        }
        else
        {
            foreach (string mergedPostfix in mergedAssemblyPostfixes)
            {
                if (name.EndsWith(mergedPostfix, StringComparison.Ordinal))
                {
                    candidates.Add(string.Concat(name.AsSpan(0, name.Length - mergedPostfix.Length), QuartzAssemblyTypePostfix));
                    break;
                }
            }
        }

        int assemblyCandidateCount = candidates.Count;
        for (int i = 0; i < assemblyCandidateCount; i++)
        {
            foreach (var (oldNamespace, newNamespace) in renamedNamespaces)
            {
                if (candidates[i].StartsWith(oldNamespace, StringComparison.Ordinal))
                {
                    candidates.Add(string.Concat(newNamespace, candidates[i].AsSpan(oldNamespace.Length)));
                }
            }
        }

        // Applied last, over every assembly and namespace spelling, because a renamed type can be
        // named through any of them. The comma test keeps `JobStoreTXSomething` from matching.
        int namespaceCandidateCount = candidates.Count;
        for (int i = 0; i < namespaceCandidateCount; i++)
        {
            foreach (var (oldType, newType) in renamedTypes)
            {
                string candidate = candidates[i];
                if (candidate.StartsWith(oldType, StringComparison.Ordinal)
                    && (candidate.Length == oldType.Length || candidate[oldType.Length] == ','))
                {
                    candidates.Add(string.Concat(newType, candidate.AsSpan(oldType.Length)));
                }
            }
        }

        // The first entry is the name exactly as configured, which the caller has already tried.
        candidates.RemoveAt(0);
        return candidates;
    }
}