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
using Quartz.Diagnostics;
using Quartz.Util;

namespace Quartz.Impl;

/// <summary>
/// The history rows a firing is recorded as, built in one place for the two that build them: the
/// <see cref="ExecutionHistoryPlugin" />, which stores them, and the run shell, which answers a wait for the
/// firing with the row the history would record when there is no history to record it (#4414).
/// </summary>
internal static class ExecutionHistoryEntries
{
    /// <summary>
    /// The row of an execution that has just ended.
    /// </summary>
    /// <remarks>
    /// The <see cref="ExecutionHistoryEntry.Result" /> is decided by <see cref="JobRunClassifier.Classify" />, and
    /// its summary and metrics come from the job's <see cref="IJobRunReport" />, if it set one. Its exception
    /// message is what the job threw, read by <see cref="JobFailure.MessageOf" />: the run shell's wrapper says
    /// the same of every failure.
    /// </remarks>
    /// <param name="context">The firing.</param>
    /// <param name="jobException">What the job threw, or <see langword="null" />.</param>
    /// <param name="logger">Where a dropped metrics object is reported.</param>
    /// <param name="entryId">The row's key, or <see langword="null" /> for a row nothing stores.</param>
    /// <param name="input">The input the row keeps, or <see langword="null" />.</param>
    /// <param name="inputTooLarge">Whether the input was over the cap and left out.</param>
    internal static ExecutionHistoryEntry Execution(
        IJobExecutionContext context,
        JobExecutionException? jobException,
        ILogger logger,
        string? entryId,
        string? input = null,
        bool inputTooLarge = false)
    {
        JobRunClassification run = JobRunClassifier.Classify(context, jobException);
        FiringOccurrence occurrence = context.GetOccurrence();

        return new ExecutionHistoryEntry(
            SchedulerName: context.Scheduler.SchedulerName,
            SchedulerInstanceId: context.Scheduler.SchedulerInstanceId,
            JobGroup: context.JobDetail.Key.Group,
            JobName: context.JobDetail.Key.Name,
            TriggerGroup: context.Trigger.Key.Group,
            TriggerName: context.Trigger.Key.Name,
            FiredAtUtc: context.FireTimeUtc,
            Duration: context.JobRunTime,
            Succeeded: JobRunClassifier.IsSuccess(run.Result),
            ExceptionMessage: jobException is null ? null : JobFailure.MessageOf(jobException))
        {
            Result = run.Result,
            Summary = TextCut.ToFit(run.Summary, JobRunReport.MaxSummaryLength),
            MetricsJson = ExecutionMetricsWriter.Write(run.Metrics, logger, context.JobDetail.Key),
            Manual = ManualFiring.IsMarked(context.Trigger),
            FireInstanceId = context.FireInstanceId,

            // When the run was due, and how late it started: the occurrence, so a backfill is due when
            // its slot was, and the measured delay, from when the attempt itself was due, so a retry
            // reports its own lateness and not its backoff. The delay is what the run shell measured at
            // the job's start, or null on a firing that never ran through it.
            DueTimeUtc = occurrence.OccurrenceUtc,
            StartDelay = (context as JobExecutionContextImpl)?.StartDelay,
            Origin = occurrence.Origin,
            ExceptionType = jobException is null ? null : JobFailure.TypeOf(jobException),

            // Both are the scheduler's own reading of the firing that has just ended, published on the
            // context before any completion notification goes out. Together they are what lets a reader tell
            // one attempt of an occurrence from its last one.
            RetryAttempt = context.RetryAttempt,
            RetryScheduled = context.RetryScheduled,

            EntryId = entryId,

            // What the job logged, when this scheduler captures: taken now, once the job has returned, so a
            // line logged after this is not the row's.
            Log = ExecutionLogCapture.Find(context)?.ToText(),

            Input = input,
            InputTooLarge = inputTooLarge
        };
    }

    /// <summary>
    /// The row of a firing a trigger listener vetoed: in the misfire feed, with <see cref="MisfireReason.Vetoed" />.
    /// </summary>
    /// <param name="context">The firing.</param>
    /// <param name="vetoedAtUtc">When the veto was recorded, on the scheduler's clock.</param>
    internal static MisfireHistoryEntry Vetoed(IJobExecutionContext context, DateTimeOffset vetoedAtUtc)
    {
        return new MisfireHistoryEntry(
            SchedulerName: context.Scheduler.SchedulerName,
            SchedulerInstanceId: context.Scheduler.SchedulerInstanceId,
            TriggerGroup: context.Trigger.Key.Group,
            TriggerName: context.Trigger.Key.Name,
            JobKey: context.JobDetail.Key,
            MisfiredAtUtc: vetoedAtUtc,
            ScheduledFireTimeUtc: context.ScheduledFireTimeUtc)
        {
            Reason = MisfireReason.Vetoed
        };
    }
}
