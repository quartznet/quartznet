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

using Microsoft.Extensions.DependencyInjection;

using Quartz.Extensibility;

namespace Quartz.Configuration;

/// <summary>
/// Which store holds one scheduler's execution history, decided the same way for every reader that
/// asks by scheduler name — the dashboard's client and the HTTP API's history routes.
/// </summary>
/// <remarks>
/// <para>
/// There are three answers, tried in this order. A <em>window</em> onto an attached store reads its
/// history from the database it is a window onto, because no container registration could be keyed by a
/// name that was discovered rather than registered. A scheduler with a history store of its own — one
/// that called <c>UseExecutionHistory()</c>, or one <c>AddQuartzHttpClient</c> registered, whose reader
/// is keyed by its name — reads that one. Everything else reads the container's shared, unkeyed store,
/// which is where its recorder writes.
/// </para>
/// <para>
/// In core rather than in either reader, because neither of them references the other and the rule has
/// to be one: a scheduler whose history the dashboard found in its own database and the HTTP API looked
/// for in the shared store is a scheduler with two histories depending on who asked.
/// </para>
/// </remarks>
internal static class ExecutionHistoryLookup
{
    /// <summary>
    /// The store that holds <paramref name="schedulerName" />'s history, or <see langword="null" /> when
    /// it is a window whose store keeps no history here, with <paramref name="refusal" /> saying so.
    /// </summary>
    /// <remarks>
    /// Null and a reason rather than a throw, so that each caller refuses in its own layer's terms: the
    /// dashboard's pages read a <see cref="NotSupportedException" />, and the HTTP API answers the
    /// <c>400</c> it answers a scheduler's refusals with. A window with no history is never answered with
    /// the shared store: that is this process's history, and an empty page from it reads as a cluster that
    /// has run nothing.
    /// </remarks>
    /// <param name="services">The container, or a scope of it, that the keyed stores are resolved from.</param>
    /// <param name="attachedStores">
    /// The attached stores, or <see langword="null" /> in a process that attached none through the
    /// dashboard — which is then asked whether the name is a window through
    /// <see cref="SchedulerWindowRegistry" /> alone, and has no window history to offer.
    /// </param>
    /// <param name="shared">The container's unkeyed history store.</param>
    /// <param name="schedulerName">The scheduler whose history is being read.</param>
    /// <param name="refusal">Why there is no store, when there is none.</param>
    public static IExecutionHistoryStore? Find(
        IServiceProvider services,
        AttachedStores? attachedStores,
        IExecutionHistoryStore shared,
        string? schedulerName,
        out string? refusal)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(shared);

        refusal = null;

        string? target = attachedStores is not null
            ? attachedStores.TargetOf(schedulerName)
            : services.GetService<SchedulerWindowRegistry>()?.TargetOf(schedulerName);

        if (target is not null)
        {
            if (attachedStores?.HistoryFor(schedulerName) is { } windowHistory)
            {
                return windowHistory;
            }

            refusal =
                $"The store attached as '{target}', which '{schedulerName}' is a window onto, keeps no execution "
                + "history: it was attached without UseExecutionHistory(), so nothing a node ran was written where "
                + "this process can read it. Add store.UseExecutionHistory() to the nodes and to AttachStore, and run "
                + "database/migrations/4.2/add_execution_history_<dialect>.sql.";
            return null;
        }

        // A container that does not do keyed services holds no per-scheduler store either, so asking it
        // would only be a way to throw.
        if (string.IsNullOrWhiteSpace(schedulerName) || services is not IKeyedServiceProvider keyed)
        {
            return shared;
        }

        return keyed.GetKeyedService(typeof(IExecutionHistoryStore), schedulerName) as IExecutionHistoryStore ?? shared;
    }
}
