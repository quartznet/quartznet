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
/// A scheduler that can read the lines a running firing has logged, in the process running it (#4437).
/// </summary>
/// <remarks>
/// <para>
/// No <see cref="IScheduler" /> member says it, and nothing can be composed from those that do: the lines live
/// only in the capture buffer beside the firing's execution context, in the process that runs it. The
/// scheduler Quartz builds answers from its own running firings; <c>HttpScheduler</c> asks its host on the
/// <c>…/jobs/fire-instances/{fireInstanceId}/log</c> route; a cluster of targets asks the member fronting the
/// node that runs the firing.
/// </para>
/// <para>
/// Internal, as <see cref="IFiringWaitingScheduler" /> is: the scheduler Quartz builds, the container's
/// <see cref="IScheduler" />, <see cref="DelegatingScheduler" /> and <c>HttpScheduler</c> implement it, and the
/// HTTP API, the dashboard agent and the dashboard read it through <see cref="FireInstanceLogs.Read" />. A
/// scheduler without it is refused with <see cref="NotSupportedException" />.
/// </para>
/// </remarks>
internal interface IFireInstanceLogScheduler
{
    /// <summary>
    /// The entries the firing <see cref="FireInstanceLogRequest.FireInstanceId" /> has logged after the reader's
    /// cursor, or the state that says why there are none to read.
    /// </summary>
    /// <param name="request">The firing, the cursor, the page size and the node that runs it.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    /// <exception cref="ArgumentException">The cursor or the page size is out of range.</exception>
    /// <exception cref="NotSupportedException">The scheduler behind this one cannot read a running firing's log.</exception>
    ValueTask<FireInstanceLog> ReadFireInstanceLog(FireInstanceLogRequest request, CancellationToken cancellationToken = default);
}

/// <summary>
/// What a read of a running firing's log asks for.
/// </summary>
/// <param name="FireInstanceId">The firing.</param>
/// <param name="After">How many entries the reader has: the <see cref="FireInstanceLog.Next" /> of its last read, 0 at first.</param>
/// <param name="Take">The most entries to answer, from 1 to <see cref="FireInstanceLogs.MaxTake" />.</param>
/// <param name="SchedulerInstanceId">
/// The node that runs the firing, as its row lists it, or <see langword="null" />: a cluster of targets asks that
/// node alone, and the store's owner of the firing without it.
/// </param>
internal sealed record FireInstanceLogRequest(string FireInstanceId, long After, int Take, string? SchedulerInstanceId);

/// <summary>
/// One read of a running firing's log.
/// </summary>
/// <param name="State">Whether there are lines to read here, and why not when there are none.</param>
/// <param name="SchedulerInstanceId">
/// The node that answered, or <see langword="null" /> with <see cref="FireInstanceLogState.NotRunning" /> when the
/// store lists no such firing, and the node no member fronts with <see cref="FireInstanceLogState.Unreachable" />.
/// </param>
/// <param name="Entries">The entries after the cursor, oldest first.</param>
/// <param name="Next">The cursor of the next read.</param>
/// <param name="Missed">Entries the capture's bounds dropped after the cursor and before this read.</param>
/// <param name="HasMore">Whether more entries are kept than this read answered: read again at once.</param>
internal sealed record FireInstanceLog(
    FireInstanceLogState State,
    string? SchedulerInstanceId,
    string[] Entries,
    long Next,
    long Missed,
    bool HasMore);

/// <summary>
/// Whether a running firing's log can be read where it was asked, and why not when it cannot.
/// </summary>
internal enum FireInstanceLogState
{
    /// <summary>The firing runs on the node that answered, and that scheduler captures; it may have logged nothing yet.</summary>
    Running = 0,

    /// <summary>The firing runs on the node that answered, and that scheduler does not call <c>UseExecutionLogCapture()</c>.</summary>
    NotCaptured = 1,

    /// <summary>The node that answered runs no firing of that id, or, with no node named, the store lists none.</summary>
    NotRunning = 2,

