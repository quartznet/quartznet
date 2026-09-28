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
/// What a backfill may be told past the trigger and the range: how many slots it may schedule, how far
/// apart their firings start, and which execution group they count against.
/// </summary>
/// <remarks>
/// <see langword="default" /> — what omitting the argument gives — is at most
/// <see cref="DefaultMaxSlots" /> slots, every one starting now, in the original trigger's execution group.
/// </remarks>
/// <seealso cref="SchedulerBackfillExtensions" />
public readonly record struct BackfillOptions
{
    /// <summary>
    /// The most slots a backfill schedules when <see cref="MaxSlots" /> is not set.
    /// </summary>
    public const int DefaultMaxSlots = 1000;

    // Unset reads as DefaultMaxSlots, so that default(BackfillOptions) has the same ceiling as an options
    // value nobody set it on.
    private readonly int? maxSlots;

    /// <summary>
    /// The most slots the range may hold. A range holding more is refused whole with
    /// <see cref="ArgumentException" />, whose message says how many it holds. Defaults to
    /// <see cref="DefaultMaxSlots" />; below 1 is refused.
    /// </summary>
    public int MaxSlots
    {
        get => maxSlots ?? DefaultMaxSlots;
        init => maxSlots = value;
    }

    /// <summary>
    /// How far apart the firings start: the range's slot <c>i</c>, counted from 0, starts at now plus
    /// <c>i × Spacing</c>. Defaults to zero, which starts every slot now. A negative spacing is refused.
    /// </summary>
    public TimeSpan Spacing { get; init; }

    /// <summary>
    /// The execution group the firings count against, or <see langword="null" /> — the default — for the
    /// original trigger's. A name, stored as written: braces are not placeholders here.
    /// </summary>
    /// <seealso cref="ITrigger.ExecutionGroup" />
    public string? ExecutionGroup { get; init; }
}
