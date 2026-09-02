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
/// Operators available for comparing string values.
/// </summary>
/// <remarks>
/// <para>
/// The five shipped operators work on every store. An operator of your own works on
/// <see cref="Quartz.Impl.RAMJobStore" />, where matching is <see cref="Evaluate" /> over the values
/// in memory — and on no persistent store, where a matcher becomes a SQL <c>LIKE</c> pattern and the
/// translation recognises the five by identity. A query carrying an operator it does not recognise is
/// refused, at query time, naming the operator.
/// </para>
/// <para>
/// So a matcher that passes its unit test can fail in production. Derive from
/// <see cref="StringOperator" /> for a scheduler you know is in memory; for one that is not, compose
/// the shipped operators with <see cref="Matchers" /> instead, or filter the result of a wider query.
/// </para>
/// </remarks>
public abstract class StringOperator : IEquatable<StringOperator>
{
    /// <summary>
    /// Matches when the value equals the compared string exactly (ordinal).
    /// </summary>
    public static StringOperator Equality { get; } = new EqualityOperator();

    /// <summary>
    /// Matches when the value starts with the compared string.
    /// </summary>
    public static StringOperator StartsWith { get; } = new StartsWithOperator();

    /// <summary>
    /// Matches when the value ends with the compared string.
    /// </summary>
    public static StringOperator EndsWith { get; } = new EndsWithOperator();

    /// <summary>
    /// Matches when the value contains the compared string.
    /// </summary>
    public static StringOperator Contains { get; } = new ContainsOperator();

    /// <summary>
    /// Matches every value; the compared string is ignored.
    /// </summary>
    public static StringOperator Anything { get; } = new AnythingOperator();

    /// <summary>
    /// The name that discriminates this operator: <c>"Equality"</c>, <c>"StartsWith"</c>,
    /// <c>"EndsWith"</c>, <c>"Contains"</c> or <c>"Anything"</c> for the built-in operators.
    /// It is how an operator is identified when a matcher crosses a process boundary — for
    /// example as the HTTP API's <c>groupStartsWith</c>-style query parameters — so a custom
    /// operator's name should be unique and stable.
    /// </summary>
    public abstract string Name { get; }

    /// <summary>
    /// Whether <paramref name="value" /> matches <paramref name="compareTo" /> under this operator.
    /// </summary>
    /// <param name="value">The value being matched — a job or trigger group, or a calendar name.</param>
    /// <param name="compareTo">What the matcher was built with.</param>
    public abstract bool Evaluate(string value, string compareTo);

    private sealed class EqualityOperator : StringOperator
    {
        public override string Name => "Equality";

        public override bool Evaluate(string value, string compareTo)
        {
            return value == compareTo;
        }
    }

    private sealed class StartsWithOperator : StringOperator
    {
        public override string Name => "StartsWith";

        public override bool Evaluate(string value, string compareTo)
        {
            return value is not null && value.StartsWith(compareTo);
        }
    }

    private sealed class EndsWithOperator : StringOperator
    {
        public override string Name => "EndsWith";

        public override bool Evaluate(string value, string compareTo)
        {
            return value is not null && value.EndsWith(compareTo);
        }
    }

    private sealed class ContainsOperator : StringOperator
    {
        public override string Name => "Contains";

        public override bool Evaluate(string value, string compareTo)
        {
            return value is not null && value.Contains(compareTo);
        }
    }

    private sealed class AnythingOperator : StringOperator
    {
        public override string Name => "Anything";

        public override bool Evaluate(string value, string compareTo)
        {
            return true;
        }
    }

    /// <summary>
    /// Returns a value indicating whether this instance and a specified <see cref="object"/> are considered
    /// equal.
    /// </summary>
    /// <param name="obj">An <see cref="object"/> to compare with this instance.</param>
    /// <returns>
    /// <see langword="true"/> if the current <see cref="StringOperator"/> and <paramref name="obj"/>
    /// are the same instance, or the <see cref="Type"/> of the current <see cref="StringOperator"/>
    /// equals that of <paramref name="obj"/>; otherwise, <see langword="false"/>.
    /// </returns>
    public override bool Equals(object? obj)
    {
        return Equals(obj as StringOperator);
    }

    /// <summary>
    /// Returns a value indicating whether this instance and a specified <see cref="StringOperator"/>
    /// instance are considered equal.
    /// </summary>
    /// <param name="other">An <see cref="StringOperator"/> to compare with this instance.</param>
    /// <returns>
    /// <see langword="true"/> if the current <see cref="StringOperator"/> and <paramref name="other"/>
    /// are the same instance, or the <see cref="Type"/> of the current <see cref="StringOperator"/> equals
    /// that of <paramref name="other"/>; otherwise, <see langword="false"/>.
    /// </returns>
    public virtual bool Equals(StringOperator? other)
    {
        return other is not null && GetType() == other.GetType();
    }

    /// <summary>
    /// Returns the hash code for the <see cref="StringOperator"/>.
    /// </summary>
    /// <returns>
    /// The hash code of the <see cref="Type"/> of the current <see cref="StringOperator"/>
    /// instance.
    /// </returns>
    public override int GetHashCode()
    {
        return GetType().GetHashCode();
    }

    /// <summary>
    /// Returns the operator's <see cref="Name" />.
    /// </summary>
    public override string ToString()
    {
        return Name;
    }
}