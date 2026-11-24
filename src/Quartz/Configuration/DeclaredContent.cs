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
using Quartz.Impl;

namespace Quartz.Configuration;

/// <summary>
/// Every job and trigger key the declarations name, recorded as they are applied: for the prune
/// <see cref="SchedulingOptions.PruneUndeclaredInGroups" /> asks for at the first start, and for whether a
/// change to a stored trigger lasts past the next start (#4412).
/// </summary>
/// <remarks>
/// <para>
/// One per scheduler the container builds, on its <see cref="Core.QuartzSchedulerResources" />. Code
/// declarations are recorded as the scheduler is built, a scheduling file's as its plugin starts; both go
/// through <see cref="Xml.XmlSchedulingDataProcessor.ScheduleJobs" />.
/// </para>
/// <para>
/// A key counts whether or not it was stored: a key <see cref="SchedulingOptions.IgnoreDuplicates" /> passed
/// over, or a trigger switched off with <c>-</c>, is still the declarations'. The first start closes the
/// record, after the prune when there is one, and what a later rescan of a file adds is not recorded.
/// </para>
/// </remarks>
internal sealed class DeclaredContent
{
    private readonly Lock gate = new();
    private readonly HashSet<JobKey> jobs = [];
    private readonly HashSet<TriggerKey> triggers = [];
    private readonly HashSet<TriggerKey> storedOverAtStart = [];
    private readonly HashSet<string> neverPrunedJobGroups = new(StringComparer.Ordinal);
    private readonly HashSet<string> neverPrunedTriggerGroups = new(StringComparer.Ordinal);
    private readonly List<string> unread = [];
    private readonly bool storePersists;
    private bool closed;

    private DeclaredContent(string[] ownedGroups, bool storePersists)
    {
        OwnedGroups = ownedGroups;
        this.storePersists = storePersists;
    }

    /// <summary>
    /// The groups the declarations own, each once, in the order they were listed; none when the option lists
    /// none or the store keeps nothing across starts.
    /// </summary>
    public IReadOnlyList<string> OwnedGroups { get; }

    /// <summary>
    /// Whether the first start prunes the owned groups: the option lists a group, and the store keeps what it
    /// holds across starts.
    /// </summary>
    public bool Prunes => OwnedGroups.Count > 0;

    /// <summary>
    /// The record a scheduler keeps.
    /// </summary>
    public static DeclaredContent For(SchedulingOptions scheduling, IJobStore jobStore)
    {
        bool storePersists = jobStore.SupportsPersistence;
        string[] ownedGroups = storePersists ? scheduling.PruneUndeclaredInGroups.Distinct(StringComparer.Ordinal).ToArray() : [];
        return new DeclaredContent(ownedGroups, storePersists);
    }

    /// <summary>
    /// The record of the scheduler a declaration is applied to, or <see langword="null" /> when it keeps none.
    /// </summary>
    /// <remarks>
    /// Every processor and plugin is handed the scheduler's own facade, so a scheduler of somebody else's
    /// is one that records nothing.
    /// </remarks>
    public static DeclaredContent? Of(IScheduler scheduler)
    {
        return scheduler is StdScheduler std ? std.scheduler.resources.DeclaredContent : null;
    }

    /// <summary>
    /// The jobs the declarations name, once the record is closed.
    /// </summary>
    public IReadOnlyCollection<JobKey> Jobs => jobs;

    /// <summary>
    /// Where a declaration could not be read, so what it names is not known; nothing is pruned then.
    /// </summary>
    public IReadOnlyList<string> Unread => unread;

    /// <summary>
    /// Records keys a declaration names, which stores nothing over what the store already holds.
    /// </summary>
    public void Declare(IEnumerable<JobKey> jobKeys, IEnumerable<TriggerKey> triggerKeys)
    {
        Declare(jobKeys, triggerKeys, storesOverStored: false);
    }

    /// <summary>
    /// Records keys a declaration names.
    /// </summary>
    /// <param name="jobKeys">The jobs it names.</param>
    /// <param name="triggerKeys">The triggers it names.</param>
    /// <param name="storesOverStored">
    /// Whether it stores its triggers over the ones the store holds under the same keys, as
    /// <see cref="SchedulingOptions.OverwriteExistingData" /> does; it does so at every start, so a change made
    /// to one of them in between lasts until the next.
    /// </param>
    public void Declare(IEnumerable<JobKey> jobKeys, IEnumerable<TriggerKey> triggerKeys, bool storesOverStored)
    {
        lock (gate)
        {
            if (closed)
            {
                return;
            }

            jobs.UnionWith(jobKeys);
            foreach (TriggerKey triggerKey in triggerKeys)
            {
                triggers.Add(triggerKey);
                if (storesOverStored)
                {
                    storedOverAtStart.Add(triggerKey);
                }
            }
        }
    }

