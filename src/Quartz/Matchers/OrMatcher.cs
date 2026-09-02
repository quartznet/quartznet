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
/// Matches using an OR operator on two Matcher operands.
/// </summary>
/// <seealso cref="Matchers.Or{TKey}" />
/// <author>James House</author>
/// <author>Marko Lahma (.NET)</author>
public sealed class OrMatcher<TKey> : IMatcher<TKey> where TKey : Key<TKey>
{
    internal OrMatcher(IMatcher<TKey> leftOperand, IMatcher<TKey> rightOperand)
    {
        if (leftOperand is null || rightOperand is null)
        {
            Throw.ArgumentException("Two non-null operands required!");
        }

        LeftOperand = leftOperand;
        RightOperand = rightOperand;
    }

    /// <inheritdoc />
    public bool IsMatch(TKey key)
    {
        return LeftOperand.IsMatch(key) || RightOperand.IsMatch(key);
    }

    /// <summary>
    /// The first of the two matchers, and the only one consulted when it matches.
    /// </summary>
    public IMatcher<TKey> LeftOperand { get; private set; } = null!;

    /// <summary>
    /// The second matcher, consulted when the first does not match.
    /// </summary>
    public IMatcher<TKey> RightOperand { get; private set; } = null!;

    /// <inheritdoc />
    public override int GetHashCode()
    {
        const int prime = 31;
        int result = 1;
        result = prime * result + (LeftOperand?.GetHashCode() ?? 0);
        result = prime * result + (RightOperand?.GetHashCode() ?? 0);
        return result;
    }

    /// <inheritdoc />
    public override bool Equals(object? obj)
    {
        if (ReferenceEquals(this, obj))
        {
            return true;
        }
        if (obj is null)
        {
            return false;
        }
        if (GetType() != obj.GetType())
        {
            return false;
        }
        OrMatcher<TKey> other = (OrMatcher<TKey>) obj;
        if (LeftOperand is null)
        {
            if (other.LeftOperand is not null)
            {
                return false;
            }
        }
        else if (!LeftOperand.Equals(other.LeftOperand))
        {
            return false;
        }
        if (RightOperand is null)
        {
            if (other.RightOperand is not null)
            {
                return false;
            }
        }
        else if (!RightOperand.Equals(other.RightOperand))
        {
            return false;
        }
        return true;
    }
}