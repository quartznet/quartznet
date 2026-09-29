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

namespace Quartz.Impl;

/// <summary>
/// What a finished firing's history row says it achieved.
/// </summary>
/// <param name="Result">The result, decided by <see cref="JobRunClassifier.Classify" />.</param>
/// <param name="Summary">The job's own summary, uncut.</param>
/// <param name="Metrics">The job's own metrics, unwritten.</param>
internal readonly record struct JobRunClassification(
    JobRunResult Result,
    string? Summary,
    IReadOnlyDictionary<string, object?>? Metrics);

/// <summary>
/// Decides a finished firing's <see cref="JobRunResult" />.
/// </summary>
internal static class JobRunClassifier
{
    /// <summary>
    /// The first of these that holds: the firing was cancelled; the job threw; the job set an
    /// <see cref="IJobRunReport" /> as <see cref="IJobExecutionContext.Result" />, which answers; otherwise
    /// it succeeded.
    /// </summary>
    /// <remarks>
    /// The report's summary and metrics are kept whichever rule decided. Any other
    /// <see cref="IJobExecutionContext.Result" /> is ignored — <c>NativeJob</c> keeps an exit code there.
    /// </remarks>
    /// <param name="context">The firing, settled by the run shell.</param>
    /// <param name="jobException">What the job threw, or <see langword="null" />.</param>
    internal static JobRunClassification Classify(IJobExecutionContext context, JobExecutionException? jobException)
    {
        IJobRunReport? report = context.Result as IJobRunReport;

        JobRunResult result;
        if (context.Outcome == ExecutionOutcome.Cancelled)
        {
            result = JobRunResult.Cancelled;
        }
        else if (jobException is not null)
        {
            result = JobRunResult.Failed;
        }
        else if (report is not null)
        {
            result = report.Result;
        }
        else
        {
            result = JobRunResult.Succeeded;
        }

        return new JobRunClassification(result, report?.Summary, report?.Metrics);
    }

    /// <summary>
    /// Whether a result counts as a success: <see cref="JobRunResult.Succeeded" /> or
    /// <see cref="JobRunResult.Skipped" />.
    /// </summary>
    internal static bool IsSuccess(JobRunResult result) => result is JobRunResult.Succeeded or JobRunResult.Skipped;
}