    /// <summary>No process this caller reaches runs the firing: a store window, or a node no member target fronts.</summary>
    Unreachable = 3,
}

/// <summary>
/// The bounds of a running firing's log read, and the read for any <see cref="IScheduler" />.
/// </summary>
internal static class FireInstanceLogs
{
    /// <summary>The entries one read answers when the caller names no number.</summary>
    public const int DefaultTake = 500;

    /// <summary>The most entries one read answers.</summary>
    public const int MaxTake = 1000;

    /// <summary>
    /// The most UTF-8 bytes of entries one read answers, the first entry excepted: far under the agent hub's
    /// message cap, whatever <see cref="ExecutionLogCaptureOptions.MaxLines" /> an application raised.
    /// </summary>
    public const int MaxAnswerBytes = 65_536;

    /// <summary>
    /// Why a read with <paramref name="after" /> and <paramref name="take" /> is refused, or <see langword="null" />
    /// when both are in range.
    /// </summary>
    public static string? Refusal(long after, int take)
    {
        if (after < 0)
        {
            return $"after must be 0 or more, was {after}";
        }

        return take is < 1 or > MaxTake
            ? $"take must be from 1 to {MaxTake}, was {take}"
            : null;
    }

    /// <exception cref="ArgumentException">The request's cursor or page size is out of range.</exception>
    public static void Check(FireInstanceLogRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.FireInstanceId, nameof(request));

        if (Refusal(request.After, request.Take) is { } refusal)
        {
            throw new ArgumentException(refusal, nameof(request));
        }
    }

    /// <summary>
    /// Reads the firing's log through <paramref name="scheduler" />, which must be able to.
    /// </summary>
    /// <exception cref="ArgumentException">The request's cursor or page size is out of range.</exception>
    /// <exception cref="NotSupportedException"><paramref name="scheduler" /> cannot read a running firing's log.</exception>
    public static ValueTask<FireInstanceLog> Read(IScheduler scheduler, FireInstanceLogRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scheduler);
        Check(request);

        return scheduler is IFireInstanceLogScheduler logs
            ? logs.ReadFireInstanceLog(request, cancellationToken)
            : ValueTask.FromException<FireInstanceLog>(CannotRead(scheduler));
    }

    /// <summary>
    /// The answer of a caller that reaches no process running the firing: a store window, or a node no member
    /// target fronts.
    /// </summary>
    /// <param name="node">The node that runs the firing, when it is known.</param>
    /// <param name="after">The reader's cursor, answered back unchanged.</param>
    public static FireInstanceLog Unreachable(string? node, long after)
    {
        return new FireInstanceLog(FireInstanceLogState.Unreachable, node, [], after, Missed: 0, HasMore: false);
    }

    /// <summary>
    /// The answer of a node that runs no firing of that id, or of a store that lists none (<paramref name="node" />
    /// <see langword="null" />).
    /// </summary>
    public static FireInstanceLog NotRunning(string? node, long after)
    {
        return new FireInstanceLog(FireInstanceLogState.NotRunning, node, [], after, Missed: 0, HasMore: false);
    }

    /// <summary>
    /// What a scheduler that cannot read a running firing's log raises.
    /// </summary>
    public static NotSupportedException CannotRead(IScheduler scheduler)
    {
        return new NotSupportedException(
            $"Scheduler '{scheduler.SchedulerName}' cannot read a running firing's log: it is not a scheduler Quartz built, "
            + "nor one that reaches such a scheduler.");
    }

    /// <summary>
    /// What <c>HttpScheduler</c> raises for a host or agent that has no such route: the words Currently Executing's
    /// <em>Show log</em> shows.
    /// </summary>
    public static NotSupportedException HostTooOld()
    {
        return new NotSupportedException(
            "This target runs Quartz older than 4.9, which cannot show a running firing's log. Its log is on its execution page once it ends.");
    }
}
