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

using System.Diagnostics.CodeAnalysis;

using Microsoft.Extensions.Logging;

using Quartz.Diagnostics;
using Quartz.Util;

namespace Quartz.Listeners;

/// <summary>
/// Keeps a collection of mappings of which jobs to trigger after the completion
/// of a given job.  If this listener is notified of a job completing that has a
/// mapping, then it will then attempt to trigger each of its follow-up jobs.  This
/// achieves "job chaining", or a "poor man's workflow".
///</summary>
/// <remarks>
/// <para>
/// Generally an instance of this listener would be registered as a global
/// job listener, rather than being registered directly to a given job.
/// </para>
/// <para>
/// A job can be chained to more than one follow-up job, by calling
/// <see cref="AddJobChainLink(JobKey, JobKey)" /> once per follow-up or <see cref="AddJobChainLinks" />
/// with all of them.  Each follow-up is triggered as its own firing, in the order the
/// links were added, so the follow-ups run concurrently rather than one after another —
/// as many at a time as the thread pool has threads to give them.  A follow-up that has to
/// wait for a sibling is a chain link from that sibling, not a second link from the same job.
/// </para>
/// <para>
/// If for some reason there is a failure creating the trigger for a
/// follow-up job (which would generally only be caused by a rare serious
/// failure in the system, or the non-existence of the follow-up job), an error
/// message is logged, but no other action is taken: the remaining follow-ups of that
/// job are still triggered. If you need more rigorous handling of the error, consider
/// scheduling the triggering of the follow-up job within your job itself.
/// </para>
/// <para>
/// The links are meant to be registered before the scheduler is started, and this type
/// does not synchronize them; adding one while jobs are executing races with the
/// notifications reading it.
/// </para>
///</remarks>
/// <author>James House</author>
/// <author>Marko Lahma (.NET)</author>
public sealed class JobChainingJobListener : IJobListener
{
    /// <summary>
    /// One follow-up, and the outcomes of the first job's execution that trigger it.
    /// </summary>
    private readonly record struct ChainLink(JobKey FollowUpJob, ContinuationCondition When);

    private readonly Dictionary<JobKey, List<ChainLink>> chainLinks;
    private readonly ILogger<JobChainingJobListener> logger;

    /// <summary>
    /// Construct an instance with the given name.
    /// </summary>
    /// <param name="name">The name of this instance.</param>
    public JobChainingJobListener(string name)
    {
        if (name is null)
        {
            Throw.ArgumentException("Listener name cannot be null!");
        }
        Name = name;
        chainLinks = new Dictionary<JobKey, List<ChainLink>>();
        logger = LogProvider.CreateLogger<JobChainingJobListener>();
    }

    /// <inheritdoc />
    public string Name { get; }

    /// <summary>
    /// Add a chain mapping - when the Job identified by the first key completes
    /// the job identified by the second key will be triggered.
    /// </summary>
    /// <remarks>
    /// Calling this again with the same first job adds a second follow-up rather than
    /// replacing the first one; the two then run concurrently. Chaining the same follow-up
    /// to the same first job twice is a configuration mistake — it would fire that job twice
    /// for one completion — and is rejected.
    /// </remarks>
    /// <param name="firstJob">a JobKey with the name and group of the first job</param>
    /// <param name="secondJob">a JobKey with the name and group of the follow-up job</param>
    /// <exception cref="ArgumentException">
    /// Either key is null or has a null name, or <paramref name="secondJob" /> is already
    /// chained to <paramref name="firstJob" />.
    /// </exception>
    public void AddJobChainLink(JobKey firstJob, JobKey secondJob)
    {
        AddJobChainLink(firstJob, secondJob, ContinuationCondition.OnAnyOutcome);
    }

    /// <summary>
    /// Add a chain mapping that fires the follow-up only when the first job's execution ended in one
    /// of the named ways.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The conditional twin of <see cref="AddJobChainLink(JobKey, JobKey)" />, which is
    /// <see cref="ContinuationCondition.OnAnyOutcome" /> — the behaviour this listener has always
    /// had. This is how a <em>recurring</em> conditional chain is written: "run the cleanup whenever
    /// the nightly job fails" is a link, not a <see cref="Quartz.Continuation" />, because a
    /// continuation settles once and this fires on every completion.
    /// </para>
    /// <para>
    /// It is the listener's reading of the outcome, not the store's: a link is a process-local
    /// arrangement on the node that ran the first job. <see cref="ExecutionOutcome.NotExecuted" /> —
    /// a firing a listener abandoned, or a job that could not be instantiated — reaches no
    /// notification here, so it triggers no follow-up whatever the condition says.
    /// </para>
    /// </remarks>
    /// <param name="firstJob">a JobKey with the name and group of the first job</param>
    /// <param name="secondJob">a JobKey with the name and group of the follow-up job</param>
    /// <param name="condition">the outcomes of the first job's execution that trigger the follow-up</param>
    /// <exception cref="ArgumentException">
    /// Either key is null or has a null name, or <paramref name="secondJob" /> is already
    /// chained to <paramref name="firstJob" />.
    /// </exception>
    /// <seealso cref="ContinuationCondition" />
    public void AddJobChainLink(JobKey firstJob, JobKey secondJob, ContinuationCondition condition)
    {
        ValidateKey(firstJob, nameof(firstJob));
        ValidateKey(secondJob, nameof(secondJob));

        if (!chainLinks.TryGetValue(firstJob, out List<ChainLink>? followUpJobs))
        {
            followUpJobs = new List<ChainLink>(capacity: 1);
            chainLinks[firstJob] = followUpJobs;
        }
        else if (followUpJobs.Exists(link => link.FollowUpJob.Equals(secondJob)))
        {
            ThrowAlreadyChained(firstJob, secondJob, nameof(secondJob));
        }

        followUpJobs.Add(new ChainLink(secondJob, condition));
    }

