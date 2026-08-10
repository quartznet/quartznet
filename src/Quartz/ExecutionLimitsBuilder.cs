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
/// Builds the per-node <see cref="ExecutionLimits"/> a scheduler applies when it acquires triggers.
/// </summary>
/// <remarks>
/// <para>
/// The builder is mutable and the <see cref="ExecutionLimits"/> that <see cref="Build"/> returns is
/// not, so a snapshot handed to <see cref="IScheduler.SetExecutionLimits"/> cannot change underneath
/// the scheduler thread that reads it.
/// </para>
/// <para>
/// <see cref="IQuartzBuilder.UseExecutionLimits"/> hands one of these to a callback, which is the
/// usual way to configure limits.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// ExecutionLimits limits = ExecutionLimitsBuilder.Create()
///     .ForGroup("high-cpu", 2)
///     .ForOtherGroups(5)
///     .Build();
/// </code>
/// </example>
public sealed class ExecutionLimitsBuilder
{
    private readonly Dictionary<string, int?> limits = new(StringComparer.Ordinal);

    internal ExecutionLimitsBuilder()
    {
    }

    /// <summary>
    /// Create an ExecutionLimitsBuilder with no limits configured.
    /// </summary>
    /// <returns>the new ExecutionLimitsBuilder</returns>
    public static ExecutionLimitsBuilder Create()
    {
        return new ExecutionLimitsBuilder();
    }

    /// <summary>
    /// Set the concurrency limit for a named execution group.
    /// </summary>
    /// <param name="group">The execution group name.</param>
    /// <param name="maxConcurrent">Maximum concurrent threads (must be &gt;= 0), or <c>0</c> to forbid execution.</param>
    /// <returns>This builder for fluent chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="group"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="group"/> is a reserved name.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxConcurrent"/> is negative.</exception>
    public ExecutionLimitsBuilder ForGroup(string group, int maxConcurrent)
    {
        limits[RequireGroupName(group)] = RequireNonNegative(maxConcurrent);
        return this;
    }

    /// <summary>
    /// Set the concurrency limit for triggers that have no execution group.
    /// </summary>
    /// <param name="maxConcurrent">Maximum concurrent threads (must be &gt;= 0), or <c>0</c> to forbid execution.</param>
    /// <returns>This builder for fluent chaining.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxConcurrent"/> is negative.</exception>
    public ExecutionLimitsBuilder ForDefaultGroup(int maxConcurrent)
    {
        limits[ExecutionLimits.DefaultGroupKey] = RequireNonNegative(maxConcurrent);
        return this;
    }

    /// <summary>
    /// Set the default concurrency limit applied to any execution group not explicitly configured.
    /// </summary>
    /// <param name="maxConcurrent">Maximum concurrent threads (must be &gt;= 0), or <c>0</c> to forbid execution.</param>
    /// <returns>This builder for fluent chaining.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxConcurrent"/> is negative.</exception>
    public ExecutionLimitsBuilder ForOtherGroups(int maxConcurrent)
    {
        limits[ExecutionLimits.OtherGroups] = RequireNonNegative(maxConcurrent);
        return this;
    }

    /// <summary>
    /// Mark a group as having no concurrency limit (unlimited).
    /// </summary>
    /// <remarks>
    /// This is not the same as leaving the group out: an unlisted group falls back to
    /// <see cref="ForOtherGroups"/>, while an explicitly unlimited one does not.
    /// </remarks>
    /// <param name="group">The execution group name.</param>
    /// <returns>This builder for fluent chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="group"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="group"/> is a reserved name.</exception>
    public ExecutionLimitsBuilder Unlimited(string group)
    {
        limits[RequireGroupName(group)] = null;
        return this;
    }

    /// <summary>
    /// Takes an immutable snapshot of what has been configured so far.
    /// </summary>
    public ExecutionLimits Build()
    {
        return new ExecutionLimits(new Dictionary<string, int?>(limits, StringComparer.Ordinal));
    }

    private static string RequireGroupName(string group)
    {
        ArgumentNullException.ThrowIfNull(group);
        string trimmed = group.Trim();

        if (ExecutionLimits.IsReservedGroupName(trimmed))
        {
            throw new ArgumentException(
                $"Group name '{trimmed}' is reserved. Use ForDefaultGroup() for the default group or ForOtherGroups() for the catch-all.",
                nameof(group));
        }

        return trimmed;
    }

    private static int RequireNonNegative(int maxConcurrent)
    {
        if (maxConcurrent < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxConcurrent), maxConcurrent, "Execution limit must be non-negative.");
        }

        return maxConcurrent;
    }
}
