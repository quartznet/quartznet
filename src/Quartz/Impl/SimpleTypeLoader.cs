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
using Microsoft.Extensions.Options;

using Quartz.Diagnostics;
using Quartz.Extensibility;
using Quartz.Util;

namespace Quartz.Impl;

/// <summary>
/// A <see cref="ITypeLoader" /> that simply calls <see cref="Type.GetType(string)" />.
/// </summary>
/// <seealso cref="ITypeLoader" />
/// <author>James House</author>
/// <author>Marko Lahma (.NET)</author>
internal sealed class SimpleTypeLoader : ITypeLoader
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
        ("Quartz.Impl.Redis.", "Quartz.Extensions.Redis."),
    ];

    /// <summary>
    /// Types that were renamed in 4.0. The job stores are here because <c>quartz.jobStore.type</c> is
    /// the one type name almost every persistent configuration spells out.
    /// </summary>
    /// <remarks>
    /// The lock handlers were renamed twice — once out of 3.x's three spellings of the same idea, and
    /// again when "semaphore" gave way to "lock handler" — so each of them has two entries: a 3.x
    /// configuration file and a 4.0 alpha one both name a type nothing is called any more, and both have
    /// to keep resolving. Each entry names the type as it is called today rather than chaining, because
    /// the rewrite is one pass.
    /// </remarks>
    private static readonly (string Old, string New)[] renamedTypes =
    [
        ("Quartz.Impl.AdoJobStore.JobStoreTX", "Quartz.Impl.AdoJobStore.LocalTransactionJobStore"),
        ("Quartz.Impl.AdoJobStore.JobStoreCMT", "Quartz.Impl.AdoJobStore.ExternalTransactionJobStore"),
        ("Quartz.Impl.AdoJobStore.StdRowLockSemaphore", "Quartz.Impl.AdoJobStore.SelectForUpdateLockHandler"),
        ("Quartz.Impl.AdoJobStore.SelectForUpdateSemaphore", "Quartz.Impl.AdoJobStore.SelectForUpdateLockHandler"),
        ("Quartz.Impl.AdoJobStore.PostgreSQLRowLockSemaphore", "Quartz.Impl.AdoJobStore.PostgreSqlSelectForUpdateLockHandler"),
        ("Quartz.Impl.AdoJobStore.PostgreSqlSelectForUpdateSemaphore", "Quartz.Impl.AdoJobStore.PostgreSqlSelectForUpdateLockHandler"),
        ("Quartz.Impl.AdoJobStore.UpdateLockRowSemaphore", "Quartz.Impl.AdoJobStore.UpdateRowLockHandler"),
        ("Quartz.Impl.AdoJobStore.UpdateRowSemaphore", "Quartz.Impl.AdoJobStore.UpdateRowLockHandler"),
        ("Quartz.Impl.AdoJobStore.UpdateLockRowSemaphoreMOT", "Quartz.Impl.AdoJobStore.SqlServerMemoryOptimizedUpdateRowLockHandler"),
        ("Quartz.Impl.AdoJobStore.SqlServerMemoryOptimizedUpdateRowSemaphore", "Quartz.Impl.AdoJobStore.SqlServerMemoryOptimizedUpdateRowLockHandler"),
        ("Quartz.Impl.AdoJobStore.SimpleSemaphore", "Quartz.Impl.AdoJobStore.InProcessLockHandler"),
        ("Quartz.Impl.AdoJobStore.SQLiteSemaphore", "Quartz.Impl.AdoJobStore.SqliteLockHandler"),
        ("Quartz.Extensions.Redis.RedisSemaphore", "Quartz.Extensions.Redis.RedisLockHandler"),
        ("Quartz.Impl.HostnameInstanceIdGenerator", "Quartz.Impl.HostNameInstanceIdGenerator"),
        ("Quartz.Impl.SimpleTypeLoadHelper", "Quartz.Impl.SimpleTypeLoader"),
        ("Quartz.Plugins.Xml.XMLSchedulingDataProcessorPlugin", "Quartz.Plugins.Xml.XmlSchedulingDataProcessorPlugin"),
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

    private readonly ILogger<SimpleTypeLoader> logger;

    /// <summary>
    /// The application's own renames, in the same shape as <see cref="renamedTypes" /> and matched by
    /// the same rule, because they are the same kind of fact about the same kind of string.
    /// </summary>
    private readonly (string Old, string New)[] aliases;

    /// <param name="logger">
    /// Where a legacy type name that had to be rewritten is reported. The container fills this in; a
    /// loader constructed by hand — every plugin that builds its own — reads
    /// <see cref="LogProvider" />, as before.
    /// </param>
    /// <param name="options">
    /// The application's declared renames. A loader constructed by hand has none, which is what every
    /// caller outside the container wants: those resolve Quartz's own type names.
    /// </param>
    public SimpleTypeLoader(ILogger<SimpleTypeLoader>? logger = null, IOptions<TypeLoaderOptions>? options = null)
    {
        this.logger = logger ?? LogProvider.CreateLogger<SimpleTypeLoader>();

        // Read here rather than per lookup, so a bad alias is a failure to build the loader — which is a
        // failure to build the scheduler — rather than a TypeLoadException on the first job that needs it.
        aliases = DeclaredAliases(options?.Value);
    }

    /// <inheritdoc />
    public Type? LoadType(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return null;
        }
        Type? type = LoadDeclaredAlias(name) ?? Type.GetType(name, false) ?? LoadLegacyName(name);
        if (type is null)
        {
            Throw.TypeLoadException($"Could not load type '{name}'");
        }
        return type;
    }

    /// <summary>
    /// Whether a name resolves to a type, without the <see cref="TypeLoadException" />
    /// <see cref="LoadType" /> throws when it does not.
    /// </summary>
    /// <remarks>
    /// What <c>TypeLoaderOptionsValidator</c> asks of an alias's target at startup. It lives here so
    /// that resolving a type from a string stays in the one type whose contract that is — the trim
    /// analyzer's <c>IL2057</c> is recorded against this type and nothing else — and so that a target
    /// may itself be spelled with a pre-4.0 name.
    /// </remarks>
    internal static bool CanResolve(string name)
    {
        if (Type.GetType(name, false) is not null)
        {
            return true;
        }

        foreach (string candidate in LegacyNameCandidates(name))
        {
            if (Type.GetType(candidate, false) is not null)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// The renames the application declared, dropping the entries that say nothing.
    /// </summary>
    /// <remarks>
    /// A blank alias or a blank target is refused by <c>TypeLoaderOptionsValidator</c>, which is where
    /// it is reported; skipping it here as well is what keeps a loader built without validation — one
    /// constructed by hand, with options handed to it directly — from matching a blank alias against
    /// every name it is ever asked for.
    /// </remarks>
    private static (string Old, string New)[] DeclaredAliases(TypeLoaderOptions? options)
    {
        if (options is null || options.Aliases.Count == 0)
        {
            return [];
        }

        List<(string Old, string New)> declared = [];
        foreach ((string alias, string? target) in options.Aliases)
        {
            if (!string.IsNullOrWhiteSpace(alias) && !string.IsNullOrWhiteSpace(target))
            {
                declared.Add((alias, target));
            }
        }

        return declared.ToArray();
    }

    /// <summary>
    /// Resolves a name the application declared an alias for, before the runtime is asked at all.
    /// </summary>
    /// <remarks>
    /// An alias states what a stored name means <em>now</em>, so it holds even where the old name would
    /// still resolve — a shim class left behind, or the old assembly still deployed beside the new one
    /// mid-rollout. Nothing is written back: the row keeps the spelling it was stored with, and the SQL
    /// <c>UPDATE</c> in the troubleshooting page stays the way to retire an alias.
    /// </remarks>
    private Type? LoadDeclaredAlias(string name)
    {
        if (aliases.Length == 0)
        {
            return null;
        }

        List<string> candidates = [];
        AddRenames(candidates, name, aliases);

        foreach (string candidate in candidates)
        {
            // The target is a type name like any other, so Quartz's own renames apply to it too: an
            // alias may point at a type this application still spells the 3.x way.
            Type? type = Type.GetType(candidate, false) ?? LoadLegacyName(candidate);
            if (type is not null)
            {
                logger.TypeFoundUnderDeclaredAlias(name, candidate);
                return type;
            }
        }

        return null;
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
                logger.TypeFoundUnderNewName(name, candidate);
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
        // named through any of them.
        int namespaceCandidateCount = candidates.Count;
        for (int i = 0; i < namespaceCandidateCount; i++)
        {
            AddRenames(candidates, candidates[i], renamedTypes);
        }

        // The first entry is the name exactly as configured, which the caller has already tried.
        candidates.RemoveAt(0);
        return candidates;
    }

    /// <summary>
    /// Adds what a rename table makes of one candidate name.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A table entry matches the whole name or the part of it before the comma that starts the assembly,
    /// which is what lets one entry cover every spelling of the assembly after it. The comma test is
    /// also what keeps <c>JobStoreTXSomething</c> from matching <c>JobStoreTX</c>.
    /// </para>
    /// <para>
    /// A replacement that names its own assembly stands on its own, so what followed the old name — its
    /// assembly, and any version or public key after that — is dropped rather than carried over onto a
    /// type that lives somewhere else now. Quartz's own tables never name one, since every rename in
    /// them stays inside the assembly it was already in; an application's alias usually does.
    /// </para>
    /// </remarks>
    private static void AddRenames(List<string> candidates, string candidate, (string Old, string New)[] table)
    {
        foreach (var (oldName, newName) in table)
        {
            if (!candidate.StartsWith(oldName, StringComparison.Ordinal)
                || (candidate.Length != oldName.Length && candidate[oldName.Length] != ','))
            {
                continue;
            }

            candidates.Add(newName.Contains(',', StringComparison.Ordinal)
                ? newName
                : string.Concat(newName, candidate.AsSpan(oldName.Length)));
        }
    }
}