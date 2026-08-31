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
/// The entry point for building matchers: the roots live here as static factories, and the
/// combinators — <see cref="And{TKey}" />, <see cref="Or{TKey}" /> and <see cref="Not{TKey}" /> —
/// are extension methods on any <see cref="IMatcher{TKey}" />, so an expression reads left to
/// right:
/// </summary>
/// <remarks>
/// <para>
/// <see cref="GroupMatcher{TKey}" /> and <see cref="NameMatcher{TKey}" /> also carry factories of
/// their own — <c>GroupEquals</c>, <c>NameStartsWith</c>, … — and that split is deliberate rather
/// than an unfinished move. The two answer different questions. A factory on the concrete type
/// <em>names</em> its comparison, so a call site that knows which comparison it wants reads as a
/// sentence and returns the concrete type the scheduler members that take a
/// <see cref="GroupMatcher{TKey}" /> require. The roots here take the comparison as a
/// <see cref="StringOperator" /> <em>value</em>, which is what a caller who read the operator from
/// configuration, from a query string or off the wire actually holds — the HTTP API builds every
/// matcher it receives this way — and there is no way to spell that with a method per operator.
/// </para>
/// <para>
/// What does not live on a concrete type is a root that names no comparison:
/// <see cref="AllJobs" />, <see cref="AllTriggers" /> and <see cref="Key(JobKey)" /> have nothing to
/// name themselves after, so they are built here and nowhere else.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// IMatcher&lt;JobKey&gt; matcher = Matchers.Group&lt;JobKey&gt;(StringOperator.StartsWith, "reporting")
///     .And(Matchers.Name&lt;JobKey&gt;(StringOperator.Contains, "cleanup"))
///     .Not();
/// </code>
/// </example>
/// <seealso cref="GroupMatcher{TKey}" />
/// <seealso cref="NameMatcher{TKey}" />
/// <seealso cref="StringOperator" />
public static class Matchers
{
    /// <summary>
    /// A matcher that matches every job.
    /// </summary>
    public static EverythingMatcher<JobKey> AllJobs()
    {
        return EverythingMatcher<JobKey>.All();
    }

    /// <summary>
    /// A matcher that matches every trigger.
    /// </summary>
    public static EverythingMatcher<TriggerKey> AllTriggers()
    {
        return EverythingMatcher<TriggerKey>.All();
    }

    /// <summary>
    /// A matcher that matches exactly the job with the given key.
    /// </summary>
    public static KeyMatcher<JobKey> Key(JobKey key)
    {
        ArgumentNullException.ThrowIfNull(key);
        return new KeyMatcher<JobKey>(key);
    }

    /// <summary>
    /// A matcher that matches exactly the trigger with the given key.
    /// </summary>
    public static KeyMatcher<TriggerKey> Key(TriggerKey key)
    {
        ArgumentNullException.ThrowIfNull(key);
        return new KeyMatcher<TriggerKey>(key);
    }

    /// <summary>
    /// A matcher that compares the group of a key with the given value.
    /// </summary>
    /// <typeparam name="TKey">Which kind of key to match: <see cref="JobKey" /> or <see cref="TriggerKey" />.</typeparam>
    /// <param name="matchOperator">How to compare, e.g. <see cref="StringOperator.Equality" /> or <see cref="StringOperator.StartsWith" />.</param>
    /// <param name="compareTo">The value to compare the group with.</param>
    public static GroupMatcher<TKey> Group<TKey>(StringOperator matchOperator, string compareTo) where TKey : Key<TKey>
    {
        ArgumentNullException.ThrowIfNull(matchOperator);
        ArgumentNullException.ThrowIfNull(compareTo);
        return new GroupMatcher<TKey>(compareTo, matchOperator);
    }

    /// <summary>
    /// A matcher that compares the name of a key with the given value.
    /// </summary>
    /// <typeparam name="TKey">Which kind of key to match: <see cref="JobKey" /> or <see cref="TriggerKey" />.</typeparam>
    /// <param name="matchOperator">How to compare, e.g. <see cref="StringOperator.Equality" /> or <see cref="StringOperator.StartsWith" />.</param>
    /// <param name="compareTo">The value to compare the name with.</param>
    public static NameMatcher<TKey> Name<TKey>(StringOperator matchOperator, string compareTo) where TKey : Key<TKey>
    {
        ArgumentNullException.ThrowIfNull(matchOperator);
        ArgumentNullException.ThrowIfNull(compareTo);
        return new NameMatcher<TKey>(compareTo, matchOperator);
    }

    /// <summary>
    /// A matcher that matches when both this matcher and <paramref name="other" /> match.
    /// </summary>
    public static AndMatcher<TKey> And<TKey>(this IMatcher<TKey> matcher, IMatcher<TKey> other) where TKey : Key<TKey>
    {
        return new AndMatcher<TKey>(matcher, other);
    }

    /// <summary>
    /// A matcher that matches when this matcher, <paramref name="other" />, or both match.
    /// </summary>
    public static OrMatcher<TKey> Or<TKey>(this IMatcher<TKey> matcher, IMatcher<TKey> other) where TKey : Key<TKey>
    {
        return new OrMatcher<TKey>(matcher, other);
    }

    /// <summary>
    /// A matcher that matches when this matcher does not.
    /// </summary>
    public static NotMatcher<TKey> Not<TKey>(this IMatcher<TKey> matcher) where TKey : Key<TKey>
    {
        return new NotMatcher<TKey>(matcher);
    }
}