    /// <summary>
    /// Add several chain mappings at once - when the Job identified by the first key
    /// completes, every one of the given follow-up jobs will be triggered.
    /// </summary>
    /// <remarks>
    /// This is <see cref="AddJobChainLink(JobKey, JobKey)" /> for the fan-out case, and appends to whatever
    /// the first job is already chained to. The follow-ups are triggered in the order given,
    /// each as its own firing, so they run concurrently. Naming the same follow-up twice — in
    /// this collection or against a link added earlier — is a configuration mistake and is
    /// rejected; nothing is added when it is.
    /// </remarks>
    /// <param name="firstJob">a JobKey with the name and group of the first job</param>
    /// <param name="followUpJobs">the keys of the jobs to trigger when the first job completes</param>
    /// <exception cref="ArgumentException">
    /// Any key is null or has a null name, <paramref name="followUpJobs" /> is null or empty,
    /// or a follow-up is named twice.
    /// </exception>
    public void AddJobChainLinks(JobKey firstJob, IReadOnlyCollection<JobKey> followUpJobs)
    {
        ValidateKey(firstJob, nameof(firstJob));

        if (followUpJobs is null)
        {
            Throw.ArgumentException("Follow-up jobs cannot be null!", nameof(followUpJobs));
        }

        if (followUpJobs.Count == 0)
        {
            Throw.ArgumentException("At least one follow-up job is required!", nameof(followUpJobs));
        }

        // validate the whole collection before touching the links, so a rejected call leaves
        // the listener as it was rather than half-configured
        chainLinks.TryGetValue(firstJob, out List<ChainLink>? existing);
        List<ChainLink> added = new List<ChainLink>(followUpJobs.Count);

        foreach (JobKey followUpJob in followUpJobs)
        {
            ValidateKey(followUpJob, nameof(followUpJobs));

            if (existing?.Exists(link => link.FollowUpJob.Equals(followUpJob)) == true
                || added.Exists(link => link.FollowUpJob.Equals(followUpJob)))
            {
                ThrowAlreadyChained(firstJob, followUpJob, nameof(followUpJobs));
            }

            added.Add(new ChainLink(followUpJob, ContinuationCondition.OnAnyOutcome));
        }

        if (existing is null)
        {
            chainLinks[firstJob] = added;
        }
        else
        {
            existing.AddRange(added);
        }
    }

    /// <inheritdoc />
    public ValueTask JobWasExecuted(IJobExecutionContext context,
        JobExecutionException? jobException,
        CancellationToken cancellationToken = default)
    {
        // The three outcomes this notification can carry. A veto arrives at JobExecutionVetoed
        // instead, and a firing that never happened arrives nowhere.
        ContinuationCondition outcome = context.CancellationToken.IsCancellationRequested
            ? ContinuationCondition.OnCancellation
            : jobException is not null
                ? ContinuationCondition.OnFailure
                : ContinuationCondition.OnSuccess;

        return TriggerFollowUps(context, outcome, cancellationToken);
    }

    /// <inheritdoc />
    public ValueTask JobExecutionVetoed(IJobExecutionContext context, CancellationToken cancellationToken = default)
    {
        // Declared so that OnVeto means something here: a vetoed firing never reaches JobWasExecuted,
        // so a link conditioned on it would otherwise never fire.
        return TriggerFollowUps(context, ContinuationCondition.OnVeto, cancellationToken);
    }

    private async ValueTask TriggerFollowUps(
        IJobExecutionContext context,
        ContinuationCondition outcome,
        CancellationToken cancellationToken)
    {
        if (!chainLinks.TryGetValue(context.JobDetail.Key, out List<ChainLink>? followUpJobs))
        {
            return;
        }

        foreach (ChainLink link in followUpJobs)
        {
            if (!link.When.HasFlag(outcome))
            {
                continue;
            }

            logger.ChainingToJob(context.JobDetail.Key, link.FollowUpJob);

            try
            {
                await context.Scheduler.TriggerJob(link.FollowUpJob, cancellationToken: cancellationToken).ConfigureAwait(false);
            }
            catch (SchedulerException se)
            {
                // a follow-up that could not be triggered must not cost its siblings their firing
                logger.ChainingToJobFailed(link.FollowUpJob, se);
            }
        }
    }

    private static void ValidateKey(JobKey key, string paramName)
    {
        if (key is null)
        {
            Throw.ArgumentException("Key cannot be null!", paramName);
        }
        if (key.Name is null)
        {
            Throw.ArgumentException("Key cannot have a null name!", paramName);
        }
    }

    [DoesNotReturn]
    private static void ThrowAlreadyChained(JobKey firstJob, JobKey followUpJob, string paramName)
    {
        Throw.ArgumentException($"Job '{firstJob}' is already chained to Job '{followUpJob}'!", paramName);
    }
}