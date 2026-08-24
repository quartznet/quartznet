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

using System.Diagnostics.CodeAnalysis;

namespace Quartz;

/// <summary>
/// Uniquely identifies a <see cref="IJobDetail" />.
/// </summary>
/// <remarks>
/// <para>Keys are composed of both a name and group, and the name must be unique
/// within the group.  If only a group is specified then the default group
/// name will be used.</para>
///
/// <para>Quartz provides a builder-style API for constructing scheduling-related
/// entities via a Domain-Specific Language (DSL).  The DSL can best be
/// utilized through the usage of static imports of the methods on the classes
/// <see cref="TriggerBuilder" />, <see cref="JobBuilder" />,
/// <see cref="DateBuilder" />, <see cref="JobKey" />, <see cref="TriggerKey" />
/// and the various <see cref="IScheduleBuilder" /> implementations.</para>
///
/// <para>Client code can then use the DSL to write code such as this:</para>
/// <code>
/// IJobDetail job = JobBuilder.Create&lt;MyJob>()
///     .WithIdentity("myJob")
///     .Build();
/// ITrigger trigger = TriggerBuilder.Create()
///     .WithIdentity("myTrigger", "myTriggerGroup")
///     .WithSimpleSchedule(x => x
///         .WithInterval(TimeSpan.FromHours(1))
///         .RepeatForever())
///     .StartAt(DateTimeOffset.UtcNow.AddMinutes(10))
///     .Build();
/// scheduler.scheduleJob(job, trigger);
/// </code>
/// </remarks>
/// <seealso cref="IJob"/>
/// <seealso cref="Key{T}.DefaultGroup" />
[Serializable]
public sealed class JobKey : Key<JobKey>, IComparable<JobKey>, IEquatable<JobKey>, IParsable<JobKey>
{
    public JobKey(string name) : base(name)
    {
    }

    public JobKey(string name, string group) : base(name, group)
    {
    }

    public bool Equals(JobKey? other)
    {
        return other is not null && (ReferenceEquals(this, other) || (Group == other.Group && Name == other.Name));
    }

    /// <inheritdoc cref="Equals(JobKey?)" />
    public override bool Equals(object? obj)
    {
        return Equals(obj as JobKey);
    }

    /// <inheritdoc cref="Key{T}.CompareTo(Key{T})" />
    /// <remarks>
    /// Declared here as well as on the base so that <see cref="Comparer{T}.Default" /> finds a
    /// comparison of <em>this</em> type: the inherited one is over <c>Key&lt;JobKey&gt;</c>, which the
    /// default comparer does not recognise, and without this <c>List&lt;JobKey&gt;.Sort()</c>,
    /// <c>OrderBy(k =&gt; k)</c> and <see cref="SortedSet{T}" /> throw at runtime while compiling fine.
    /// </remarks>
    public int CompareTo(JobKey? other)
    {
        return base.CompareTo(other);
    }

    /// <inheritdoc />
    public override int GetHashCode()
    {
        return base.GetHashCode();
    }

    /// <summary>
    /// Parses the <c>&lt;group&gt;.&lt;name&gt;</c> form <see cref="Key{T}.ToString" /> produces —
    /// <c>"DEFAULT.my.job"</c> parses to group <c>DEFAULT</c>, name <c>my.job</c>.
    /// </summary>
    /// <remarks>
    /// The string splits at the first '.', the exact inverse of how <see cref="Key{T}.ToString" />
    /// composes it. A <em>group</em> containing '.' is the ambiguous case: such a key parses at the
    /// first dot, which is not the key it printed from.
    /// </remarks>
    /// <param name="s">The composed key string.</param>
    /// <exception cref="ArgumentNullException"><paramref name="s"/> is <see langword="null" />.</exception>
    /// <exception cref="FormatException"><paramref name="s"/> contains no '.'.</exception>
    public static JobKey Parse(string s)
    {
        ArgumentNullException.ThrowIfNull(s);

        if (!TryParse(s, out JobKey? result))
        {
            Throw.FormatException($"'{s}' is not a '<group>.<name>' job key.");
        }

        return result;
    }

    /// <inheritdoc cref="Parse(string)" />
    public static bool TryParse([NotNullWhen(true)] string? s, [MaybeNullWhen(false)] out JobKey result)
    {
        if (!TryParseParts(s, out string name, out string group))
        {
            result = null;
            return false;
        }

        result = new JobKey(name, group);
        return true;
    }

    static JobKey IParsable<JobKey>.Parse(string s, IFormatProvider? provider)
    {
        return Parse(s);
    }

    static bool IParsable<JobKey>.TryParse(string? s, IFormatProvider? provider, [MaybeNullWhen(false)] out JobKey result)
    {
        return TryParse(s, out result);
    }
}