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

// ReSharper disable ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract - Can be null when received from Web API

using System.Globalization;

namespace Quartz.HttpApiContract;

internal record KeyDto(string Name, string Group) : IValidatable
{
    public static KeyDto Create(JobKey jobKey)
    {
        ArgumentNullException.ThrowIfNull(jobKey);

        return new KeyDto(jobKey.Name, jobKey.Group);
    }

    public static KeyDto Create(TriggerKey triggerKey)
    {
        ArgumentNullException.ThrowIfNull(triggerKey);

        return new KeyDto(triggerKey.Name, triggerKey.Group);
    }

    public JobKey AsJobKey() => new(Name, Group);

    public TriggerKey AsTriggerKey() => new(Name, Group);

    public IEnumerable<string> Validate()
    {
        if (Name is null)
        {
            yield return "Key is missing name";
        }

        if (Group is null)
        {
            yield return "Key is missing group";
        }
    }

    public override string ToString() => Group + '.' + Name;
}

internal record SchedulerContextDto(Dictionary<string, string?> Context)
{
    /// <summary>
    /// Renders a scheduler's context as text. A <see langword="string" /> passes through; any other
    /// value becomes its invariant text, and <see langword="null" /> stays <see langword="null" />.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Nothing here throws. The scheduler context is the application's own <c>Map&lt;String, Object&gt;</c>
    /// and an application may put whatever it likes in it, so a value that is not a string is ordinary
    /// rather than exceptional — this used to answer one with a <see cref="NotSupportedException" />,
    /// which the endpoint turned into a <c>500</c> for the whole context.
    /// </para>
    /// <para>
    /// Text is all a remote reader can use in any case: the values arrive as JSON strings and
    /// <see cref="AsContext" /> hands every one of them back as a <see langword="string" />, whatever it
    /// was in the scheduler's own process. Rendering with
    /// <see cref="CultureInfo.InvariantCulture" /> is what makes that reading the same one wherever the
    /// server happens to be running.
    /// </para>
    /// <para>
    /// An instant is the one value rendered by name rather than by <c>Convert.ToString</c>: a
    /// <see cref="DateTimeOffset" /> or a <see cref="DateTime" /> goes out in the round-trip <c>"O"</c>
    /// format, so a context entry reads as the ISO-8601 instant every other instant this API emits is.
    /// Nothing else needs a case — a <see cref="TimeSpan" /> already renders in its constant form and a
    /// <see cref="Guid" /> in its <c>"D"</c> one.
    /// </para>
    /// <para>
    /// The entries are ordered by key, ordinally. A <see cref="SchedulerContext" /> is backed by a
    /// <see cref="System.Collections.Concurrent.ConcurrentDictionary{TKey, TValue}" />, whose enumeration
    /// order is where the keys landed in a hash table rather than anything the application chose, so
    /// ordering them here is what makes the body the same one on every server and on every run. The
    /// order is part of the contract rather than an accident, and a snapshot can pin it.
    /// </para>
    /// </remarks>
    public static SchedulerContextDto Create(SchedulerContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        Dictionary<string, string?> data = context
            .OrderBy(x => x.Key, StringComparer.Ordinal)
            .ToDictionary(x => x.Key, x => Render(x.Value));

        return new SchedulerContextDto(data);
    }

    /// <summary>
    /// One context value as the text a remote reader gets.
    /// </summary>
    private static string? Render(object? value)
    {
        return value switch
        {
            null => null,
            string text => text,
            DateTimeOffset instant => instant.ToString("O", CultureInfo.InvariantCulture),
            DateTime instant => instant.ToString("O", CultureInfo.InvariantCulture),
            _ => Convert.ToString(value, CultureInfo.InvariantCulture)
        };
    }

    public SchedulerContext AsContext()
    {
        return new SchedulerContext(Context.ToDictionary(x => x.Key, x => (object?) x.Value));
    }
}

internal record TriggerStateDto(TriggerState State);