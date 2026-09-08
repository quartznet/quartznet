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
/// Thrown by <see cref="ISchedulerRuntime.Restart" /> when the outgoing scheduler's jobs were still
/// running when the drain gave up on them.
/// </summary>
/// <remarks>
/// <para>
/// A type of its own rather than a bare <see cref="SchedulerException" />, because this is the one
/// failure a caller can act on without changing anything: the work will finish, and asking again
/// afterwards succeeds. Nothing was lost — the old scheduler is shut down, the new one was never built,
/// and the scheduler's name is listed with no status until a restart completes.
/// </para>
/// <para>
/// A <see cref="SchedulerConfigException" /> is the other outcome and means the opposite: the recipe
/// cannot produce a second generation, so asking again will fail the same way.
/// </para>
/// </remarks>
public sealed class SchedulerRestartException : SchedulerException
{
    internal SchedulerRestartException(
        string schedulerName,
        int jobsStillExecuting,
        TimeSpan drainTimeout,
        string message) : base(message)
    {
        SchedulerName = schedulerName;
        JobsStillExecuting = jobsStillExecuting;
        DrainTimeout = drainTimeout;
    }

    /// <summary>
    /// The scheduler that was being restarted, so a caller can report it without parsing the message.
    /// </summary>
    public string SchedulerName { get; }

    /// <summary>
    /// How many of its jobs the outgoing scheduler was still running when the wait was given up on.
    /// </summary>
    /// <remarks>
    /// A floor rather than a total. The drain waits for each job's job store update as well as the job,
    /// and a job leaves this count before its update is issued — so zero here means the jobs have
    /// finished and a write is still in flight, which is just as much a reason not to start a second
    /// scheduler over the same store.
    /// </remarks>
    public int JobsStillExecuting { get; }

    /// <summary>
    /// The deadline that expired, as <see cref="SchedulerRestartOptions.DrainTimeout" /> gave it or
    /// defaulted it.
    /// </summary>
    public TimeSpan DrainTimeout { get; }
}
