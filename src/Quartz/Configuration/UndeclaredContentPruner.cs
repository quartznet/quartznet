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

using Quartz.Core;

namespace Quartz.Configuration;

/// <summary>
/// Deletes the stored jobs and triggers in the groups <see cref="SchedulingOptions.PruneUndeclaredInGroups" />
/// owns that no declaration names, once, at a scheduler's first start.
/// </summary>
/// <remarks>
/// <para>
/// Run by <see cref="QuartzScheduler.Start" /> after the plugins have started, so a scheduling file's
/// declarations are recorded, and before the firing loop is unpaused, so this node never fires what it is
/// about to delete. Recovery has run by then, and a recovery trigger of a pruned job goes with the job.
/// </para>
/// <para>
/// Keys only: a stale job whose type no longer loads is deleted without loading it. The reads go through the
/// scheduler and hold no lock, so they never stall another node's acquisition; then one
/// <see cref="IScheduler.DeleteJobs(IReadOnlyCollection{JobKey}, CancellationToken)" /> and one
/// <see cref="IScheduler.UnscheduleJobs(IReadOnlyCollection{TriggerKey}, CancellationToken)" />, each one
/// store transaction. Two nodes starting with the same declarations compute the same keys, and the second
/// finds nothing left to delete.
/// </para>
/// <para>
/// A failure is logged, never thrown: the start goes on with what a start without the option leaves.
/// </para>
/// </remarks>
internal sealed class UndeclaredContentPruner
{
    private readonly IScheduler scheduler;
    private readonly DeclaredContent declared;
    private readonly ILogger logger;

    /// <param name="scheduler">The scheduler's own facade, whose store is pruned.</param>
    /// <param name="declared">What the scheduler's declarations name.</param>
    /// <param name="logger">Where each deletion is reported.</param>
    public UndeclaredContentPruner(IScheduler scheduler, DeclaredContent declared, ILogger logger)
    {
        this.scheduler = scheduler;
        this.declared = declared;
        this.logger = logger;
    }

    /// <summary>
    /// Deletes what the owned groups hold that no declaration names, and closes the record.
    /// </summary>
    public async ValueTask Prune(CancellationToken cancellationToken = default)
    {
        declared.Close();

        try
        {
            if (declared.Unread.Count > 0)
            {
                string unread = string.Join(", ", declared.Unread.Select(static x => $"'{x}'"));
                logger.UndeclaredContentPruneFailed(
                    scheduler.SchedulerName,
                    OwnedGroups(),
                    new SchedulerException(
                        $"The scheduling file(s) {unread} did not load, so what they declare is not known and nothing was pruned. "
                        + "Once the files load, the next start prunes."));
                return;
            }

            await PruneOwnedGroups(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.UndeclaredContentPruneFailed(scheduler.SchedulerName, OwnedGroups(), exception);
        }
    }

    private async ValueTask PruneOwnedGroups(CancellationToken cancellationToken)
    {
        HashSet<string> owned = new(declared.OwnedGroups, StringComparer.Ordinal);

        // (1) The jobs the owned groups hold, and which of them no declaration names. A job in a group a
        // file processor never deletes from is never pruned either.
        HashSet<JobKey> storedOwnedJobs = [];
        Dictionary<JobKey, string> undeclaredJobs = [];
        foreach (string group in declared.OwnedGroups)
        {
            foreach (JobKey job in await scheduler.GetJobKeys(GroupMatcher<JobKey>.GroupEquals(group), cancellationToken).ConfigureAwait(false))
            {
                storedOwnedJobs.Add(job);
                if (!declared.Names(job) && !declared.NeverPrunes(job))
                {
                    undeclaredJobs.TryAdd(job, group);
                }
            }
        }

        // (2) The triggers in the scope: every trigger in an owned group, whatever its job's group, and every
        // trigger of a declared job in an owned group, whatever the trigger's group. Each remembers the owned
        // group that brought it in, which is what its log line names.
        Dictionary<TriggerKey, (JobKey Job, string OwnedGroup)> candidates = [];
        foreach (string group in declared.OwnedGroups)
        {
            TriggerQuery inGroup = new() { Group = GroupMatcher<TriggerKey>.GroupEquals(group), Take = PagedQuery.All };
            Consider(await scheduler.QueryTriggers(inGroup, cancellationToken).ConfigureAwait(false), group, undeclaredJobs, candidates);
        }

        foreach (JobKey job in declared.Jobs)
        {
            if (owned.Contains(job.Group) && storedOwnedJobs.Contains(job))
            {
                TriggerQuery ofJob = new() { Job = job, Take = PagedQuery.All };
                Consider(await scheduler.QueryTriggers(ofJob, cancellationToken).ConfigureAwait(false), job.Group, undeclaredJobs, candidates);
            }
        }

        // (3) A pending manual firing is somebody's request rather than a declaration, so it stays. Its mark
        // is on the trigger's map, which only the trigger itself carries. A trigger gone since the listing is
        // passed over.
        List<TriggerKey> prunable = [];
        if (candidates.Count > 0)
        {
            foreach (ITrigger trigger in await scheduler.GetTriggers(candidates.Keys.ToList(), cancellationToken).ConfigureAwait(false))
            {
                if (!ManualFiring.IsMarked(trigger))
                {
                    prunable.Add(trigger.Key);
                }
            }
        }

        // (4) Every read is done, so a failure above deleted nothing. The jobs go first, their triggers with
        // them, and then the triggers left.
        if (undeclaredJobs.Count > 0)
        {
            List<JobKey> deleted = await scheduler.DeleteJobs(undeclaredJobs.Keys.ToList(), cancellationToken).ConfigureAwait(false);
            foreach (JobKey job in deleted)
            {
                logger.UndeclaredJobDeleted(scheduler.SchedulerName, job, undeclaredJobs[job]);
            }
        }

        if (prunable.Count > 0)
        {
            List<TriggerKey> unscheduled = await scheduler.UnscheduleJobs(prunable, cancellationToken).ConfigureAwait(false);
            foreach (TriggerKey trigger in unscheduled)
            {
                (JobKey job, string group) = candidates[trigger];
                logger.UndeclaredTriggerUnscheduled(scheduler.SchedulerName, trigger, job, group);
            }
        }
    }

    /// <summary>
    /// Adds the triggers of a listing that no declaration names to the candidates, less those that go with
    /// an undeclared job and those in a group the prune never touches.
    /// </summary>
    private void Consider(
        PagedResult<TriggerHeader> listing,
        string ownedGroup,
        Dictionary<JobKey, string> undeclaredJobs,
        Dictionary<TriggerKey, (JobKey Job, string OwnedGroup)> candidates)
    {
        foreach (TriggerHeader header in listing.Items)
        {
            if (declared.Names(header.Key) || declared.NeverPrunes(header.Key) || undeclaredJobs.ContainsKey(header.JobKey))
            {
                continue;
            }

            candidates.TryAdd(header.Key, (header.JobKey, ownedGroup));
        }
    }

    private string OwnedGroups()
    {
        return string.Join(", ", declared.OwnedGroups.Select(static x => $"'{x}'"));
    }
}
