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

namespace Quartz;

/// <summary>
/// What a job says its run achieved, set as <see cref="IJobExecutionContext.Result" />.
/// </summary>
/// <remarks>
/// <para>
/// The execution history reads it when the job has returned. A cancelled run is recorded as
/// <see cref="JobRunResult.Cancelled" />, and a run that threw as <see cref="JobRunResult.Failed" />,
/// whatever the report says; <see cref="Summary" /> and <see cref="Metrics" /> are recorded in every case.
/// </para>
/// <para>
/// To have the scheduler act on a failure — retry it, run <c>OnFailure</c> continuations — throw. A
/// reported <see cref="JobRunResult.Failed" /> is history only.
/// </para>
/// <para>
/// <see cref="JobRunReport" /> is the shipped implementation.
/// </para>
/// </remarks>
public interface IJobRunReport
{
    /// <summary>
    /// What the run achieved.
    /// </summary>
    JobRunResult Result { get; }

    /// <summary>
    /// One line for a reader of the history, or <see langword="null" />. Cut to
    /// <see cref="JobRunReport.MaxSummaryLength" /> characters when recorded.
    /// </summary>
    string? Summary { get; }

    /// <summary>
    /// Named values the run measured, or <see langword="null" />. Recorded as one JSON object of at most
    /// <see cref="JobRunReport.MaxMetricsLength" /> characters; a larger one, or one with a value that
    /// throws while it is written, is dropped whole and the run is recorded without it.
    /// </summary>
    IReadOnlyDictionary<string, object?>? Metrics { get; }
}
