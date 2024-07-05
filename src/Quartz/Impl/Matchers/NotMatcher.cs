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

namespace Quartz.Impl.Matchers;

/// <summary>
/// Matches using an NOT operator on another Matcher.
/// </summary>
/// <author>James House</author>
/// <author>Marko Lahma (.NET)</author>
[Serializable]
public sealed class NotMatcher<TKey> : IMatcher<TKey> where TKey : Key<TKey>
{
    // ReSharper disable once UnusedMember.Local
    private NotMatcher()
    {
    }

    private NotMatcher(IMatcher<TKey> operand)
    {
        if (operand is null)
        {
            ThrowHelper.ThrowArgumentNullException(nameof(operand), "Non-null operand required!");
        }
        Operand = operand;
    }

    /// <summary>
    /// Create a NotMatcher that reverses the result of the given matcher.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="operand"></param>
    /// <returns></returns>
    public static NotMatcher<T> Not<T>(IMatcher<T> operand) where T : Key<T>
    {
        return new NotMatcher<T>(operand);
    }

    public bool IsMatch(TKey key)
    {
        return !Operand.IsMatch(key);
    }

    public IMatcher<TKey> Operand { get; private set; } = null!;

    public override int GetHashCode()
    {
        const int Prime = 31;
        int result = 1;
        result = Prime * result + (Operand is null ? 0 : Operand.GetHashCode());
        return result;
    }

    public override bool Equals(object? obj)
    {
        if (this == obj)
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
        NotMatcher<TKey> other = (NotMatcher<TKey>) obj;
        if (Operand is null)
        {
            if (other.Operand is not null)
            {
                return false;
            }
        }
        else if (!Operand.Equals(other.Operand))
        {
            return false;
        }
        return true;
    }
}