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
/// What one run of a job achieved, as its execution history records it.
/// </summary>
/// <remarks>
/// <para>
/// Separate from <see cref="ExecutionOutcome" />, which drives continuations and is not changed by
/// anything a job reports. A job sets <see cref="Skipped" /> or <see cref="Failed" /> through
/// <see cref="IJobExecutionContext.Result" />; see <see cref="JobRunReport" />.
/// </para>
/// <para>
/// Persisted as the integer, so the values are a storage contract: members are never renumbered and a
/// new one is appended.
/// </para>
/// </remarks>
/// <seealso cref="ExecutionHistoryEntry.Result" />
public enum JobRunResult
{
    /// <summary>
    /// The job ran and did its work.
    /// </summary>
    Succeeded = 0,

    /// <summary>
    /// The job threw, or reported a failure through <see cref="IJobRunReport" />.
    /// </summary>
    /// <remarks>
    /// A reported failure is history only: the scheduler retries, runs <c>OnFailure</c> continuations
    /// and raises <see cref="ITriggerListener.TriggerRetriesExhausted" /> only for a job that throws.
    /// </remarks>
    Failed = 1,

    /// <summary>
    /// The firing's cancellation token was signalled and the job stopped rather than finished.
    /// </summary>
    Cancelled = 2,

    /// <summary>
    /// The job ran and found nothing to do. Counts as a success.
    /// </summary>
    Skipped = 3,
}
