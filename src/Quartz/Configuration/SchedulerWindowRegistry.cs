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

namespace Quartz.Configuration;

/// <summary>
/// Which schedulers of this container are windows onto somebody else's, and which attached store each
/// of them was discovered in.
/// </summary>
/// <remarks>
/// <para>
/// A window is an ordinary scheduler as far as the repository is concerned — built through
/// <see cref="ISchedulerRuntime.Add" />, bound, and reached by name — so nothing about the object says
/// that this process does not run it. That distinction is a registration fact rather than a property of
/// the scheduler, and this is where it is recorded: one place, so the listing, the health check and the
/// dashboard cannot disagree about what a window is.
/// </para>
/// <para>
/// Empty in every container that never attached a store, which is what makes it free to ask. The
/// dictionary is keyed the way <see cref="Extensibility.ISchedulerRepository" /> indexes names, so a
/// window and a lookup of it cannot differ by case.
/// </para>
/// </remarks>
internal sealed class SchedulerWindowRegistry
{
    private readonly ConcurrentDictionary<string, string> windows = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Records that <paramref name="schedulerName" /> is a window onto <paramref name="target" />.
    /// </summary>
    /// <remarks>
    /// Written after the scheduler has been built and bound, so a name that was refused is never here.
    /// </remarks>
    public void Add(string schedulerName, string target)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(schedulerName);
        ArgumentException.ThrowIfNullOrWhiteSpace(target);

        windows[schedulerName] = target;
    }

    /// <summary>
    /// Forgets a window, as its target is detached or its scheduler removed.
    /// </summary>
    public bool Remove(string schedulerName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(schedulerName);
        return windows.TryRemove(schedulerName, out _);
    }

    /// <summary>
    /// The attached store <paramref name="schedulerName" /> is a window onto, or
    /// <see langword="null" /> when it is a scheduler of this process.
    /// </summary>
    public string? TargetOf(string? schedulerName)
    {
        if (string.IsNullOrWhiteSpace(schedulerName))
        {
            return null;
        }

        return windows.TryGetValue(schedulerName, out string? target) ? target : null;
    }

    /// <summary>
    /// Whether this container holds no windows at all, which is every container that attached no store.
    /// </summary>
    public bool IsEmpty => windows.IsEmpty;
}
