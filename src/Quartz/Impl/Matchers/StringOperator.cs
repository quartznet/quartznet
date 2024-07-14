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

namespace Quartz.Impl.Matchers;

/// <summary>
/// Operators available for comparing string values.
/// </summary>
[Serializable]
public abstract class StringOperator : IEquatable<StringOperator>
{
    public static readonly StringOperator Equality = new EqualityOperator();
    public static readonly StringOperator StartsWith = new StartsWithOperator();
    public static readonly StringOperator EndsWith = new EndsWithOperator();
    public static readonly StringOperator Contains = new ContainsOperator();
    public static readonly StringOperator Anything = new AnythingOperator();

    public abstract bool Evaluate(string value, string compareTo);

    [Serializable]
    private sealed class EqualityOperator : StringOperator
    {
        public override bool Evaluate(string value, string compareTo)
        {
            return value == compareTo;
        }
    }

    [Serializable]
    private sealed class StartsWithOperator : StringOperator
    {
        public override bool Evaluate(string value, string compareTo)
        {
            return value is not null && value.StartsWith(compareTo);
        }
    }

    [Serializable]
    private sealed class EndsWithOperator : StringOperator
    {
        public override bool Evaluate(string value, string compareTo)
        {
            return value is not null && value.EndsWith(compareTo);
        }
    }

    [Serializable]
    private sealed class ContainsOperator : StringOperator
    {
        public override bool Evaluate(string value, string compareTo)
        {
            return value is not null && value.Contains(compareTo);
        }
    }

    [Serializable]
    private sealed class AnythingOperator : StringOperator
    {
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
    /// equals that of <paramref name="obj"/>; otherwise, <see langword="true"/>.
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
    /// that of <paramref name="other"/>; otherwise, <see langword="true"/>.
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
}