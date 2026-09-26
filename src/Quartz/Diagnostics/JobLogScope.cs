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
/// The logging scope that says which firing a log line belongs to: its job, its trigger and its fire
/// instance.
/// </summary>
/// <remarks>
/// <para>
/// The attribute names are the ones the execution span carries, from <see cref="ActivityTags" />, so a
/// log line and the span of the firing that wrote it are found by the same values. It is the per-firing
/// twin of <see cref="SchedulerLogScope" />, and has its shape for the same reason: an
/// <see cref="IReadOnlyList{T}" /> of key-value pairs, which every structured logging provider reads
/// without allocating an enumerator.
/// </para>
/// <para>
/// One is built per firing, so it holds the five values rather than a list of pairs, and the indexer
/// makes each pair when a provider asks for it. The text form is made the first time a provider asks
/// for it and kept, because the providers that render a scope as text ask for it once per line; made
/// eagerly, it would cost a string on every firing whether or not anything rendered it.
/// </para>
/// </remarks>
internal sealed class JobLogScope : IReadOnlyList<KeyValuePair<string, object?>>
{
    private readonly string jobName;
    private readonly string jobGroup;
    private readonly string triggerName;
    private readonly string triggerGroup;
    private readonly string fireInstanceId;
    private string? formatted;

    public JobLogScope(IJobExecutionContext context)
    {
        JobKey jobKey = context.JobDetail.Key;
        TriggerKey triggerKey = context.Trigger.Key;

        jobName = jobKey.Name;
        jobGroup = jobKey.Group;
        triggerName = triggerKey.Name;
        triggerGroup = triggerKey.Group;
        fireInstanceId = context.FireInstanceId;
    }

    public int Count => 5;

    public KeyValuePair<string, object?> this[int index] => index switch
    {
        0 => new KeyValuePair<string, object?>(ActivityTags.JobName, jobName),
        1 => new KeyValuePair<string, object?>(ActivityTags.JobGroup, jobGroup),
        2 => new KeyValuePair<string, object?>(ActivityTags.TriggerName, triggerName),
        3 => new KeyValuePair<string, object?>(ActivityTags.TriggerGroup, triggerGroup),
        4 => new KeyValuePair<string, object?>(ActivityTags.FireInstanceId, fireInstanceId),
        _ => throw new ArgumentOutOfRangeException(nameof(index), index, "A firing's log scope has five entries."),
    };

    public IEnumerator<KeyValuePair<string, object?>> GetEnumerator()
    {
        for (int i = 0; i < Count; i++)
        {
            yield return this[i];
        }
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    // A benign race: two threads rendering the same scope at once both build the same string.
    public override string ToString() => formatted ??=
        $"{ActivityTags.JobName}:{jobName} {ActivityTags.JobGroup}:{jobGroup} "
        + $"{ActivityTags.TriggerName}:{triggerName} {ActivityTags.TriggerGroup}:{triggerGroup} "
        + $"{ActivityTags.FireInstanceId}:{fireInstanceId}";
}
