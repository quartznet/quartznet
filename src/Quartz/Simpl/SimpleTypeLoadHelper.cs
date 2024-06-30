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

using Quartz.Spi;

namespace Quartz.Simpl;

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

    /// <inheritdoc />
    public void Initialize()
    {
    }

    /// <inheritdoc />
    public Type? LoadType(string? name)
    {
        if (string.IsNullOrEmpty(name) || name is null)
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
            ThrowHelper.ThrowTypeLoadException($"Could not load type '{name}'");
        }
        return type;
    }
}