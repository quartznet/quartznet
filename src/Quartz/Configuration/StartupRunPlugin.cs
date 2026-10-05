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

using System.Globalization;
using System.Security.Cryptography;
using System.Text;

using Quartz.Extensibility;

namespace Quartz.Configuration;

/// <summary>
/// Schedules one run of a job each time its scheduler starts: what
/// <see cref="QuartzBuilderExtensions.RunAtStartup" /> registers.
/// </summary>
/// <remarks>
/// <para>
/// A plugin, because <see cref="ISchedulerPlugin.Start" /> is the one hook that runs on a scheduler's
/// first start and on no other: not when the scheduler is built, which is when registered jobs and
/// triggers are stored, and not when it leaves standby. It runs after the store has recovered what the
/// last run of this node left, and before the scheduler thread starts.
/// </para>
/// <para>
/// The run is an ordinary stored trigger rather than a call made in this process, so everything that
/// applies to a firing applies to it — concurrency, listeners, history, and recovery of a firing a crash
/// interrupted. Its name is new on every start and begins with the node that started, so a start can
/// find what an earlier start of the same node left unfired and replace it rather than add to it.
/// </para>
/// </remarks>
internal sealed class StartupRunPlugin : ISchedulerPlugin
{
    /// <summary>
    /// The longest node tag a trigger name carries as it is: the narrowest <c>TRIGGER_NAME</c> a shipped
    /// schema has is 150, and the name adds a dot and 32 hexadecimal digits.
    /// </summary>
    private const int MaxNodeTagLength = 150 - 33;

    private readonly JobKey jobKey;
    private IScheduler? scheduler;

    public StartupRunPlugin(JobKey jobKey)
    {
        this.jobKey = jobKey;
    }

    public ValueTask Initialize(string pluginName, IScheduler scheduler, CancellationToken cancellationToken = default)
    {
        this.scheduler = scheduler;
        return default;
    }

    /// <exception cref="SchedulerException">The job is not stored.</exception>
    public async ValueTask Start(CancellationToken cancellationToken = default)
    {
        IScheduler target = scheduler ?? throw new InvalidOperationException("The plugin was started before it was initialized.");

        if (!await target.Exists(jobKey, cancellationToken).ConfigureAwait(false))
        {
            Throw.SchedulerException(
                $"RunAtStartup names job '{jobKey}', which is not stored. Store it durably, or give it a trigger of its own.");
        }

        SchedulerMetadata metadata = await target.GetMetadata(cancellationToken).ConfigureAwait(false);
        string instanceId = metadata.SchedulerInstanceId;
        bool clustered = metadata.JobStoreClustered;
        ITrigger trigger = CreateTrigger(jobKey, instanceId, clustered, target.TimeProvider);

        // Stored before the leftovers go, so a non-durable job whose only trigger is a leftover is not
        // deleted as an orphan in between. Nothing on this node can fire either yet: its thread has not
        // started, and no other node acquires a trigger pinned to a node that is checking in.
        await target.ScheduleJob(trigger, cancellationToken: cancellationToken).ConfigureAwait(false);
        await RemoveLeftovers(target, instanceId, clustered, trigger.Key, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Unschedules the startup triggers of this job that an earlier start of this node left unfired — a
    /// crash before they fired — so this start's run replaces them rather than adding to them.
    /// </summary>
    /// <remarks>
    /// In a cluster only this node's: another node's leftover is that node's to replace when it starts,
    /// or another's to fail over while it is down. A store shared with nobody has only this node, so every
    /// leftover of the job is its own. A leftover reserved or running on any node is left alone: that run
    /// is happening.
    /// </remarks>
    private async ValueTask RemoveLeftovers(
        IScheduler target,
        string instanceId,
        bool clustered,
        TriggerKey current,
        CancellationToken cancellationToken)
    {
        PagedResult<TriggerHeader> startupTriggers = await target.QueryTriggers(
            new TriggerQuery
            {
                Job = jobKey,
                Group = GroupMatcher<TriggerKey>.GroupEquals(SchedulerConstants.StartupGroup),
                Take = PagedQuery.All
            },
            cancellationToken).ConfigureAwait(false);

        List<TriggerKey> leftovers = [];
        foreach (TriggerHeader candidate in startupTriggers.Items)
        {
            if (!candidate.Key.Equals(current) && IsLeftover(candidate.Key, instanceId, clustered))
            {
                leftovers.Add(candidate.Key);
            }
        }

        if (leftovers.Count == 0)
        {
            return;
        }

        PagedResult<FireInstance> inFlight = await target.QueryFireInstances(
            new FireInstanceQuery
            {
                TriggerGroup = GroupMatcher<TriggerKey>.GroupEquals(SchedulerConstants.StartupGroup),
                State = null,
                Take = PagedQuery.All
            },
            cancellationToken).ConfigureAwait(false);

        HashSet<TriggerKey> busy = [.. inFlight.Items.Select(x => x.TriggerKey)];
        leftovers.RemoveAll(busy.Contains);

        if (leftovers.Count > 0)
        {
            await target.UnscheduleJobs(leftovers, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Whether <paramref name="key" /> names a startup trigger this node scheduled: every one in a store
    /// shared with nobody, and in a cluster those whose name begins with this node's tag.
    /// </summary>
    internal static bool IsLeftover(TriggerKey key, string instanceId, bool clustered)
    {
        if (!string.Equals(key.Group, SchedulerConstants.StartupGroup, StringComparison.Ordinal))
        {
            return false;
        }

        if (!clustered)
        {
            return true;
        }

        string prefix = NodeTag(instanceId) + ".";
        return key.Name.Length == prefix.Length + 32 && key.Name.StartsWith(prefix, StringComparison.Ordinal);
    }

    /// <summary>
    /// The one-shot trigger a start schedules: due at once, fired at once if it is late, named for the
    /// node that started, and pinned to it when the store is shared with others.
    /// </summary>
    internal static ITrigger CreateTrigger(JobKey jobKey, string instanceId, bool clustered, TimeProvider timeProvider)
    {
        string name = NodeTag(instanceId) + "." + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture);

        return TriggerBuilder.Create(timeProvider)
            .WithIdentity(name, SchedulerConstants.StartupGroup)
            .ForJob(jobKey)
            .WithDescription($"Runs {jobKey} once as {instanceId} starts")
            .WithPreferredNode(clustered ? PreferredNode.For(instanceId) : PreferredNode.None)
            .StartNow()
            .WithSimpleSchedule(x => x.WithMisfireInstruction(SimpleTriggerMisfireInstruction.FireNow))
            .Build();
    }

    /// <summary>
    /// The node as a trigger name carries it: the instance id itself, or for one too long to fit beside
    /// the rest of the name, <c>#</c> and the first 16 bytes of its SHA-256 in hexadecimal.
    /// </summary>
    private static string NodeTag(string instanceId)
    {
        if (instanceId.Length <= MaxNodeTagLength)
        {
            return instanceId;
        }

        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(instanceId));
        return "#" + Convert.ToHexString(hash, 0, 16);
    }
}

/// <summary>
/// That <see cref="QuartzBuilderExtensions.RunAtStartup" /> has registered a run of a job for a scheduler,
/// so that saying it again registers nothing more.
/// </summary>
/// <param name="SchedulerName">The scheduler, empty for the default one.</param>
/// <param name="JobKey">The job.</param>
internal sealed record StartupRunRegistration(string SchedulerName, JobKey JobKey);
