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
