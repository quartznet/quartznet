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

using Quartz.HttpApiContract;

namespace Quartz.Extensibility;

/// <summary>
/// Where one scheduler's events can be read as they happen.
/// </summary>
/// <remarks>
/// <para>
/// Two implementations, and a reader cannot tell them apart: the broker every scheduler in this process
/// publishes into, and a reader of another process's event route. That is what lets a page subscribe to
/// "this scheduler's events" without knowing which process the scheduler runs in — the same shape
/// <see cref="IExecutionHistoryStore" /> gives the history.
/// </para>
/// <para>
/// A subscription carries what happens after it is made. There is no replay and no cursor: the events are
/// a live view, and what a scheduler <em>has</em> done is the history's question.
/// </para>
/// <para>
/// Internal in 4.1. The event model, the broker and the reader are all Quartz's own for now, and a public
/// seam over them can be added later without moving any of it.
/// </para>
/// </remarks>
internal interface ISchedulerEventSource
{
    /// <summary>
    /// Reads <paramref name="schedulerName" />'s events until <paramref name="cancellationToken" /> is
    /// cancelled or the enumeration is disposed.
    /// </summary>
    /// <remarks>
    /// The enumeration <em>completes</em> when the token is cancelled rather than raising: a reader that
    /// has gone away is the ordinary end of a subscription, not a failure of one.
    /// </remarks>
    /// <param name="schedulerName">The scheduler whose events to read.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    IAsyncEnumerable<SchedulerEvent> Subscribe(string schedulerName, CancellationToken cancellationToken = default);
}
