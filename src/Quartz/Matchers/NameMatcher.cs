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

using Quartz.Util;

namespace Quartz;

/// <summary>
/// Matches on name (ignores group) property of Keys.
/// </summary>
/// <remarks>
/// The key-typed half of the name filter. Its arity-free twin, <see cref="NameMatcher" />, is the
/// same four comparisons over a name that belongs to no key — a calendar's name, a group's name.
/// </remarks>
/// <author>James House</author>
/// <author>Marko Lahma (.NET)</author>
public sealed class NameMatcher<TKey> : StringMatcher<TKey> where TKey : Key<TKey>
{
    internal NameMatcher(string compareTo, StringOperator compareWith) : base(compareTo, compareWith)
    {
    }

    /// <summary>
    /// Create a NameMatcher that matches names equaling the given string.
    /// </summary>
    /// <param name="compareTo"></param>
    /// <returns></returns>
    public static NameMatcher<TKey> NameEquals(string compareTo)
    {
        return new NameMatcher<TKey>(compareTo, StringOperator.Equality);
    }

    /// <summary>
    /// Create a NameMatcher that matches names starting with the given string.
    /// </summary>
    /// <param name="compareTo"></param>
    /// <returns></returns>
    public static NameMatcher<TKey> NameStartsWith(string compareTo)
    {
        return new NameMatcher<TKey>(compareTo, StringOperator.StartsWith);
    }

    /// <summary>
    /// Create a NameMatcher that matches names ending with the given string.
    /// </summary>
    /// <param name="compareTo"></param>
    /// <returns></returns>
    public static NameMatcher<TKey> NameEndsWith(string compareTo)
    {
        return new NameMatcher<TKey>(compareTo, StringOperator.EndsWith);
    }

    /// <summary>
    /// Create a NameMatcher that matches names containing the given string.
    /// </summary>
    /// <param name="compareTo"></param>
    /// <returns></returns>
    public static NameMatcher<TKey> NameContains(string compareTo)
    {
        return new NameMatcher<TKey>(compareTo, StringOperator.Contains);
    }

    protected override string GetValue(TKey key)
    {
        return key.Name;
    }
}

/// <summary>
/// Matches a name that belongs to no key: a calendar's name, or a group's.
/// </summary>
/// <remarks>
/// <para>
/// The same four comparisons as <see cref="NameMatcher{TKey}" />, over a bare string. Jobs and
/// triggers are identified by a <see cref="Key{T}" />, so their names are matched by the generic
/// form, which is written against that key; a calendar and a group are identified by a name alone —
/// no group, no key type — so they are matched by this one rather than by a key type invented to
/// satisfy a generic constraint.
/// </para>
/// <para>
/// It is deliberately not an <see cref="IMatcher{T}" />: that interface is constrained to
/// <c>Key&lt;T&gt;</c>, which is what lets a matcher be handed to the scheduler members that take
/// one, and a name is not a key. The combinators — <see cref="Matchers.And{TKey}" />,
/// <see cref="Matchers.Or{TKey}" />, <see cref="Matchers.Not{TKey}" /> — are constrained the same
/// way, and neither of the two filters this matcher serves has ever needed them.
/// </para>
/// <para>
/// There is no "any name" factory, because every property that takes one is nullable and null
/// already means every name.
/// </para>
/// </remarks>
/// <seealso cref="CalendarQuery.Name" />
/// <seealso cref="JobGroupQuery.Name" />
/// <seealso cref="TriggerGroupQuery.Name" />
public sealed class NameMatcher : IEquatable<NameMatcher>
{
    private NameMatcher(string compareTo, StringOperator compareWith)
    {
        if (compareTo is null)
        {
            Throw.ArgumentNullException(nameof(compareTo), "CompareTo value cannot be null!");
        }

        CompareToValue = compareTo;
        CompareWithOperator = compareWith;
    }

    /// <summary>
    /// The string the name is compared against.
    /// </summary>
    public string CompareToValue { get; }

    /// <summary>
    /// How the name is compared against <see cref="CompareToValue" />.
    /// </summary>
    public StringOperator CompareWithOperator { get; }

    /// <summary>
    /// Matches names equaling the given string.
    /// </summary>
    public static NameMatcher NameEquals(string compareTo) => new NameMatcher(compareTo, StringOperator.Equality);

    /// <summary>
    /// Matches names starting with the given string.
    /// </summary>
    public static NameMatcher NameStartsWith(string compareTo) => new NameMatcher(compareTo, StringOperator.StartsWith);

    /// <summary>
    /// Matches names ending with the given string.
    /// </summary>
    public static NameMatcher NameEndsWith(string compareTo) => new NameMatcher(compareTo, StringOperator.EndsWith);

    /// <summary>
    /// Matches names containing the given string.
    /// </summary>
    public static NameMatcher NameContains(string compareTo) => new NameMatcher(compareTo, StringOperator.Contains);

    /// <summary>
    /// Whether the given name matches.
    /// </summary>
    public bool IsMatch(string name) => CompareWithOperator.Evaluate(name, CompareToValue);

    public bool Equals(NameMatcher? other)
    {
        if (ReferenceEquals(this, other))
        {
            return true;
        }

        return other is not null
               && CompareToValue == other.CompareToValue
               && CompareWithOperator.Equals(other.CompareWithOperator);
    }

    public override bool Equals(object? obj) => Equals(obj as NameMatcher);

    public override int GetHashCode() => HashCode.Combine(CompareToValue, CompareWithOperator);

    public override string ToString() => $"{CompareWithOperator}({CompareToValue})";
}