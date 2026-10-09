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

using Quartz.Configuration;
using Quartz.Extensibility;

namespace Quartz.Impl;

/// <summary>
/// What a key resolved to: the scheduler, the identity it was found under, and whether it is a window
/// onto an attached store.
/// </summary>
/// <param name="Scheduler">The scheduler the key names.</param>
/// <param name="Ref">
/// The scheduler's own identity — the bare name for a scheduler reached through no target, and
/// <c>target/name</c> otherwise — which is the key as it would be listed, whatever spelling resolved it.
/// </param>
/// <param name="IsWindow">
/// Whether the scheduler is a window onto an attached store, which decides what belongs to one process
/// and so cannot be done through it.
/// </param>
internal sealed record SchedulerResolution(IScheduler Scheduler, SchedulerRef Ref, bool IsWindow);

/// <summary>
/// Turns a <see cref="SchedulerRef" /> into the scheduler it names, by the one rule the dashboard's
/// client and the HTTP API share.
/// </summary>
/// <remarks>
/// <para>
/// A bare key resolves to the one <em>untargeted</em> entry under the name: a scheduler of this process,
/// a window, or an <c>AddQuartzHttpClient</c> registration with no target. There is at most one, because
/// a local name is unique, an untargeted registration is refused per name, and a window is refused for a
/// name this process already has. A bare key never resolves to a targeted proxy, so a deployment that
/// fronts only targets has no bare keys and one that upgrades sees nothing change.
/// </para>
/// <para>
/// A targeted key resolves first to an untargeted entry named exactly like the whole key — a scheduler
/// of this process may contain <c>/</c> — and otherwise to the proxy under the name whose
/// <see cref="IProxyScheduler.Target" /> is the target, or the window whose store was attached under
/// it. Targets are compared ignoring case, the way names are.
/// </para>
/// </remarks>
internal static class SchedulerLookup
{
    /// <summary>
    /// The scheduler <paramref name="key" /> names, or <see langword="null" /> when nothing does.
    /// </summary>
    /// <param name="repository">The container's repository.</param>
    /// <param name="windows">
    /// Which schedulers are windows and onto which store, or <see langword="null" /> in a container that
    /// records none — a bare key then resolves exactly as it always did.
    /// </param>
    /// <param name="key">The key to resolve.</param>
    public static SchedulerResolution? Resolve(ISchedulerRepository repository, SchedulerWindowRegistry? windows, SchedulerRef key)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(key);

        // Exact bare match wins: a scheduler of this process whose name happens to contain '/' is reached
        // by that name, and a key spelled like it cannot mean anything else here. Read says which.
        SchedulerRef read = key.Target is null ? key : Read(repository, key.Key);
        if (read.Target is null)
        {
            return Untargeted(repository, windows, read.SchedulerName);
        }

        foreach (IScheduler scheduler in repository.LookupByName(key.SchedulerName))
        {
            if (scheduler is IProxyScheduler proxy)
            {
                if (string.Equals(proxy.Target, key.Target, StringComparison.OrdinalIgnoreCase))
                {
                    return new SchedulerResolution(scheduler, new SchedulerRef(scheduler.SchedulerName, proxy.Target), IsWindow: false);
                }

                continue;
            }

            string? windowTarget = windows?.TargetOf(scheduler.SchedulerName);
            if (windowTarget is not null && string.Equals(windowTarget, key.Target, StringComparison.OrdinalIgnoreCase))
            {
                return new SchedulerResolution(scheduler, new SchedulerRef(scheduler.SchedulerName, windowTarget), IsWindow: true);
            }
        }

        return null;
    }

    /// <summary>
    /// Reads a key: an untargeted entry bound under exactly this spelling is a bare name, whatever it
    /// contains, and otherwise the key is <c>target/name</c>.
    /// </summary>
    /// <remarks>
    /// The one rule, which <see cref="Resolve" /> applies and every reader that takes a key and asks
    /// something other than the repository — the history lookup, the event lookup — applies too, so that
    /// a scheduler of this process named <c>x/y</c> beside a target <c>x</c> reads its own history and its
    /// own events, as it resolves to itself. With no repository to ask, the key is read as spelled.
    /// </remarks>
    /// <param name="repository">The container's repository, or <see langword="null" /> where there is none.</param>
    /// <param name="key">The key to read.</param>
    public static SchedulerRef Read(ISchedulerRepository? repository, string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        return repository is not null && Untargeted(repository, windows: null, key) is not null
            ? new SchedulerRef(key)
            : SchedulerRef.Parse(key);
    }

    /// <summary>
    /// The targets under which <paramref name="schedulerName" /> is held, sorted — what a refusal of a
    /// bare key names so that the caller can spell the key that would have resolved.
    /// </summary>
    public static List<string> TargetsHolding(ISchedulerRepository repository, string schedulerName)
    {
        ArgumentNullException.ThrowIfNull(repository);

        List<string> targets = [];
        foreach (IScheduler scheduler in repository.LookupByName(schedulerName))
        {
            if (scheduler is IProxyScheduler { Target: { } target })
            {
                targets.Add(target);
            }
        }

        targets.Sort(StringComparer.OrdinalIgnoreCase);
        return targets;
    }

    /// <summary>
    /// What to say about a key nothing resolved: the plain refusal, or — for a bare name that targets
    /// hold — the keys that would have resolved.
    /// </summary>
    public static string NotFoundMessage(ISchedulerRepository repository, SchedulerRef key)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(key);

        if (key.Target is null && TargetsHolding(repository, key.SchedulerName) is { Count: > 0 } targets)
        {
            IEnumerable<string> keys = targets.Select(target => $"'{new SchedulerRef(key.SchedulerName, target).Key}'");
            return $"Scheduler '{key.Key}' was not found under that name alone: it is reached through "
                + $"{(targets.Count == 1 ? "a target" : "targets")} {string.Join(", ", targets.Select(t => $"'{t}'"))}. "
                + $"Use {string.Join(" or ", keys)}.";
        }

        return $"Scheduler '{key.Key}' was not found.";
    }

    private static SchedulerResolution? Untargeted(ISchedulerRepository repository, SchedulerWindowRegistry? windows, string schedulerName)
    {
        foreach (IScheduler scheduler in repository.LookupByName(schedulerName))
        {
            if (scheduler is IProxyScheduler proxy)
            {
                if (proxy.Target is null)
                {
                    return new SchedulerResolution(scheduler, new SchedulerRef(scheduler.SchedulerName), IsWindow: false);
                }

                continue;
            }

            string? windowTarget = windows?.TargetOf(scheduler.SchedulerName);
            return new SchedulerResolution(
                scheduler,
                new SchedulerRef(scheduler.SchedulerName, windowTarget),
                IsWindow: windowTarget is not null);
        }

        return null;
    }
}
