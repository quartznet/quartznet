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
/// The identity the dashboard and the HTTP API address a scheduler by: its name, and the target it is
/// reached through when it is reached through one.
/// </summary>
/// <remarks>
/// <para>
/// Spelled <c>target/name</c> — <c>prod/reporting</c> for a window onto the store attached as
/// <c>prod</c>, <c>w1/QuartzScheduler</c> for the scheduler the HTTP target <c>w1</c> fronts — and as
/// the bare name for a scheduler that is reached through no target: one of this process, or one
/// <c>AddQuartzHttpClient</c> registered without a <c>Target</c>. A bare key never resolves to a targeted
/// scheduler, so a deployment that fronts one process per name sees nothing change, and two targets may
/// front schedulers of one name without either of them taking the bare key.
/// </para>
/// <para>
/// Public because a replacement <c>IQuartzApiClient</c> receives keys and needs the one rule for reading
/// them. <see cref="SchedulerName" /> is positional and <see cref="Target" /> defaults to
/// <see langword="null" />, so <c>new SchedulerRef("x")</c> is the bare form.
/// </para>
/// </remarks>
/// <param name="SchedulerName">The scheduler's name, as its own process spells it.</param>
/// <param name="Target">
/// The target the scheduler is reached through, or <see langword="null" /> for one reached through
/// none. A target name never contains <see cref="Separator" />.
/// </param>
public sealed record SchedulerRef(string SchedulerName, string? Target = null)
{
    /// <summary>
    /// What separates the target from the scheduler's name in a key.
    /// </summary>
    public const char Separator = '/';

    private readonly string schedulerName = RequireName(SchedulerName);
    private readonly string? target = RequireTarget(Target);

    /// <summary>
    /// The scheduler's name, as its own process spells it.
    /// </summary>
    public string SchedulerName
    {
        get => schedulerName;
        init => schedulerName = RequireName(value);
    }

    /// <summary>
    /// The target the scheduler is reached through, or <see langword="null" /> for one reached through
    /// none.
    /// </summary>
    /// <remarks>
    /// Validated on every write, <c>with</c> included: a target containing the separator would make a key
    /// that could not be read back.
    /// </remarks>
    public string? Target
    {
        get => target;
        init => target = RequireTarget(value);
    }

    /// <summary>
    /// <c>target/name</c>, or the bare name when <see cref="Target" /> is <see langword="null" />.
    /// </summary>
    public string Key => Target is null ? SchedulerName : string.Concat(Target, Separator, SchedulerName);

    /// <summary>
    /// Reads a key: the part before the first <see cref="Separator" /> is the target and the rest is the
    /// scheduler's name. A key with no separator is a bare name, and so is one that starts or ends with
    /// it, since neither half of such a key could be a target.
    /// </summary>
    /// <remarks>
    /// <c>a/b/c</c> is the scheduler <c>b/c</c> behind the target <c>a</c>: a target name never contains
    /// the separator, and a scheduler's may.
    /// </remarks>
    /// <param name="key">The key to read.</param>
    /// <exception cref="ArgumentException"><paramref name="key" /> is null or white space.</exception>
    public static SchedulerRef Parse(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        int separator = key.IndexOf(Separator, StringComparison.Ordinal);
        if (separator <= 0 || separator == key.Length - 1)
        {
            return new SchedulerRef(key);
        }

        string target = key.Substring(0, separator);
        string schedulerName = key.Substring(separator + 1);
        if (string.IsNullOrWhiteSpace(target) || string.IsNullOrWhiteSpace(schedulerName))
        {
            return new SchedulerRef(key);
        }

        return new SchedulerRef(schedulerName, target);
    }

    /// <inheritdoc cref="Key" />
    public override string ToString()
    {
        return Key;
    }

    private static string RequireName(string schedulerName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(schedulerName);
        return schedulerName;
    }

    private static string? RequireTarget(string? target)
    {
        if (target is null)
        {
            return null;
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(target);

        if (target.Contains(Separator, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"'{target}' cannot be a target name: '{Separator}' is what separates a target from the scheduler's name in a key.",
                nameof(target));
        }

        return target;
    }
}
