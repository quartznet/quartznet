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

using System.Collections;

namespace Quartz.Diagnostics;

/// <summary>
/// The logging scope that says which scheduler a log line belongs to.
/// </summary>
/// <remarks>
/// <para>
/// An <see cref="Microsoft.Extensions.Logging.ILogger" /> category is a type name, so two schedulers in
/// one process write log lines that are identical in everything a log query can filter on — except where
/// a message template happens to carry the scheduler's name, which most of them do not. A scope carries
/// it on every line instead.
/// </para>
/// <para>
/// The attribute names are the ones the spans and the measurements use, from
/// <see cref="ActivityTags" />, so one query says "this tenant" against traces, metrics and logs alike.
/// </para>
/// <para>
/// It is an <see cref="IReadOnlyList{T}" /> of key-value pairs — the shape ASP.NET Core's request scope
/// has, and the one every structured logging provider reads without allocating an enumerator — and it is
/// immutable, so one instance per scheduler is built once and pushed by whoever opens the scope. The
/// scheduler thread opens it once for the lifetime of its loop; a job's own log lines carry it when the
/// dispatch to the thread pool captured the execution context that the loop's scope is held in.
/// </para>
/// </remarks>
internal sealed class SchedulerLogScope : IReadOnlyList<KeyValuePair<string, object?>>
{
    private readonly KeyValuePair<string, object?>[] tags;
    private readonly string formatted;

    public SchedulerLogScope(string schedulerName, string schedulerInstanceId)
    {
        tags =
        [
            new KeyValuePair<string, object?>(ActivityTags.SchedulerName, schedulerName),
            new KeyValuePair<string, object?>(ActivityTags.SchedulerId, schedulerInstanceId)
        ];

        // Built here rather than on demand, because the providers that render a scope as text ask for
        // this once per line and would otherwise format the same two values over and over.
        formatted = $"{ActivityTags.SchedulerName}:{schedulerName} {ActivityTags.SchedulerId}:{schedulerInstanceId}";
    }

    public int Count => tags.Length;

    public KeyValuePair<string, object?> this[int index] => tags[index];

    public IEnumerator<KeyValuePair<string, object?>> GetEnumerator() => ((IEnumerable<KeyValuePair<string, object?>>) tags).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => tags.GetEnumerator();

    public override string ToString() => formatted;
}
