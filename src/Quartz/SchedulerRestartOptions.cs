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
/// How long a restart waits for the outgoing scheduler's work, and what it leaves the new one doing.
/// </summary>
/// <remarks>
/// <para>
/// Two settings, and both defaults say "as it was": wait long enough for a job to finish but not
/// indefinitely, and leave the new scheduler running exactly if the old one was. So
/// <see langword="default" /> — which is what omitting the argument gives — is a restart that changes
/// nothing but the instances.
/// </para>
/// <para>
/// There is no <c>WaitForJobsToComplete</c>. A restart always waits, because the next generation's first
/// act is a recovery sweep over the whole scheduler name — it moves acquired and blocked triggers back
/// to waiting and deletes every fired-trigger row — and running that while the previous generation's
/// jobs are still writing would corrupt them. <see cref="DrainTimeout" /> is how long that wait may
/// take, and giving up on it fails the restart rather than proceeding.
/// </para>
/// </remarks>
/// <seealso cref="ISchedulerRuntime.Restart" />
public readonly record struct SchedulerRestartOptions
{
    /// <summary>
    /// Leave the new scheduler for the application to start, whatever the old one was doing. The name
    /// for <c>new SchedulerRestartOptions { Start = false }</c>.
    /// </summary>
    public static SchedulerRestartOptions WithoutStarting => new() { Start = false };

    /// <summary>
    /// How long to wait for the outgoing scheduler's running jobs before giving up on the restart.
    /// </summary>
    /// <remarks>
    /// <see langword="null" /> — the default — is thirty seconds.
    /// <see cref="Timeout.InfiniteTimeSpan" /> waits until the jobs finish or the cancellation token
    /// fires, whichever comes first. The wait covers each job's job store update as well as the job
    /// itself, because the thread pool is handed the whole of an execution and that update is its last
    /// act.
    /// <para>
    /// A wait that expires leaves the old scheduler shut down and the new one unbuilt, and reports a
    /// <see cref="SchedulerRestartException" />. Ask again once the work has finished — nothing else is
    /// needed, and until then the name is listed with no status. Long-running jobs that must be cut
    /// short are what <see cref="ShutdownJobInterruption" /> and <see cref="JobTimeoutAttribute" /> are
    /// for.
    /// </para>
    /// </remarks>
    public TimeSpan? DrainTimeout { get; init; }

    /// <summary>
    /// Whether the new scheduler is started.
    /// </summary>
    /// <remarks>
    /// <see langword="null" /> — the default — starts it if the old one was running, and leaves it
    /// created if the old one was in standby, had never been started, or had already been shut down.
    /// A restart that changed whether a scheduler fires would be a second decision hidden inside the
    /// first. Say <see langword="true" /> or <see langword="false" /> to decide it deliberately.
    /// </remarks>
    public bool? Start { get; init; }
}
