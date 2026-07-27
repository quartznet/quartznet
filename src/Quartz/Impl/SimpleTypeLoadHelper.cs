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
        if (type is null && name.EndsWith(QuartzAssemblyTypePostfix, StringComparison.Ordinal))
        {
            // we've moved jobs to new assembly try that too
            var newName = string.Concat(name.AsSpan(0, name.Length - QuartzAssemblyTypePostfix.Length), QuartzJobsAssemblyTypePostfix);
            type = Type.GetType(newName);
        }
        if (type is null)
        {
            type = LoadRenamed(name);
        }
        if (type is null)
        {
            Throw.TypeLoadException($"Could not load type '{name}'");
        }
        return type;
    }

    /// <summary>
    /// Resolves a type whose namespace was renamed in 4.0, warning so the configuration can be fixed.
    /// </summary>
    private Type? LoadRenamed(string name)
    {
        foreach (var (oldNamespace, newNamespace) in renamedNamespaces)
        {
            if (!name.StartsWith(oldNamespace, StringComparison.Ordinal))
            {
                continue;
            }

            var renamed = string.Concat(newNamespace, name.AsSpan(oldNamespace.Length));
            var type = Type.GetType(renamed, false);
            if (type is not null)
            {
                logger.LogWarning(
                    "Type '{OldName}' was found as '{NewName}'; the namespace was renamed in Quartz 4.0. " +
                    "Update the configuration, as this fallback will not last forever.",
                    name, renamed);
                return type;
            }
        }

        return null;
    }
}