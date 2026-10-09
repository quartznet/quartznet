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

using Quartz.Extensibility;

namespace Quartz.Configuration;

/// <summary>
/// Every database this process has been pointed at, and the one place a window's history is asked for.
/// </summary>
/// <remarks>
/// A singleton of the application's container, so that the discovery loop, the pages and the listing
/// are talking about the same targets. Empty in every container that attached none, which is what makes
/// it free to resolve.
/// </remarks>
internal sealed class AttachedStores : IAsyncDisposable
{
    private readonly List<AttachedStore> stores = [];
    private readonly SchedulerWindowRegistry registry;
    private readonly SchedulerTargets targets;

    public AttachedStores(SchedulerWindowRegistry registry, SchedulerTargets targets)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(targets);
        this.registry = registry;
        this.targets = targets;
    }

    /// <summary>
    /// The targets, in the order they were attached.
    /// </summary>
    public IReadOnlyList<AttachedStore> Stores => stores;

    /// <summary>
    /// Which schedulers are windows and onto which store, for a reader resolving a key.
    /// </summary>
    public SchedulerWindowRegistry Windows => registry;

    /// <summary>
    /// Takes ownership of an attached store, which is disposed with this.
    /// </summary>
    /// <remarks>
    /// The store's name is claimed in the container's target registry, so that a store and an HTTP
    /// target cannot share one: a name is half of every key, and the registry is where every kind of
    /// target is held to that.
    /// </remarks>
    /// <exception cref="SchedulerConfigException">A target of any kind already has that name.</exception>
    public void Add(AttachedStore store)
    {
        ArgumentNullException.ThrowIfNull(store);

        targets.Add(new SchedulerTarget
        {
            Name = store.Target,
            Origin = SchedulerOrigin.Window,

            // A database carries a schedule, not a feed of what happened to it, so no events; and the
            // history it carries when the cluster writes one there. Null when the recipe did not ask
            // for it, which the lookup reports rather than answering with this process's own history.
            History = (_, _) => store.History,
        });

        stores.Add(store);
    }

    /// <summary>
    /// The history of the target <paramref name="schedulerName" /> is a window onto, or
    /// <see langword="null" /> when it is not a window or its target keeps no history here.
    /// </summary>
    /// <remarks>
    /// Null rather than an empty store: a window whose cluster does not call
    /// <c>UseExecutionHistory()</c> has no history in this database at all, and saying so is the page's
    /// job. Answering with this process's own history instead would show an empty page that looks like
    /// a cluster which has run nothing.
    /// </remarks>
    public IExecutionHistoryStore? HistoryFor(string? schedulerName)
    {
        if (registry.TargetOf(schedulerName) is not { } target)
        {
            return null;
        }

        foreach (AttachedStore store in stores)
        {
            if (string.Equals(store.Target, target, StringComparison.OrdinalIgnoreCase))
            {
                return store.History;
            }
        }

        return null;
    }

    /// <summary>
    /// Whether <paramref name="schedulerName" /> is a window onto an attached store.
    /// </summary>
    public bool IsWindow(string? schedulerName) => registry.TargetOf(schedulerName) is not null;

    /// <summary>
    /// The name the store <paramref name="schedulerName" /> is a window onto was attached under, or
    /// <see langword="null" /> when it is not a window.
    /// </summary>
    public string? TargetOf(string? schedulerName) => registry.TargetOf(schedulerName);

    public async ValueTask DisposeAsync()
    {
        foreach (AttachedStore store in stores)
        {
            targets.Remove(store.Target);
            await store.DisposeAsync().ConfigureAwait(false);
        }

        stores.Clear();
    }
}