    /// <summary>
    /// Whether the next start stores the declared trigger again, over a change made to it since: a declaration
    /// that stores over what is stored names it, or the store keeps nothing across starts and a declaration names
    /// it at all.
    /// </summary>
    /// <remarks>
    /// Read while declarations may still be recorded, by a scheduler that has not started yet, so under the lock.
    /// </remarks>
    public bool StoresAgainAtNextStart(TriggerKey key)
    {
        lock (gate)
        {
            return storePersists ? storedOverAtStart.Contains(key) : triggers.Contains(key);
        }
    }

    /// <summary>
    /// Records the groups a scheduling file's processor never deletes from, which are never pruned either:
    /// the file plugins keep their scan jobs there.
    /// </summary>
    public void NeverPrune(IEnumerable<string> jobGroups, IEnumerable<string> triggerGroups)
    {
        lock (gate)
        {
            if (closed)
            {
                return;
            }

            neverPrunedJobGroups.UnionWith(jobGroups);
            neverPrunedTriggerGroups.UnionWith(triggerGroups);
        }
    }

    /// <summary>
    /// Records that a declaration could not be read, so the prune deletes nothing at this start.
    /// </summary>
    /// <param name="source">
    /// What could not be read and why, as the prune's log line names it: a scheduling file's name, quoted, and
    /// <c>(not found)</c> when it was not there.
    /// </param>
    public void CouldNotRead(string source)
    {
        lock (gate)
        {
            if (!closed)
            {
                unread.Add(source);
            }
        }
    }

    /// <summary>
    /// Ends the recording: what is recorded afterwards is dropped, and the members that read the record
    /// without the lock may be used.
    /// </summary>
    public void Close()
    {
        lock (gate)
        {
            closed = true;
        }
    }

    /// <summary>
    /// Whether a declaration names the job.
    /// </summary>
    public bool Names(JobKey key) => jobs.Contains(key);

    /// <summary>
    /// Whether a declaration names the trigger.
    /// </summary>
    public bool Names(TriggerKey key) => triggers.Contains(key);

    /// <summary>
    /// Whether the job is in a group the prune never touches.
    /// </summary>
    public bool NeverPrunes(JobKey key) => neverPrunedJobGroups.Contains(key.Group);

    /// <summary>
    /// Whether the trigger is in a group the prune never touches: one a scheduling file's processor never
    /// deletes from, or one that holds the scheduler's own firings.
    /// </summary>
    public bool NeverPrunes(TriggerKey key) => neverPrunedTriggerGroups.Contains(key.Group) || HoldsTheSchedulersOwnFirings(key.Group);

    /// <summary>
    /// Why <paramref name="group" /> may not be owned, or <see langword="null" /> when it may.
    /// </summary>
    /// <remarks>
    /// <c>*</c> is refused as <see cref="QuartzSchedulerOptions.ServedExecutionGroups" /> refuses it: it can
    /// be allowed later, and removing it could not be done.
    /// </remarks>
    public static string? RefusalReason(string? group)
    {
        if (string.IsNullOrWhiteSpace(group))
        {
            return "a blank group name, which names no group";
        }

        if (string.Equals(group, "*", StringComparison.Ordinal))
        {
            return "'*', which would own every group, an operator's included";
        }

        if (HoldsTheSchedulersOwnFirings(group))
        {
            return $"'{group}', a group that holds the scheduler's own firings (recovery, fail-over, one-off "
                   + "ScheduleJob, RunAtStartup or a backfill), which no declaration names";
        }

        return null;
    }

    /// <summary>
    /// Whether the scheduler stores its own firings in <paramref name="group" />: recovery, fail-over, the
    /// one-call <c>ScheduleJob</c> overloads, <c>RunAtStartup</c>, and backfills.
    /// </summary>
    /// <remarks>
    /// Such a trigger is never pruned, even as a trigger of a declared job in an owned group: it is no
    /// declaration's, and deleting it would drop a recovery, a start-up run or a backfill slot.
    /// </remarks>
    private static bool HoldsTheSchedulersOwnFirings(string group)
    {
        return group is SchedulerConstants.DefaultRecoveryGroup
                   or SchedulerConstants.DefaultFailOverGroup
                   or SchedulerConstants.ScheduledJobGroup
                   or SchedulerConstants.StartupGroup
               || group.StartsWith(SchedulerConstants.BackfillGroupPrefix, StringComparison.Ordinal);
    }
}
